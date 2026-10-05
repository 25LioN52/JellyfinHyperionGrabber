# Contributing

Thanks for helping! This project is maintained mostly with AI coding agents, with human ownership and review, so
the rules below are explicit and apply to humans and agents equally.

## Ways to help

- **Test on your setup** and report what works: client app, Jellyfin version, Hyperion.ng/HyperHDR version, LED
  devices. Compatibility reports are as valuable as code.
- **Report bugs** with the [bug form](https://github.com/25LioN52/JellyfinHyperionGrabber/issues/new/choose).
- **Improve the docs**: every page has an edit button.
- **Pick an issue** labelled `good first issue` or `help wanted`, or comment on one to claim it.

## Development setup

You need the .NET 10 SDK and the .NET 9 runtime (both Jellyfin lines are built and tested), plus Git.
PowerShell 7 runs the packaging scripts, and Python 3 is only needed to preview the docs.

```bash
dotnet build -c Release
dotnet test --solution Jellyfin.Plugin.HyperionGrabber.slnx -c Release
dotnet format Jellyfin.Plugin.HyperionGrabber.slnx --verify-no-changes
```

The [developer guide](docs/development/index.md) covers architecture, the Hyperion protocol, testing and how to run
the plugin in a local Jellyfin.

## Workflow

1. **Open or pick an issue first** for anything bigger than a typo, so the design can be agreed before code is written.
2. **Branch** from `main`: `feat/<issue>-short-name`, `fix/<issue>-short-name`, `docs/...`.
3. **Keep PRs small** and focused on one concern. Refactors go in their own PR.
4. **Write tests.** New behaviour needs tests; a bug fix needs a test that fails without the fix.
5. **Update docs** in the same PR when behaviour, settings or architecture change.
6. **Title the PR** with [Conventional Commits](https://www.conventionalcommits.org/): `feat: add seek handling`,
   `fix: reconnect after Hyperion restart`, `docs: …`. PRs are squash-merged, so the title becomes the changelog entry.
   Use `feat!:` for breaking changes.
7. **Review:** the maintainer reviews every PR (with Claude Code, locally) before merging. Address
   every finding or reply with why it does not apply.

## Ground rules

These are enforced in review. Reasons and details are in [AGENTS.md](AGENTS.md).

- `Jellyfin.Plugin.HyperionGrabber.Core` never references Jellyfin packages.
- Both Jellyfin lines (10.11 / `net9.0` and 12 / `net10.0`) must build and pass tests.
- Warnings are errors; do not suppress analyzers without a justification comment.
- Never block Jellyfin's threads: async I/O with cancellation, `ConfigureAwait(false)` in library code.
- Saved configuration must stay backwards compatible.
- No new runtime dependencies in the plugin without an ADR in `docs/development/adr/`.

## Code of conduct and license

Participation is governed by the [Code of Conduct](CODE_OF_CONDUCT.md). By contributing you agree that your
contributions are licensed under the [GPL-3.0](LICENSE).
