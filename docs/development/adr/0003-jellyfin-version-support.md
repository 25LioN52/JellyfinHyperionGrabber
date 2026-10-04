# 0003: Jellyfin 10.11 and 12 from one codebase

**Status:** Accepted (2026-10-04)

## Context

Jellyfin 12.0 (released 2026-09-08, followed by 12.1 a week later) moved the server to .NET 10 and changed plugin
interfaces; plugins compiled for 10.11 (.NET 9) do not load on 12. Many servers will stay on 10.11 for a while,
partly because their other plugins are not updated yet. The maintainer's own server may be on either line.
Jellyfin's plugin catalog filters versions by `targetAbi` and installs the highest compatible version.

## Decision

- Multi-target every project: `net9.0` builds against `Jellyfin.Controller`/`Jellyfin.Model` **10.11.0**
  (`targetAbi` 10.11.0.0), `net10.0` against **12.0.0** (`targetAbi` 12.0.0.0). Always compile against the lowest
  release of a line.
- Each release ships one zip per line. The 4th version component identifies the line (`x.y.z.0` = 10.11,
  `x.y.z.1` = 12), so a single manifest works: a 12 server sees both but installs `.1` (higher), a 10.11 server only
  qualifies for `.0`. A server upgraded from 10.11 to 12 is offered the `.1` build as an update.
- Line differences, if any, are isolated in adapter classes with `#if NET10_0_OR_GREATER`.
- Older lines (10.10 and earlier) are not supported.

## Consequences

- One repository URL for users, no per-version instructions.
- CI builds and tests both lines on every change; the test suite runs twice.
- Jellyfin package versions are pinned per line and updated deliberately (Dependabot ignores them).
- When a new Jellyfin line arrives, add a target framework / flavor `.2`; drop a line by removing its target framework.
