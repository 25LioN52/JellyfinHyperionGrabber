---
paths:
  - ".github/**"
  - "release-please-config.json"
  - ".release-please-manifest.json"
  - "build/*.ps1"
---

# CI/CD rules

- Least privilege: workflow-level `permissions: contents: read`; grant more per job only where needed.
- Never check out or run PR code in a `pull_request_target` workflow. Workflows that use secrets must skip fork PRs.
- Actions are referenced by major version tag (`@v4`); Dependabot keeps them current.
- `build/*.ps1` must run in both PowerShell 7 and Windows PowerShell 5.1: no `??`, `?:`, `-AsHashtable`, 3-argument
  `Join-Path`; write files with `[System.IO.File]::WriteAllText` and UTF-8 without BOM; iterate JSON arrays with
  `foreach` (5.1 returns them as one object).
- The repository manifest (`manifest.json`) is generated from release assets on every Pages deployment; never commit a
  hand-edited manifest.
- Releases are cut only by merging the release-please PR. Do not create tags or releases manually.
