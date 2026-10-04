---
name: release
description: Explain, check or troubleshoot the release pipeline (release-please PR, plugin zips, manifest.json on GitHub Pages) and prepare a release. Use only when the user explicitly asks about releasing or a release failed.
disable-model-invocation: true
---

# Release maintenance

Read `docs/development/releasing.md` first. Releases are never created by hand.

## Normal release

1. Make sure `main` is green (CI, CodeQL) and the roadmap/docs reflect what ships.
2. Find the open release PR titled `chore(main): release x.y.z` (created by release-please). Check its
   `CHANGELOG.md` diff: entries come from squash-merged PR titles; if one is wrong, fix the title of the *next* PR
   or add a `Release-As:` footer, never edit released sections.
3. The maintainer merges the release PR. `.github/workflows/release.yml` then tags `vX.Y.Z`, creates the GitHub
   release, runs tests, builds `hyperion-grabber_X.Y.Z.0_jellyfin-10.11.zip` and `..._X.Y.Z.1_jellyfin-12.zip`,
   uploads them with `manifest-versions.json`, and redeploys Pages so `manifest.json` lists the new versions.

## Verify a release

- Release assets: two zips plus `manifest-versions.json`.
- `https://25lion52.github.io/JellyfinHyperionGrabber/manifest.json` contains both versions with the right `targetAbi`
  and checksums matching the zips (`Get-FileHash -Algorithm MD5`).
- Installing from the repository URL works on Jellyfin 10.11 and 12 (see `docs/development/building.md` for running
  both in Docker).

## Troubleshooting

- *No release PR appears*: commits on `main` since the last release are all hidden types (`chore`, `ci`, `test`...).
- *Release created but no zips*: re-run the `publish` job of the Release workflow; `--clobber` makes it idempotent.
- *Manifest missing a version*: run the "Docs & plugin repository" workflow manually (workflow_dispatch).
- *Pages deploy fails*: Settings → Pages → Source must be "GitHub Actions".
