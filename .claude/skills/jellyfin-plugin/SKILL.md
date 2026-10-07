---
name: jellyfin-plugin
description: Reference for Jellyfin plugin development in this repository - supported lines (10.11 net9.0, 12 net10.0), ABI and versioning, how to check an API exists in both lines, DI and hosted services, admin controllers, configuration page pattern, playback session events and client reporting cadence, and running the plugin in a local Jellyfin. Use when changing src/Jellyfin.Plugin.HyperionGrabber or designing Jellyfin integration.
user-invocable: false
---

# Jellyfin plugin reference

Authoritative write-up: `docs/development/jellyfin-integration.md`.

## Lines and ABI

| Jellyfin | TFM | Packages compiled against | targetAbi | Version suffix |
| --- | --- | --- | --- | --- |
| 10.11.x | net9.0 | 10.11.0 | 10.11.0.0 | `.0` |
| 12.x | net10.0 | 12.0.0 | 12.0.0.0 | `.1` |

Jellyfin installs the highest version whose `targetAbi` is <= the server version, so a 12.x server picks `x.y.z.1`
and a 10.11 server `x.y.z.0`. `JellyfinTargetAbi` / `JellyfinAbiFlavor` live in `Directory.Build.props`.

## Checking an API in both lines

Build the solution (it builds both TFMs). To inspect an API, look at the XML docs/assemblies in
`~/.nuget/packages/jellyfin.controller/<version>/lib/<tfm>/`. If signatures differ, wrap them in one adapter with
`#if NET10_0_OR_GREATER`.

## Building blocks

- `Plugin : BasePlugin<PluginConfiguration>, IHasWebPages`; pages are embedded resources.
- `IPluginServiceRegistrator.RegisterServices(IServiceCollection, IServerApplicationHost)` for DI; use
  `services.AddHostedService<T>()` for background work.
- Controllers are discovered automatically; protect them with `[Authorize(Policy = Policies.RequiresElevation)]`
  from `MediaBrowser.Common.Api`. JSON is PascalCase.
- Useful services (verify in both lines before use): `ISessionManager` (events `PlaybackStart`, `PlaybackProgress`,
  `PlaybackStopped`; `PlaybackProgressEventArgs` has `Session`, `Item`, `PlaybackPositionTicks`, `IsPaused`,
  `MediaSourceId`, `DeviceId`), `ILibraryManager` (`GetItemById`), `IMediaEncoder` (`EncoderPath`),
  `IServerConfigurationManager` (encoding options / hardware acceleration), `IMediaSourceManager`.

## Playback reporting cadence (drives sync design)

- Pause, resume and seek arrive as `PlaybackProgress` with a new position / `IsPaused`; there is no separate seek event.
- Jellyfin for Kodi (`jellyfin_kodi/player.py`, `entrypoint/service.py`) reports `int(getTime())` (whole seconds,
  truncated) immediately on pause/resume/seek and otherwise about every 4 minutes. Web-based clients report milliseconds
  every 10 s, Android TV every 3 s. Never assume frequent or precise reports; `PositionTracker` (ADR 0009) handles both.

## Running locally

`pwsh ./build/package.ps1 -Version 0.0.0`, then `docker compose -f build/docker-compose.dev.yml up` runs Jellyfin
10.11 (http://localhost:8096) and 12 (http://localhost:8097) with the matching build mounted from `artifacts/stage/`.
Details in `docs/development/building.md`.
