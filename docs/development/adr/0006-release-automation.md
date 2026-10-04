# 0006: release-please releases and a generated plugin manifest

**Status:** Accepted (2026-10-04)

## Context

The maintainer does not want to do release chores, and agents should not create releases. Jellyfin installs plugins
from a repository manifest (`manifest.json`) listing every version with its download URL, MD5 checksum and
`targetAbi`. A hand-maintained or committed manifest drifts from the actual release assets and needs write access to
`main` from CI.

## Decision

- Conventional Commits PR titles (enforced) + squash merge; release-please maintains a release PR with the next
  version and changelog. Merging it is the only way to release.
- The release workflow builds both zips and attaches them with `manifest-versions.json` (the entries for that
  release) to the GitHub release.
- `manifest.json` is regenerated from all releases' `manifest-versions.json` on every GitHub Pages deployment, and
  published next to the docs. Nothing generated is committed.

## Consequences

- Releasing is one click (merge the release PR); the changelog writes itself from PR titles.
- The manifest always matches the published assets; deleting a release and redeploying removes it from the catalog.
- PR titles matter: a wrong type produces a wrong version bump or changelog entry.
- The manifest URL depends on GitHub Pages being configured (see [Repository setup](../repository-setup.md)).
