# Architecture decision records

Short records of decisions that shape the project: the context, the decision and its consequences. Add a new record
(next number, same structure) when a change makes a decision that future contributors would otherwise question or
accidentally undo. Superseded records stay, marked *Superseded by ...*.

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-server-side-decoding.md) | Decode the video on the Jellyfin server instead of capturing the screen | Accepted |
| [0002](0002-flatbuffers-transport.md) | Talk FlatBuffers RawImage to Hyperion with a hand-written, verified codec | Accepted |
| [0003](0003-jellyfin-version-support.md) | Support Jellyfin 10.11 and 12 from one codebase and one repository URL | Accepted |
| [0004](0004-core-library-boundary.md) | Keep all logic in a Core library without Jellyfin references | Accepted |
| [0005](0005-documentation-tooling.md) | Docs as Markdown in the repository, built with Material for MkDocs | Accepted |
| [0006](0006-release-automation.md) | release-please releases; plugin manifest generated from release assets | Accepted |
| [0007](0007-ffmpeg-frame-source.md) | Decode with Jellyfin's FFmpeg as a child process, raw frames over a pipe into a fixed buffer pool | Accepted |
| [0008](0008-streaming-session-pacing.md) | Pace frames with a frame-rate timer, send the newest due frame, drop late ones | Accepted |
| [0009](0009-position-tracking.md) | Track the playback position as a range that every report narrows | Accepted |
| [0010](0010-reconnect-with-backoff.md) | Reconnect to Hyperion with backoff from a connect task the ticks check; never stall pacing | Accepted |
| [0011](0011-kodi-clock.md) | Read the playback position from Kodi over JSON-RPC | Accepted |
| [0012](0012-hdr-tone-mapping.md) | Tone map HDR to SDR after scaling, on the CPU, with the best filter Jellyfin's FFmpeg has | Accepted |
| [0013](0013-black-bars-and-aspect-ratio.md) | Send the display aspect ratio; leave black bars to Hyperion's detector | Accepted |
| [0014](0014-multiple-targets.md) | Several targets, each a Hyperion server and priority with its own devices | Accepted |
