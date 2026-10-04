<!--
PR title = squash commit = changelog entry. Use Conventional Commits:
  feat: …  fix: …  perf: …  docs: …  refactor: …  test: …  ci: …  build: …  chore: …  deps: …
Add "!" for breaking changes (feat!: …).
-->

## What and why

Closes #

## How it was verified

- [ ] `dotnet build -c Release` (warnings are errors) and `dotnet test --solution Jellyfin.Plugin.HyperionGrabber.slnx -c Release` pass
- [ ] `dotnet format --verify-no-changes` passes
- [ ] New behaviour has tests (bug fixes include a test that fails without the fix)
- [ ] Manually tested with a real Hyperion.ng / HyperHDR (describe below), or not applicable

## Checklist

- [ ] `Jellyfin.Plugin.HyperionGrabber.Core` still has no Jellyfin references
- [ ] Docs updated (user guide for behaviour/settings, `docs/development` for design), or not needed
- [ ] Configuration changes are backwards compatible (old XML still loads), or a migration is included
- [ ] Works for both Jellyfin lines (10.11 / `net9.0` and 12 / `net10.0`)
