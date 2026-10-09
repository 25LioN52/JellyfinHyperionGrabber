# 0008: Streaming sessions pace frames with a frame-rate timer and drop late frames

**Status:** Accepted (2026-10-05)

## Context

The frame source ([ADR 0007](0007-ffmpeg-frame-source.md)) yields frames with media positions; something must decide
when each frame goes to Hyperion. The client only reports its position now and then (Jellyfin for Kodi: in whole
seconds, on pause/resume/seek and about every 4 minutes), Hyperion or the network can be slow, the decoder can be slow (CPU decoding
of 4K HEVC), and Hyperion.ng and HyperHDR close a FlatBuffers connection that stays silent (5 s by default).
Requirements: frames at the configured rate, never a queue of stale frames, no per-frame allocations, a bounded stop.

Options considered:

- **Send every frame as soon as it is decoded:** FFmpeg decodes faster than real time, so the lights would run
  ahead of the picture.
- **Wait per frame until it is due** (`Task.Delay` per frame): precise, but a timer and a task per frame, and pause,
  keep-alive and seeks each need their own wake-up path.
- **A sender task fed by a one-slot "latest frame" mailbox:** decouples sending from decoding, but adds a second
  task and hand-over per frame for what a single loop can do.
- **One loop driven by a `PeriodicTimer` at the frame rate.**

## Decision

Each playback gets a `StreamingGrabSession` that runs one loop on its own task, ticking at the configured frame rate
(`PeriodicTimer` on the injected `TimeProvider`, default 25 fps). On every tick it:

1. Estimates the playback position (last report plus the time since, frozen while paused; since 0.4 a range that
   reports narrow, [ADR 0009](0009-position-tracking.md)), plus the configured light timing offset (seek detection
   ignores the offset).
2. Treats a report that moved the estimate by more than **1 s** as a seek and restarts FFmpeg there.
3. Takes the **newest decoded frame at or before the position** without waiting (`TryReadFrame`); older due frames
   are disposed (returned to the pool) and counted as dropped. A frame that is not due yet is held for a later tick.
4. Restarts FFmpeg at the position when the decoder lags more than **2 s** behind, but only after it delivered frames
   and had **5 s** to open and seek the file, so a slow start does not cause a restart loop.
5. Copies the frame into one session buffer, returns the pooled frame and sends the copy. If no new frame was due
   for **500 ms** (pause, end of video, slow decoder), it resends the copy so Hyperion keeps the connection open.

Hyperion is connected at the first tick that has something to send, not when the session starts: opening and seeking
a large file (a NAS disk spinning up) can take longer than Hyperion's idle timeout, which would close a connection that
has nothing to send yet. It also means Hyperion's priority is only taken once there is a picture.

A pause longer than the configured release time (default **15 s**, 0 = never) stops FFmpeg and closes the Hyperion
connection, so Hyperion shows its default; resuming starts FFmpeg at the position and reconnects with the first frame.
Holding the frame forever kept the LEDs on a still picture during long pauses, which users did not want (found in the
first real test).

A failure (Hyperion unreachable or lost, FFmpeg error) ends the stream (since 0.5 Hyperion failures are retried
instead, [ADR 0010](0010-reconnect-with-backoff.md)); disposing FFmpeg and the Hyperion connection
(which clears the priority) runs in parallel. `DisposeAsync` waits at most **5 s** and otherwise lets the release
finish in the background, so the playback monitor never stalls ([`IGrabSession`](../architecture.md#playback-monitor-m1)).

## Consequences

- At most one frame per tick reaches Hyperion and frames never queue: when sending takes longer than a tick, the
  timer coalesces the missed ticks and the next tick sends the newest due frame. A slow decoder means fewer frames.
- A frame is sent up to one tick after it is due (on average half a tick, 20 ms at 25 fps). Timer resolution on
  Windows (about 15 ms) makes the interval between frames uneven there; Hyperion's smoothing hides both.
- The steady-state loop allocates nothing of its own (measured: under 2 bytes per tick, which is noise);
  `HyperionClient.SendImageAsync` is unchanged from M0.
- While paused, FFmpeg is blocked on the full pool and Hyperion gets two small frames per second (about 85 KB/s).
- Seeks are detected from reports, so their accuracy depends on the client's reports. A global light timing offset
  cancels the constant delay (about 1 s in the first real test with Kodi and WLED); a per-device offset and a precise
  Kodi clock remain M2 work. Reconnecting after a lost connection came in 0.5 ([ADR 0010](0010-reconnect-with-backoff.md)).
- Tests drive the loop with `FakeTimeProvider`, a fake frame source and a recording connection, plus an end-to-end
  test with real FFmpeg and `FakeHyperionServer`.
