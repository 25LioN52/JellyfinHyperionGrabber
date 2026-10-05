---
name: implement-issue
description: Take a GitHub issue of this repository from reading to an open pull request - plan, branch, test-first implementation for both Jellyfin lines, docs, verification and a Conventional Commits PR. Use when asked to implement, fix or work on an issue or roadmap item.
argument-hint: "[issue-number]"
arguments: [issue]
---

# Implement issue $issue

Work through these steps in order. Do not skip verification.

## 1. Understand

- Read the issue and its comments: `gh issue view $issue --comments`. Note acceptance criteria; if there are none,
  write them down in your plan.
- Read `AGENTS.md`, the roadmap entry (`docs/roadmap.md`) and the design docs for the area you will touch
  (`docs/development/architecture.md`, `hyperion-protocol.md`, `jellyfin-integration.md`).
- If the issue is ambiguous in a way that changes the design, ask in the issue (or the user) before coding. Minor
  ambiguity: pick the conservative option and say so in the PR.

## 2. Plan

Write a short plan (in the PR description later): which projects change, which new types, which tests, which docs.
Check the plan against the non-negotiable rules in `AGENTS.md`, especially:

- Core has no Jellyfin references; Jellyfin specifics go behind a Core interface.
- Both `net9.0` (Jellyfin 10.11) and `net10.0` (Jellyfin 12) must build.
- No new shipped dependency without an ADR.
- Configuration stays backwards compatible.

If the change is a significant design decision, add an ADR in `docs/development/adr/` as part of the PR.

## 3. Branch

```bash
git switch -c feat/$issue-short-slug   # or fix/..., docs/..., refactor/...
```

## 4. Test first, then implement

- Bug: write the failing test first and run it to see it fail.
- Feature: write tests for the acceptance criteria next to the code under `tests/`, using `FakeHyperionServer`,
  `RecordingSink` and `FakeTimeProvider` instead of real time or real servers.
- Implement in small steps; build often. Follow `.claude/rules/*` for the files you touch.

## 5. Verify

All of these must pass locally:

```bash
dotnet build -c Release
dotnet test --solution Jellyfin.Plugin.HyperionGrabber.slnx -c Release
dotnet format Jellyfin.Plugin.HyperionGrabber.slnx --verify-no-changes
```

If you changed docs: `mkdocs build --strict` (needs `pip install -r docs/requirements.txt`).
If you changed packaging: `pwsh ./build/package.ps1 -Version 0.0.0` and inspect `artifacts/`.

Then run `/review-pr` on your own diff mentally (or literally) and fix what it would flag.

## 6. Docs

Update user docs for any user-visible change (settings, behaviour, troubleshooting entries) and developer docs for
design changes. Tick the item in `docs/roadmap.md` if the issue completes one.

## 7. Commit and open the PR

- Commit messages and the PR title use Conventional Commits: `feat: ...`, `fix: ...`, `docs: ...`, `test: ...`.
- Push the branch and open a PR that fills in `.github/pull_request_template.md`, including `Closes #$issue`, the plan,
  and exactly how you verified it. Mention anything you could not verify (for example: not tested on real Hyperion).
- Do not merge. The maintainer reviews it locally with `/review-pr <n>`; address every finding with a fix or a
  reasoned reply.
