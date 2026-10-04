# Repository setup

One-time GitHub settings for the maintainer. Everything else is in the repository.

## Required

- [ ] **Make the repository public** (Settings → General → Danger zone). GitHub Pages, CodeQL and free Actions
      minutes for the matrix build depend on it.
- [ ] **Pages:** Settings → Pages → Build and deployment → Source: **GitHub Actions**. Then run the
      *Docs & plugin repository* workflow once (Actions → workflow → Run workflow).
- [ ] **Actions permissions:** Settings → Actions → General → Workflow permissions: *Read repository contents*
      (workflows request more per job), and tick **Allow GitHub Actions to create and approve pull requests**
      (release-please opens the release PR).
- [ ] **Merge settings:** Settings → General → Pull Requests: allow **squash merging** only, default commit message
      *Pull request title*; enable *Automatically delete head branches*.
- [ ] **Branch protection / ruleset for `main`:** require a pull request, require status checks
      `Build & test (ubuntu-latest)`, `Build & test (windows-latest)`, `Package plugin`, `conventional-commit`;
      block force pushes.

## Claude Code (reviews and agents)

- [ ] Install the **Claude GitHub App** on the repository. The easiest way is to run `/install-github-app` in a
      Claude Code terminal session inside this repository; it installs the app and creates the secret.
- [ ] Add **one** repository secret: `CLAUDE_CODE_OAUTH_TOKEN` (created with `claude setup-token`, uses your Claude
      subscription) or `ANTHROPIC_API_KEY` (API billing).
- [ ] Create the label **`claude`**: labelling an issue with it asks Claude to implement it.

## Recommended

- [ ] **Discussions:** Settings → General → Features → Discussions (Q&A, Ideas, Show and tell).
- [ ] **Private vulnerability reporting:** Settings → Security → Advanced Security → enable.
- [ ] **Dependabot alerts and security updates:** same page.
- [ ] **About box:** description *"Ambient lighting for Jellyfin: stream the playing video to Hyperion.ng / HyperHDR"*,
      website = the Pages URL, topics: `jellyfin`, `jellyfin-plugin`, `hyperion`, `hyperion-ng`, `hyperhdr`,
      `ambilight`, `wled`, `philips-hue`, `ambient-lighting`, `home-theater`.
- [ ] **Labels:** `bug`, `enhancement`, `triage`, `good first issue`, `help wanted`, `documentation`, `claude`.
- [ ] **Social preview** image (Settings → General) once there is a demo photo or GIF.

## Spreading the word (after the first streaming release)

- A short demo video/GIF (TV and LEDs side by side) at the top of the README.
- Add the plugin to [awesome-jellyfin](https://github.com/awesome-jellyfin/awesome-jellyfin).
- Announce on the Jellyfin forum (there is an existing *WLED ambilight interest* thread), r/jellyfin, the Hyperion
  forum and HyperHDR discussions.
