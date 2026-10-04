@AGENTS.md

## Claude Code specifics

- **Skills** (`.claude/skills/`): `/implement-issue <n>` to take an issue to a PR, `/review-pr <n>` to review (also run
  by CI on every PR), `/release` for release maintenance. `hyperion-protocol` and `jellyfin-plugin` are reference
  skills that load when you work on those areas.
- **Rules** (`.claude/rules/`) are scoped by path and load automatically when you open matching files.
- You are this repository's **required reviewer**. When asked to review anything, use `/review-pr`; be specific,
  cite `file:line`, and separate blocking issues from suggestions.
- The maintainer does not want to babysit the project: prefer finishing work end to end (tests, docs, PR description)
  over asking, but stop and ask before anything irreversible or outward-facing (publishing releases, changing
  repository settings, force pushes).
- On Windows the shell may be PowerShell; `build/*.ps1` scripts work in Windows PowerShell 5.1 and PowerShell 7.
