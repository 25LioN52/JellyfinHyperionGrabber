---
paths:
  - "src/Jellyfin.Plugin.HyperionGrabber.Core/**"
---

# Core library rules

- No `Jellyfin.*` or `MediaBrowser.*` usings or package references, ever. If Core needs something from Jellyfin
  (playback events, media paths, ffmpeg path, hardware acceleration settings), define a small interface or record in
  Core and implement it in the plugin project.
- Only dependency: the ASP.NET Core shared framework (for `Microsoft.Extensions.*` abstractions). Adding a package
  needs an ADR (`docs/development/adr/`).
- Public types need XML docs (CS1591 is an error). Prefer `internal` for implementation details; tests see internals
  through `InternalsVisibleTo`.
- Inject time as `TimeProvider` and logging as `ILogger`; no static mutable state, no `DateTime.Now`.
- All I/O is async, takes a `CancellationToken`, and uses `ConfigureAwait(false)`.
- Hot paths (anything called per frame) must not allocate per frame: reuse buffers, avoid LINQ and closures
  (`static` lambdas with a state argument are fine), avoid string formatting unless the log level is enabled.
- Exceptions: throw `HyperionConnectionException` (network) or `HyperionProtocolException` (server rejected / malformed
  data) from the Hyperion layer; argument problems throw `ArgumentException`. Never catch `Exception` broadly.
- Log with `[LoggerMessage]` in a nested `private static partial class Log`, with unique event ids per class.
