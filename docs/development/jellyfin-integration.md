# Jellyfin integration

## Supported lines, ABI and versions

| Jellyfin | Target framework | Compiled against | `targetAbi` | Plugin version |
| --- | --- | --- | --- | --- |
| 10.11.x | `net9.0` | `Jellyfin.Controller` 10.11.0 | `10.11.0.0` | `x.y.z.0` |
| 12.x | `net10.0` | `Jellyfin.Controller` 12.0.0 | `12.0.0.0` | `x.y.z.1` |

- Jellyfin 12.0 (September 2026) moved to .NET 10 and changed plugin interfaces; plugins built for 10.11 do not load
  on 12. Jellyfin 10.11 is still widely deployed, so we build both from one codebase
  ([ADR 0003](adr/0003-jellyfin-version-support.md)).
- We compile against the **lowest** release of each line, so we cannot accidentally use an API a supported server lacks.
- The mapping lives in `Directory.Build.props` (`JellyfinLine`, `JellyfinTargetAbi`, `JellyfinAbiFlavor`) and the
  pinned package versions in `Directory.Packages.props`. `build/package.ps1` reads them through MSBuild.
- Jellyfin installs the highest catalog version whose `targetAbi` is not newer than the server, so `x.y.z.1` (12) wins
  on 12.x servers and only `x.y.z.0` qualifies on 10.11. After a 10.11 → 12 server upgrade, the 12 build shows up as
  an update.

When an API differs between the lines, isolate it in one adapter class with `#if NET10_0_OR_GREATER` and make sure
tests cover both builds (they run for both target frameworks automatically).

## Plugin building blocks

| Concern | How |
| --- | --- |
| Entry point | `Plugin : BasePlugin<PluginConfiguration>, IHasWebPages` |
| Settings | `PluginConfiguration : BasePluginConfiguration`, persisted as XML by Jellyfin |
| Services | `PluginServiceRegistrator : IPluginServiceRegistrator`; background work as `IHostedService` |
| Admin API | `HyperionGrabberController`, `[Authorize(Policy = Policies.RequiresElevation)]` from `MediaBrowser.Common.Api` |
| Config page | `Configuration/configPage.html` (fragment with `data-controller="__plugin/HyperionGrabberJs"`) + `configPage.js` (ES module), both embedded resources |

Jellyfin serializes API JSON in PascalCase; the config page sends and reads PascalCase properties.

## Configuration compatibility

`PluginConfiguration` is a public contract stored on users' servers.

- Add properties only with defaults that keep today's behaviour.
- Never rename or remove a property without a migration (read the old value, write the new one) and a test that an
  old XML file still loads (`PluginConfigurationTests.MissingElements_KeepDefaults` is the pattern).
- Multiple targets (M3) will replace the single host/port/priority with a list; the migration turns the existing
  values into the first target.

## Playback events (input for M1/M2)

`ISessionManager` raises `PlaybackStart`, `PlaybackProgress` and `PlaybackStopped`. `PlaybackProgressEventArgs`
carries the session (device, client, user), the item, `PlaybackPositionTicks`, `IsPaused` and `MediaSourceId`.

- There is no separate seek or pause event: both arrive as `PlaybackProgress` with a new position or `IsPaused`.
- How often and how precisely clients report differs (checked in their sources, October 2026):

    | Client | Position | Regular reports | Pause / seek |
    | --- | --- | --- | --- |
    | Jellyfin for Kodi (`player.py`, `entrypoint/service.py`) | whole seconds, truncated (`int(getTime())`) | about every 4 minutes | immediately |
    | Web client and its wrappers (`apiClient.reportPlaybackProgress`) | milliseconds, up to one `timeupdate` (about 250 ms) old | every 10 s | immediately |
    | Android TV (`PlaybackController`) | milliseconds | every 3 s | immediately |
    | Android (`PlayerViewModel`) | milliseconds | every 10 s | - |
    | Swiftfin (`MediaProgressObserver`) | - | every 5 s | immediately |

    Jellyfin for Kodi's start report carries the requested start position, not the player's clock
    (`PlaybackState.IsStart` marks start reports). The session therefore keeps the range the position can be in and
    narrows it with every report (`PositionTracker`, [ADR 0009](adr/0009-position-tracking.md)); a report that moves
    the estimate by more than 1 s is a seek.
- Kodi exposes its exact playback time and events over JSON-RPC (TCP 9090 or HTTP). An optional Kodi clock source is
  planned for precise sync on Kodi clients (M2).
- Event handlers run on Jellyfin's threads: post the change to the session and return immediately.

How the plugin uses them (`Playback/PlaybackMonitorService.cs`, `Playback/PlaybackEventMapper.cs`):

