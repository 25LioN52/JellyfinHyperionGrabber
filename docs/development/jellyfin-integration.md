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
- How often clients report differs. **Jellyfin for Kodi** (`jellyfin-kodi/jellyfin_kodi/player.py`) reports
  immediately on pause, resume and seek, and otherwise only after the position advanced by 30 seconds or more.
  The sync engine therefore extrapolates (`position = last reported + elapsed × speed`) and treats a jump larger than
  a threshold as a seek.
- Kodi exposes its exact playback time and events over JSON-RPC (TCP 9090 or HTTP). An optional Kodi clock source is
  planned for precise sync on Kodi clients (M2).
- Event handlers run on Jellyfin's threads: post the change to the session and return immediately.

## Media and decoding (input for M1)

- Decode the **media source that is playing** (`MediaSourceId`), from its path, never through Jellyfin's HTTP streaming
  endpoints (that would create a phantom playback session).
- Use Jellyfin's FFmpeg (`IMediaEncoder.EncoderPath`) and its hardware acceleration settings (encoding options:
  QSV, VA-API, NVENC, AMF, VideoToolbox, RKMPP), with CPU fallback.
- Scale on the GPU before downloading frames; output `rgb24` at a constant frame rate; keep the aspect ratio.
- Live TV is excluded: re-decoding would open a second tuner stream.
