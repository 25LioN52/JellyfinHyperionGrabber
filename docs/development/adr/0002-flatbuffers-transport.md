# 0002: FlatBuffers RawImage transport with a hand-written codec

**Status:** Accepted (2026-10-04)

## Context

Hyperion.ng and HyperHDR accept external pictures over a FlatBuffers TCP server (port 19400). Their request and reply
schemas are identical apart from the namespace, which is not part of the binary format. Alternatives: Hyperion.ng's
protobuf server (not in HyperHDR), the JSON-RPC API (base64 images, much more overhead), or driving LED devices
directly (loses Hyperion's layout, calibration and device support).

The schema needed is tiny and frozen (four commands, one reply). The official FlatBuffers C# runtime would add a DLL to
the plugin folder, where it can conflict with other plugins' copies inside the Jellyfin process, and it builds
messages in its own buffer, adding a copy per frame.

## Decision

- Use the FlatBuffers server with `RawImage` RGB24 frames, `Register` with a priority in 100-199, and `Clear` on stop.
- Encode with a small hand-written front-to-back FlatBuffers writer that writes directly into a reused buffer, and
  decode replies with a bounds-checked reader. No runtime dependency.
- Verify the codec in tests against the official Google.FlatBuffers runtime and verifier, plus golden bytes.
- Do not use `NV12Image` for now: the RGB path is simplest for FFmpeg output and both servers support it identically.

## Consequences

- One codec serves both servers; the plugin ships no third-party assemblies.
- Protocol code must stay small and well tested; any change needs the official-verifier tests to pass.
- If we ever need more of the schema, revisit using generated code (and this ADR).
