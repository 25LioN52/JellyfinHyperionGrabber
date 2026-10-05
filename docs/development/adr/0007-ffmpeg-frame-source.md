# 0007: FFmpeg process with a pipe and a fixed buffer pool as the frame source

**Status:** Accepted (2026-10-05)

## Context

[ADR 0001](0001-server-side-decoding.md) makes the server decode the playing video a second time. That decode must
be cheap, use the GPU the administrator already set up for Jellyfin, never decode far ahead of playback, and never
leave processes behind. Options considered:

- **Jellyfin's HTTP streaming endpoints** (HLS / progressive): no FFmpeg handling of our own, but they create a
  second playback session (visible to users, counted for limits), transcode at full resolution and cannot be
  throttled frame by frame.
- **FFmpeg libraries in-process** (P/Invoke or a wrapper package): no process, but a new shipped native dependency
  per platform (rule 5), version conflicts with Jellyfin's own FFmpeg, and a native crash would take Jellyfin down.
- **Jellyfin's FFmpeg executable as a child process**, raw frames on stdout.

## Decision

Run Jellyfin's FFmpeg (`IMediaEncoder.EncoderPath`) as a child process per playback:

- Input seek (`-ss` before `-i`), first video stream only, no audio, subtitles or data.
- Hardware decoding and scaling with the method selected in Jellyfin (CUDA `scale_cuda`, QSV `scale_qsv`, VA-API
  `scale_vaapi`, RKMPP `scale_rkrga`; D3D11VA / VideoToolbox decode with CPU scaling), only for codecs the
  administrator enabled for hardware decoding. If FFmpeg fails before the first frame, restart once on the CPU.
- Scale to about 160 px wide keeping the aspect ratio, `fps` filter for a constant frame rate, `rgb24` rawvideo to
  `pipe:1`. Frame *n* then shows `start + n / fps`.
- Read frames into a **fixed pool** of buffers (default 3). The reader only reads FFmpeg's stdout while a buffer is
  free, so a slow or paused consumer fills the pipe and blocks FFmpeg: no unbounded decoding ahead.
- The process is always killed (whole tree) and awaited on end of stream, error and disposal; stderr is drained
  continuously and its last lines become the error message.

## Consequences

- No new shipped dependency; we get every codec and hardware path Jellyfin's FFmpeg build supports, and FFmpeg
  crashes are isolated from Jellyfin.
- Starting a process costs tens of milliseconds per playback start and seek, which is acceptable for M1/M2.
- We depend on FFmpeg filter names (`scale_cuda`, `scale_qsv`, ...) present in jellyfin-ffmpeg; failures fall back to
  the CPU and are logged as warnings rather than breaking playback lighting.
- On Windows, .NET reads child-process pipes synchronously on a thread-pool thread; disposal kills the process to
  unblock a pending read. Small runtime allocations per pipe read remain (about 150 bytes per frame on Linux,
  350 on Windows, measured); frame buffers are never allocated per frame.
- Seeking means restarting FFmpeg at the new position (the session does that, M2).
