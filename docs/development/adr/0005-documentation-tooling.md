# 0005: Docs as Markdown in the repository, built with Material for MkDocs

**Status:** Accepted (2026-10-04)

## Context

The project needs user documentation (a "wiki") and developer documentation that stay in sync with the code and can be
changed by agents in the same PR as the code. A GitHub Wiki is a separate repository: changes are not reviewed in
PRs, are not versioned with releases and are invisible to agents working in the main repository.

Material for MkDocs is the most widely used theme for this kind of site, but entered maintenance mode on
2025-11-05 (fixes until 2026-11), with its authors' successor Zensical still in alpha (0.0.x) in October 2026.
Zensical reads `mkdocs.yml`.

## Decision

- Docs live in `docs/` as plain Markdown, readable on GitHub as is, and are published to GitHub Pages together with
  the plugin repository manifest.
- Build with Material for MkDocs (pinned in `docs/requirements.txt`) and `mkdocs build --strict` in CI. MkDocs itself
  is pinned below 2.0, which drops the plugin and theme system Material relies on.
- Migrate to Zensical once it is stable; the Markdown and `mkdocs.yml` carry over.

## Consequences

- Docs changes are reviewed with code changes, and broken links fail CI.
- We depend on a toolchain in maintenance mode for a while; the migration path is cheap because content is plain
  Markdown.
