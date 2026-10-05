---
name: review-pr
description: Review a pull request (or the local diff) of this repository as its required reviewer - correctness, Hyperion protocol invariants, both Jellyfin lines, server safety, tests, docs and config compatibility - and post a verdict. Run on the maintainer's PC with /review-pr <n> (there is no automatic review in CI); use whenever asked to review.
argument-hint: "[pr-number]"
arguments: [pr]
---

# Review PR $pr

You are the required reviewer. The maintainer runs this skill locally and relies on it to merge without reading
every line, so be thorough, specific and honest. Never approve something you have not checked.

Work in a clean checkout of the PR branch (`gh pr checkout $pr`, or a separate worktree if the current one has
changes) so you can build and test exactly what will be merged, after merging current `main` into it.

## 1. Gather context

- `gh pr view $pr` (description, linked issue) and `gh pr diff $pr`. Without a PR number, review `git diff main...HEAD`.
- Read the linked issue's acceptance criteria. Read `AGENTS.md` and the `.claude/rules/*` files matching the changed
  paths. Open the full files around changed hunks, not just the diff.
- If possible run `dotnet build -c Release` and `dotnet test --solution Jellyfin.Plugin.HyperionGrabber.slnx -c Release`.
  Report if you could not.

## 2. Check, in this order

**Correctness**
- Does the change do what the issue asks? Edge cases: zero/negative sizes, empty strings, cancellation, disposal,
  reconnects, server restarts, playback stop during startup, seek during pause.
- Concurrency: shared state guarded; no races between send, receive loop and dispose; no fire-and-forget tasks without
  error handling; `CancellationToken` passed through.

**Project invariants** (each violation is blocking)
- `*.Core` has no Jellyfin/MediaBrowser references.
- Builds for both `net9.0` and `net10.0`; no API used that exists in only one Jellyfin line without `#if` and tests.
- Hyperion protocol: big-endian size prefix, Register before data, priorities 100-199, no `Clear(-1)`, replies drained,
  idle timeout handled. Encoder changes are covered by official-verifier tests.
- No new shipped dependency without an ADR; `build/plugin.json` artifacts unchanged unless intended.
- Saved configuration backwards compatible (old XML still loads) or a migration with tests.
- Admin-only endpoints keep `RequiresElevation`; no user input reaches file paths or process arguments unvalidated.

**Server safety and performance**
- No sync-over-async, no blocking calls on request threads, `ConfigureAwait(false)` in `src/`.
- No unbounded queues/buffers; per-frame code does not allocate per frame; external processes are always killed and
  awaited on stop.
- Logging: `[LoggerMessage]`, sensible levels, no per-frame Info logs, no secrets.

**Tests**
- New behaviour tested; bug fix has a regression test; no fixed sleeps; deterministic; network only via
  `FakeHyperionServer` on loopback.

**Docs and housekeeping**
- User-visible change reflected in `docs/user-guide/`; design change in `docs/development/` (ADR if significant);
  roadmap updated when an item completes; `mkdocs.yml` nav updated for new pages.
- PR title is a valid Conventional Commit and matches the change (`feat` vs `fix` vs breaking `!`).
- No hand edits to `CHANGELOG.md`, `version.txt`, `.release-please-manifest.json`.

## 3. Report

Post through the maintainer's signed-in `gh` CLI:

- One inline comment per concrete issue, anchored to the line, starting with a severity tag:
  `[blocking]`, `[should-fix]` or `[nit]`. Explain the failure scenario and suggest a fix. Use
  `gh api repos/{owner}/{repo}/pulls/$pr/comments -f body=... -f commit_id=<head sha> -f path=<file> -F line=<n> -f side=RIGHT`.
- One summary posted as a review comment (`gh pr review $pr --comment --body-file <file>`; GitHub does not let the
  maintainer approve PRs opened from their own account, so the verdict line is what counts) with: what the PR does
  (two sentences), what you verified (build/tests run or not), a list of blocking issues, a list of other findings,
  and finally exactly one of these lines:

```
Verdict: APPROVE
Verdict: REQUEST CHANGES
Verdict: COMMENT
```

Use **REQUEST CHANGES** if there is any `[blocking]` finding, **APPROVE** only if build and tests pass (or CI is green)
and nothing blocking remains, **COMMENT** otherwise. Do not pad the review with praise or restate the diff.
