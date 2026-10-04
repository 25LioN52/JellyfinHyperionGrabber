# 0004: Core library without Jellyfin references

**Status:** Accepted (2026-10-04)

## Context

Jellyfin's plugin APIs change between lines (see ADR 0003), Jellyfin types are hard to construct in tests, and the
interesting logic (protocol, frame pipeline, sync) does not depend on Jellyfin at all.

## Decision

Split the code into `Jellyfin.Plugin.HyperionGrabber.Core`, which must not reference any Jellyfin package, and a thin
`Jellyfin.Plugin.HyperionGrabber` adapter. Core defines the interfaces it needs (for example "current playback
state", "media path and FFmpeg settings"); the adapter implements them with Jellyfin services.

## Consequences

- Core is unit-testable with fakes and real sockets, fast, and unaffected by Jellyfin API churn.
- API differences between Jellyfin lines touch only the adapter.
- The engine could later be hosted elsewhere (for example a standalone service for other media servers) without
  rewriting it.
- Two assemblies ship in the plugin zip instead of one.
