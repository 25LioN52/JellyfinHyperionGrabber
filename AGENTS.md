# AGENTS.md

Instructions for AI coding agents (and humans) working in this repository. Keep this file short and true; details
live in `docs/development/`, procedures in `.claude/skills/`, file-specific rules in `.claude/rules/`.

## What this is

A Jellyfin server plugin that decodes the video a client is playing (a second, tiny, hardware-accelerated decode on
the server, kept in sync with the client) and streams frames to **Hyperion.ng or HyperHDR** over their FlatBuffers
TCP protocol (port 19400). Hyperion owns LED layout, calibration and devices (WLED, Philips Hue, ...).

Status: **M0 done** (Hyperion client, test pattern, config page, CI, docs). Next: M1 playback → LEDs.
See `docs/roadmap.md`.

## Commands

```bash
dotnet build -c Release                                                     # warnings are errors
dotnet test --solution Jellyfin.Plugin.HyperionGrabber.slnx -c Release      # runs net9.0 and net10.0
dotnet format Jellyfin.Plugin.HyperionGrabber.slnx --verify-no-changes      # CI enforces formatting
pwsh ./build/package.ps1 -Version 0.0.0                                     # plugin zips in artifacts/
```

Tests use xUnit v3 on Microsoft.Testing.Platform (`global.json`), so filters are `--filter-class "*Name"` /
`--filter-method "*Name"`, not `--filter`.

## Repository map

| Path | Purpose |
| --- | --- |
| `src/Jellyfin.Plugin.HyperionGrabber.Core` | Host-independent engine: Hyperion FlatBuffers client, test pattern, later frame pipeline and sync. **No Jellyfin references.** |
| `src/Jellyfin.Plugin.HyperionGrabber` | Thin Jellyfin adapter: `Plugin`, DI registration, admin API, config page (`Configuration/configPage.html/.js`). |
| `tests/*.Core.Tests`, `tests/*.Tests` | Unit and integration tests for each project. |
| `tests/*.TestSupport` | `FakeHyperionServer` (decodes with the official FlatBuffers runtime + verifier), `RecordingSink`, `RecordingGrabSessionFactory`, `TestHelpers`. |
| `build/` | `plugin.json` (catalog metadata), `package.ps1` (zips), `New-PluginManifest.ps1` (repository manifest). |
| `docs/` | MkDocs site: user guide, developer guide, ADRs. Published to GitHub Pages with `manifest.json`. |

## Non-negotiable rules

1. **Core stays host-independent.** No `Jellyfin.*` / `MediaBrowser.*` references in `*.Core`. Jellyfin-specific code
   lives in the plugin project behind interfaces that Core defines.
2. **Both Jellyfin lines must work.** Every project targets `net9.0` (Jellyfin 10.11) and `net10.0` (Jellyfin 12).
   Jellyfin packages are pinned per line in `Directory.Packages.props` to the lowest supported release. Use
   `#if NET10_0_OR_GREATER` only when an API really differs, and test both paths.
3. **Hyperion protocol invariants** (verified against Hyperion.ng and HyperHDR sources; see
   `docs/development/hyperion-protocol.md`): 4-byte **big-endian** size prefix; `Register` first and wait for its
   reply; priorities **100-199** only; never send `Clear(-1)` (clears every source); always drain replies; RGB24
   `RawImage`. Any change to protocol code needs official-verifier tests and, if the layout changes, updated golden bytes.
4. **Never hurt the Jellyfin server.** Async I/O with cancellation everywhere, no sync-over-async, no unbounded
   buffers or queues, `ConfigureAwait(false)` in `src/`. Per-frame code must not allocate frame buffers or other
   per-frame objects (pool them); small incidental runtime allocations (pipe reads, channel waits) are accepted when
   measured and noted in the PR.
5. **No new shipped dependencies** without an ADR. The plugin zip contains only our two assemblies (see
   `build/plugin.json`), which avoids version conflicts with other plugins inside Jellyfin.
6. **Saved configuration is a public contract.** Only add properties with safe defaults; renames and removals need a
   migration and a test that old XML still loads.
7. **Logging** uses `[LoggerMessage]` source generation. Info for lifecycle events, Debug for detail, never per frame
   above Trace. Never log secrets.
8. **Warnings are errors.** Fix analyzer findings; a suppression needs a `Justification`.
9. **Tests come with the change.** Bug fixes start with a failing test. No `Thread.Sleep`/fixed delays for
   synchronization: use `TestHelpers.WaitUntilAsync`, `FakeTimeProvider` or the fake server's awaitable requests.
10. **Docs come with the change.** User-visible behaviour → `docs/user-guide/`; design → `docs/development/`;
    significant decisions → a new ADR. Planned features are labelled *(planned)*.

## Workflow

- One issue → one branch (`feat/<issue>-slug`, `fix/...`, `docs/...`) → one small PR.
- PR title in Conventional Commits (`feat: ...`, `fix: ...`); PRs are squash-merged and release-please turns titles
  into `CHANGELOG.md` and versions. Never edit `CHANGELOG.md`, `version.txt` or `.release-please-manifest.json` by hand.
- The maintainer reviews every PR locally with Claude Code (`/review-pr <n>`, `.claude/skills/review-pr/SKILL.md`)
  and merges. There is no automatic review in CI.

## Definition of done

Build and tests pass for both target frameworks, formatting is clean, new behaviour is tested, docs are updated,
the PR description says how it was verified (including manual tests against a real Hyperion when relevant).

## Facts that are easy to get wrong

- Jellyfin plugin pages: `IHasWebPages` + an HTML fragment with `data-controller="__plugin/<ScriptPageName>"` and an
  ES module script. Admin endpoints: `[Authorize(Policy = Policies.RequiresElevation)]` (`MediaBrowser.Common.Api`).
- Jellyfin serializes API JSON in **PascalCase**.
- The plugin version's 4th component is the Jellyfin line (`x.y.z.0` = 10.11, `x.y.z.1` = 12); Jellyfin installs the
  highest version whose `targetAbi` it supports (`docs/development/adr/0003-jellyfin-version-support.md`).
- Jellyfin for Kodi reports playback position only on pause/resume/seek and roughly every 30 s of playback; the sync
  engine must extrapolate between reports (`docs/development/jellyfin-integration.md`).
