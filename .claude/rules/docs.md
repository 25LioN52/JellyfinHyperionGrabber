---
paths:
  - "docs/**"
  - "*.md"
  - "mkdocs.yml"
---

# Documentation rules

- Audience first: `docs/user-guide/` is for people running Jellyfin (no code), `docs/development/` is for contributors.
- Be concrete: exact menu paths (Dashboard → Plugins → ...), exact ports, copy-pasteable commands.
- Never document a feature as available before it ships; mark it *(planned)* and link the roadmap.
- New pages must be added to `nav` in `mkdocs.yml`. `mkdocs build --strict` must pass (it fails on broken links).
- Links between docs pages are relative (`../user-guide/configuration.md`); links from root Markdown files to the
  site use the published URL.
- Significant technical decisions get an ADR in `docs/development/adr/` (context, decision, consequences) and a line
  in `docs/development/adr/index.md`.
- Keep `AGENTS.md` short: it is loaded into every agent session. Put details in `docs/development/` and link them.
