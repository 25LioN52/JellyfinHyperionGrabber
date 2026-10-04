# Building & running

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned loosely in `global.json`) and the **.NET 9 runtime**
  (the Jellyfin 10.11 build and its tests run on .NET 9).
- Git. PowerShell 7 for the packaging scripts (Windows PowerShell 5.1 also works).
- Optional: Docker (local Jellyfin test bed), Python 3 (docs preview).

Any editor works. Visual Studio 2022 17.14+ / 2026, Rider and VS Code with C# Dev Kit understand the `.slnx` solution
and the Microsoft.Testing.Platform test runner.

## Build and test

```bash
dotnet build -c Release
dotnet test --solution Jellyfin.Plugin.HyperionGrabber.slnx -c Release
dotnet format Jellyfin.Plugin.HyperionGrabber.slnx --verify-no-changes
```

- Every project targets `net9.0` and `net10.0`, so one build produces both Jellyfin builds and the tests run twice.
- Warnings are errors (`TreatWarningsAsErrors`, .NET analyzers in `AllEnabledByDefault` mode, StyleCop).
- Run a subset: `dotnet test --project tests/Jellyfin.Plugin.HyperionGrabber.Core.Tests --filter-class "*HyperionClientTests"`.
- Coverage: add `--coverage --coverage-output-format cobertura --results-directory TestResults`.

## Package

```bash
pwsh ./build/package.ps1 -Version 0.0.0
```

Produces in `artifacts/`:

- `hyperion-grabber_0.0.0.0_jellyfin-10.11.zip` and `hyperion-grabber_0.0.0.1_jellyfin-12.zip`
- `manifest-versions.json`: plugin repository entries for both zips (with MD5 checksums)
- `stage/net9.0/`, `stage/net10.0/`: unzipped plugin folders, handy for local testing

## Run in a local Jellyfin

The test bed runs Jellyfin 10.11 and 12 side by side with the staged builds mounted as plugins:

```bash
pwsh ./build/package.ps1 -Version 0.0.0
docker compose -f build/docker-compose.dev.yml up
```

- Jellyfin 10.11: <http://localhost:8096>, Jellyfin 12: <http://localhost:8097>. Finish the setup wizard once
  (the configuration is kept in Docker volumes).
- The plugin appears under **Dashboard → Plugins → Hyperion Grabber**.
- A Hyperion on your machine is reachable from the containers as `host.docker.internal`.
- After a code change: re-run `package.ps1`, then `docker compose -f build/docker-compose.dev.yml restart`.
- Logs: `docker compose -f build/docker-compose.dev.yml logs -f jellyfin-12`.

Without Docker, copy `artifacts/stage/<tfm>/` into `<jellyfin data>/plugins/Hyperion Grabber_0.0.0.<0|1>/` and restart
Jellyfin.

## Preview the docs

```bash
python -m venv .venv && . .venv/bin/activate      # Windows: .venv\Scripts\activate
pip install -r docs/requirements.txt
mkdocs serve                                       # http://127.0.0.1:8000
mkdocs build --strict                              # what CI runs; fails on broken links
```
