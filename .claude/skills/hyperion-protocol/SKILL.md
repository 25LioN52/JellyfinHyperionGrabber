---
name: hyperion-protocol
description: Reference for the Hyperion.ng / HyperHDR FlatBuffers protocol as used by this plugin - framing, schema, union ids, priorities, replies, timeouts, server differences and how our hand-written codec is verified. Use when reading or changing code under Hyperion/ or the fake server, or when debugging connection problems.
user-invocable: false
---

# Hyperion FlatBuffers protocol (quick reference)

Authoritative write-up: `docs/development/hyperion-protocol.md`. Sources verified in October 2026:
Hyperion.ng `libsrc/flatbufserver/hyperion_request.fbs`, `hyperion_reply.fbs`, `FlatBufferClient.cpp`;
HyperHDR `include/flatbuffers/parser/hyperhdr_request.fbs`, `sources/flatbuffers/server/FlatBuffersServerConnection.cpp`,
`sources/flatbuffers/parser/FlatBuffersParser.cpp`.

## Framing

`[uint32 big-endian length][FlatBuffer]` in both directions over TCP (default port 19400).

## Schema (identical on both servers, namespaces differ and are not on the wire)

```
table Register { origin:string (required); priority:int; }
table RawImage { data:[ubyte]; width:int = -1; height:int = -1; }
table NV12Image { data_y:[ubyte]; data_uv:[ubyte]; width:int; height:int; stride_y:int = 0; stride_uv:int = 0; }
union ImageType { RawImage, NV12Image }             // 1, 2
table Image { data:ImageType (required); duration:int = -1; }
table Clear { priority:int; }
table Color { data:int = -1; duration:int = -1; }
union Command { Color, Image, Clear, Register }     // 1, 2, 3, 4
table Request { command:Command (required); }
table Reply { error:string; video:int = -1; registered:int = -1; }
```

Field slot = declaration index; a union takes two slots (type, value); vtable offset = 4 + 2 * slot.

## Behaviour

- Register first. Success reply has `registered = priority`; failure has `error`.
- Hyperion.ng accepts priorities **100-199** only and replies with an error otherwise. HyperHDR has no range check.
- Both servers send a reply after most commands (Hyperion.ng: success reply after every image). Drain them.
- Both servers drop a connection that sends nothing for a configurable timeout, and clear the client's priority
  when the connection closes. To hold the last frame (pause), resend it periodically.
- `Clear{priority:-1}` clears all priorities: never send it.
- RawImage data must be exactly width * height * 3 bytes (RGB24).

## Our implementation

- `HyperionRequestWriter` + `FlatBufferWriter`: front-to-back layout, vtable before table, every inline field in a
  4-byte slot, objects after the fields that reference them. Golden bytes in `WriteClear_ProducesExactWireFormat`.
- `HyperionReplyReader` + `FlatBufferReader`: bounds-checked; malformed input only raises `HyperionProtocolException`
  (fuzzed in tests).
- `HyperionClient`: connect (timeout) → Register (reply timeout) → serialized sends (the caller's token covers only
  the wait for the lock; a started write finishes or fails within the write timeout, so messages are never cut) →
  receive loop draining replies → Dispose sends Clear, closes the socket, then waits for in-flight senders. Safe to
  send concurrently with Dispose (senders get `HyperionConnectionException`).
- Verification: `OfficialHyperionCodec` (tests) decodes with Google.FlatBuffers and its verifier, the same checks
  Hyperion's C++ server runs. Note: Google.FlatBuffers 25.2.10's `Verifier.VerifyUnion` passes a wrong type to its
  callback, so the harness verifies unions as type field + `VerifyTable`.
