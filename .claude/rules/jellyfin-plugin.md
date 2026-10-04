---
paths:
  - "src/Jellyfin.Plugin.HyperionGrabber/**"
  - "tests/Jellyfin.Plugin.HyperionGrabber.Tests/**"
---

# Jellyfin plugin rules

- Keep this project thin: translate Jellyfin concepts (sessions, items, encoding options) into Core types and call
  Core. Business logic belongs in Core, where it is testable without Jellyfin.
- Code must compile against **both** pinned Jellyfin packages (10.11.0 for `net9.0`, 12.0.0 for `net10.0`).
  Building the solution builds both; if an API differs, isolate it in one adapter class with `#if NET10_0_OR_GREATER`.
- Services are registered in `PluginServiceRegistrator`. Long-running work runs in an `IHostedService` registered
  there and must stop promptly on shutdown.
- API controllers: `[ApiController]`, `[Route("HyperionGrabber")]`, `[Authorize(Policy = Policies.RequiresElevation)]`
  for anything that changes state or opens connections. Validate input with Core's `GetValidationErrors()` and
  return `400` with a `HyperionTestResponse`-style message. Remember Jellyfin's JSON is PascalCase.
- `PluginConfiguration` is persisted XML: add properties with defaults only; changing or removing one needs a migration
  plus a test that old XML still loads (`PluginConfigurationTests`).
- The plugin GUID, page names and element ids are duplicated in C#, HTML, JS and `build/plugin.json`; `PluginTests`
  checks them, so update all places together.
