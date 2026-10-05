# Architecture

## Layers

```mermaid
flowchart TB
    subgraph Jellyfin["Jellyfin server process"]
        subgraph Plugin["Jellyfin.Plugin.HyperionGrabber (adapter)"]
            P[Plugin + config page]
            API[HyperionGrabberController]
            Mon[PlaybackMonitorService]
            Src["JellyfinFfmpegSettingsProvider<br/>(media source resolution planned, M1)"]
        end
        subgraph Core["Jellyfin.Plugin.HyperionGrabber.Core (no Jellyfin references)"]
            Tester[HyperionConnectionTester]
            Pattern[TestPattern / TestPatternPlayer]
            Monitor[PlaybackMonitor]
            Session["IGrabSession (streaming planned, M1): frame source -> sync -> sink"]
            Sync["SyncEngine (planned, M2)"]
            Frames["FfmpegFrameSource"]
            Client[HyperionClient]
            Codec[FlatBuffers codec]
        end
    end
    API --> Tester --> Client
    Tester --> Pattern --> Client
    Mon --> Monitor --> Session
    Src --> Frames
    Session --> Frames
    Session --> Sync
    Session --> Client
    Client --> Codec
    Client -- TCP 19400 --> H[(Hyperion.ng / HyperHDR)]
```

- **Core** contains everything that can be written and tested without Jellyfin: the protocol, the client, the test
  pattern, and later the frame pipeline and sync engine. It depends only on the .NET shared framework.
- **Plugin** is a thin adapter. It turns Jellyfin concepts (sessions, items, media sources, encoding options) into
  Core inputs, hosts the background services, and provides the configuration page and admin API.

See [ADR 0004](adr/0004-core-library-boundary.md) for why.

## What exists today

| Component | Responsibility |
| --- | --- |
| `FlatBufferWriter`, `HyperionRequestWriter` | Allocation-free encoding of Register, Image, Clear, Color with the size prefix |
| `FlatBufferReader`, `HyperionReplyReader` | Bounds-checked decoding of replies from the network |
| `HyperionClient` | One registered connection: connect/register with timeouts, serialized sends, reply-draining receive loop, Clear on dispose. Never reconnects itself. |
| `IHyperionSink` | Abstraction of "where frames go"; `HyperionClient` implements it, tests use `RecordingSink` |
| `TestPattern`, `TestPatternPlayer` | Layout test picture and its paced playback, always clearing afterwards |
| `HyperionConnectionTester` | Config-page actions; returns results instead of throwing |
| `HyperionGrabberController` | Admin-only `POST HyperionGrabber/TestConnection` and `/TestPattern`, `GET HyperionGrabber/Clients` (devices and users of recent sessions for the config page) |
| `FfmpegFrameSource` (M1) | Runs FFmpeg from a start position and yields pooled RGB24 `VideoFrame`s at a constant rate; hardware decoding with CPU fallback; always kills and awaits the process ([ADR 0007](adr/0007-ffmpeg-frame-source.md)) |
| `FfmpegFrameSourceOptions`, `FfmpegArguments` | Output size from the aspect ratio, validation, and the FFmpeg command line per hardware acceleration method |
| `IFfmpegSettingsProvider` / `JellyfinFfmpegSettingsProvider` | Core interface for the server's FFmpeg path and hardware settings; the adapter reads Jellyfin's encoding options |

## Playback monitor (M1)

| Component | Responsibility |
| --- | --- |
| `PlaybackEvent`, `PlaybackState` | Host-independent playback report (start/progress/stop, session, device, user, item, media source, position, paused) and the state a session sees, time-stamped with `ReportedAt` |
| `PlaybackFilter` | Enabled switch, selected device ids (none = nothing matches), selected user ids (none = every user) |
| `PlaybackMonitor` | Non-blocking `Post`/`UpdateFilter` into a bounded queue (256, drop oldest); one processing task owns all state and decides which playback drives the target |
| `IGrabSessionFactory`, `IGrabSession` | Seam for the streaming pipeline: `Start(state)`, `Update(state)`, `DisposeAsync()`. Until streaming lands, `LoggingGrabSessionFactory` only logs |
| `PlaybackMonitorService` (plugin) | `IHostedService` that subscribes to `ISessionManager.PlaybackStart/Progress/Stopped`, maps them with `PlaybackEventMapper` and feeds the saved filter (`IPlaybackFilterSource`) |

Rules the monitor implements:

- **One session per target.** There is one Hyperion target today, so at most one `IGrabSession` runs. Of all matching
  playbacks, the most recently started one drives it; when it stops, the most recent remaining one takes over with
  its last reported state. Multiple targets (M3) will make this decision per target.
- A progress report for an unknown session counts as a start (playback that began before Jellyfin or the plugin
  started, or a device that was just selected). A report with a different item restarts the session.
- A filter change is applied immediately: sessions that no longer match are disposed, playing sessions that now
  match start.
- Tracked playbacks are capped at 64 (least recently reported forgotten first), so lost stop events cannot grow
  memory.
- A session that throws is logged and does not stop the monitor; the next report retries the start.

## Planned pipeline (M1-M3)

```mermaid
flowchart LR
    Events["Jellyfin session events"] --> Monitor["PlaybackMonitor<br/>(filter devices/users)"]
    Monitor -->|start/pause/seek/stop + position| Session["GrabSession<br/>(one per target)"]
    Session --> Source["Frame source<br/>FFmpeg: -ss pos, HW decode,<br/>GPU scale, tonemap, rgb24"]
    Source -->|"frames + timestamps"| Sync["Sync engine<br/>clock = last report + elapsed<br/>+ latency offset"]
    Sync -->|"frame due now"| Sink["HyperionClient (reconnecting)"]
```

Design rules for the pipeline:

- **Bounded everything.** A small fixed pool of frame buffers; when Hyperion or the network is slow, drop frames,
  never queue them. When FFmpeg is slow, Hyperion gets fewer frames, never stale ones.
- **The pipe is the throttle.** FFmpeg writes raw RGB to stdout; when we stop reading (pause), it blocks instead of
  decoding ahead. Seeks restart FFmpeg at the new position.
- **Timestamps, not wall-clock guesses.** A constant-frame-rate filter makes frame *n* correspond to
  `start + n / fps`, so the sync engine can schedule each frame against the estimated client position.
- **One session per target** (TV/Hyperion pair); sessions are independent and cancellable.
- **Process hygiene.** FFmpeg processes are always killed and awaited when a session ends, including on errors and
  server shutdown.

## Threading

- Sends on a `HyperionClient` are serialized with a `SemaphoreSlim`; one receive loop per client drains replies.
- Everything is async with `CancellationToken`; no thread is blocked waiting on I/O.
- Jellyfin event handlers must return quickly: they only post a state change to the session, which does the work
  on its own task.
