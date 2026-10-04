---
paths:
  - "src/**/Hyperion/**"
  - "tests/**/Hyperion/**"
  - "tests/Jellyfin.Plugin.HyperionGrabber.TestSupport/**"
---

# Hyperion protocol rules

Read `docs/development/hyperion-protocol.md` (or the `hyperion-protocol` skill) before changing protocol code.

- Wire format: `uint32` **big-endian** length, then one FlatBuffer `Request`. Replies use the same framing.
- Union ids: Command `Color=1, Image=2, Clear=3, Register=4`; ImageType `RawImage=1, NV12Image=2`. Identical in
  Hyperion.ng (`hyperionnet`) and HyperHDR (`hyperhdrnet`); namespaces are not on the wire.
- Session: connect → `Register{origin, priority}` → wait for a reply with `registered` (or `error`) → images.
  Disconnecting clears the priority on both servers; `Clear{priority}` does it explicitly. Never send priority -1.
- Priorities 100-199 only (Hyperion.ng rejects others). Lower number wins.
- Servers reply to most commands and drop idle connections after a timeout, so: always drain replies, and keep
  sending (repeat the last frame) while paused if the LEDs must hold.
- `FlatBufferWriter` lays buffers out front to back with 4-byte field slots; uoffsets must point forward.
- Every encoder change: run the official-verifier tests in `HyperionRequestWriterTests`; update the golden bytes
  test only if the layout intentionally changed, and document why in the PR.
- `FakeHyperionServer` decodes with the official Google.FlatBuffers runtime. Do not "fix" a failing test by changing
  the fake server to match our encoder; the fake server is the reference.
