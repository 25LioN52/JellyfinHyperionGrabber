# Architecture decision records

Short records of decisions that shape the project: the context, the decision and its consequences. Add a new record
(next number, same structure) when a change makes a decision that future contributors would otherwise question or
accidentally undo. Superseded records stay, marked *Superseded by ...*.

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-server-side-decoding.md) | Decode the video on the Jellyfin server instead of capturing the screen | Accepted |
| [0002](0002-flatbuffers-transport.md) | Talk FlatBuffers RawImage to Hyperion with a hand-written, verified codec | Accepted |
| [0003](0003-jellyfin-version-support.md) | Support Jellyfin 10.11 and 12 from one codebase and one repository URL | Accepted |
| [0004](0004-core-library-boundary.md) | Keep all logic in a Core library without Jellyfin references | Accepted |
| [0005](0005-documentation-tooling.md) | Docs as Markdown in the repository, built with Material for MkDocs | Accepted |
| [0006](0006-release-automation.md) | release-please releases; plugin manifest generated from release assets | Accepted |