| Core `PlaybackEvent` | Jellyfin source |
| --- | --- |
| `SessionId`, `DeviceId`, `DeviceName`, `Client`, `UserId` | `args.Session` (falls back to `args.DeviceId`, `DeviceName`, `ClientName`) |
| `ItemId` | `args.Item.Id`; start and progress without an item are ignored |
| `MediaSourceId`, `Position`, `IsPaused` | `args.MediaSourceId`, `args.PlaybackPositionTicks` (100 ns ticks = `TimeSpan` ticks), `args.IsPaused` |

- The device and user lists on the configuration page come from `ISessionManager.Sessions`
  (`GET HyperionGrabber/Clients`). `IUserManager.Users` exists in 10.11 but not in 12, so users come from sessions
  too. Saved selections keep a display name so offline devices still show.
- Jellyfin creates the plugin instance itself and does not register it for dependency injection; services read the
  configuration through `Plugin.Instance` (only in `PluginPlaybackFilterSource`) and react to
  `Plugin.ConfigurationChanged`.

## Media and decoding

- Decode the **media source that is playing** (`MediaSourceId`), from its path, never through Jellyfin's HTTP streaming
  endpoints (that would create a phantom playback session).
- `JellyfinVideoInputResolver` does this once per playback start, on the streaming session's task:
  `ILibraryManager.GetItemById(itemId)`, then `IMediaSourceManager.GetStaticMediaSources(item, false)` (same signature
  in 10.11 and 12). It picks the source whose id matches the reported `MediaSourceId` (with or without dashes),
  otherwise the first (default) one, and takes size and codec from its first video stream.
- Like Jellyfin's transcoder, the path is passed as `file:<path>`, so FFmpeg never reads a path as another protocol.
- Not decoded, with a log line saying why: items that are not `Video` (Live TV channels, music), sources that are not
  local files (`Protocol != File` or `IsRemote`, which covers `.strm` and Live TV: re-decoding would open a second
  tuner or network stream), infinite streams, DVD/Blu-ray folders and disc images (`VideoType` other than
  `VideoFile`, which need Jellyfin's special input handling), and sources without a video stream of known size.

`JellyfinFfmpegSettingsProvider` reads, on every call, `IMediaEncoder.EncoderPath` and the encoding options
(`IServerConfigurationManager.GetEncodingOptions()`, the same in 10.11 and 12): `HardwareAccelerationType`,
`VaapiDevice` and `HardwareDecodingCodecs`. Like Jellyfin's transcoder, a video is hardware-decoded only when its
codec is enabled under **Dashboard → Playback → Transcoding → Enable hardware decoding for**; an unknown codec tries
the hardware. `FfmpegFrameSource` ([ADR 0007](adr/0007-ffmpeg-frame-source.md)) then builds the command line:

| Jellyfin setting | FFmpeg input options | Scaling |
| --- | --- | --- |
| None | — | `scale=W:H:flags=area` (CPU) |
| NVIDIA NVENC | `-hwaccel cuda -hwaccel_output_format cuda` | `scale_cuda=W:H:format=yuv420p,hwdownload` |
| Intel QuickSync | Linux: `-init_hw_device vaapi=va:<VaapiDevice> -init_hw_device qsv=qs@va`; then `-hwaccel qsv -hwaccel_output_format qsv` | `scale_qsv=w=W:h=H:format=nv12,hwdownload` |
| VA-API | `-vaapi_device <VaapiDevice> -hwaccel vaapi -hwaccel_output_format vaapi` | `scale_vaapi=w=W:h=H:format=nv12,hwdownload` |
| AMD AMF | Windows: `-hwaccel d3d11va`; Linux: as VA-API | Windows: CPU; Linux: `scale_vaapi` |
| Apple VideoToolbox | `-hwaccel videotoolbox` | CPU |
| Rockchip RKMPP | `-hwaccel rkmpp -hwaccel_output_format drm_prime` | `scale_rkrga=w=W:h=H:format=nv12:afbc=0,hwdownload` |
| V4L2 | — (CPU) | CPU |

Every variant adds `-ss <start>` before `-i`, `-map 0:v:0 -an -sn -dn`, an `fps=<rate>` filter first and
`format=rgb24`, and writes `-f rawvideo pipe:1`. W is 160 by default and H follows from the display aspect ratio
(rounded to an even number). If FFmpeg exits with an error before the first frame, the source logs a warning and
restarts on the CPU. HDR tone mapping is not applied yet *(planned, M3)*.

Verified so far: CPU on Windows (FFmpeg 8.0) and Linux (Ubuntu FFmpeg 6.1); NVENC and the D3D11VA path on an NVIDIA
RTX 4070 (Windows); the CPU fallback with an unavailable VA-API device. QSV, VA-API, RKMPP and VideoToolbox are built
after Jellyfin's own commands but not yet tested on real hardware; please report results.
