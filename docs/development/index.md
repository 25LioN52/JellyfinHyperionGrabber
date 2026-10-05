# Developer guide

Everything you need to change the plugin with confidence. Start with [Building & running](building.md), then read
[Architecture](architecture.md).

| Page | Read it when |
| --- | --- |
| [Building & running](building.md) | Setting up, running tests, trying the plugin in a local Jellyfin |
| [Architecture](architecture.md) | Before any non-trivial change |
| [Hyperion protocol](hyperion-protocol.md) | Touching anything under `Hyperion/` |
| [Jellyfin integration](jellyfin-integration.md) | Touching the plugin project, playback events, configuration |
| [Testing](testing.md) | Writing tests (always) |
| [Releasing](releasing.md) | Understanding versions, the changelog and the plugin repository |
| [Repository setup](repository-setup.md) | One-time GitHub settings for maintainers |
| [Working with AI agents](ai-agents.md) | Handing work to Claude Code or another agent |
| [Decisions (ADRs)](adr/index.md) | Wondering *why* something is the way it is |

## Repository layout

```text
src/
  Jellyfin.Plugin.HyperionGrabber.Core/   engine without Jellyfin references (Hyperion client, test pattern, ...)
  Jellyfin.Plugin.HyperionGrabber/        Jellyfin adapter (plugin, DI, admin API, config page)
tests/
  Jellyfin.Plugin.HyperionGrabber.Core.Tests/
  Jellyfin.Plugin.HyperionGrabber.Tests/
  Jellyfin.Plugin.HyperionGrabber.TestSupport/   FakeHyperionServer, RecordingSink, RecordingGrabSessionFactory, TestHelpers, official FlatBuffers codec
build/          plugin.json, package.ps1, New-PluginManifest.ps1, docker-compose.dev.yml
docs/           this site
.claude/        agent rules and skills;  AGENTS.md / CLAUDE.md at the root
```

## Principles

1. **Hyperion owns the LEDs.** The plugin produces a small, correct, well-timed picture and nothing else.
2. **Never hurt the server.** Jellyfin's job is serving media; our work is bounded, cancellable and cheap.
3. **Testable without Jellyfin.** Logic lives in Core, behind small interfaces, tested against fakes.
4. **Both Jellyfin lines, always.** Every change builds and is tested for 10.11 and 12.
5. **Easy for users.** One repository URL, sensible defaults, error messages that say how to fix the problem.
