# Hyperion protocol

The plugin talks to Hyperion.ng and HyperHDR over their **FlatBuffers server** (TCP, default port 19400). This page
records what the servers actually do, verified against their source code in October 2026, and how our codec is built
and verified.

## Sources

| Server | Files |
| --- | --- |
| Hyperion.ng 2.2.x | `libsrc/flatbufserver/hyperion_request.fbs`, `hyperion_reply.fbs`, `FlatBufferClient.cpp` |
| HyperHDR 22.x | `include/flatbuffers/parser/hyperhdr_request.fbs`, `hyperhdr_reply.fbs`, `sources/flatbuffers/server/FlatBuffersServerConnection.cpp`, `sources/flatbuffers/parser/FlatBuffersParser.cpp` |

## Framing

Each message in either direction is a **4-byte big-endian unsigned length** followed by that many bytes of one
FlatBuffer. (Some third-party write-ups claim little-endian; both servers parse the length most significant byte first.)

## Schema

Both servers use the same schema; only the namespace differs (`hyperionnet` / `hyperhdrnet`), and FlatBuffers does
not put namespaces on the wire.

```text
table Register  { origin:string (required); priority:int; }
table RawImage  { data:[ubyte]; width:int = -1; height:int = -1; }
table NV12Image { data_y:[ubyte]; data_uv:[ubyte]; width:int; height:int; stride_y:int = 0; stride_uv:int = 0; }
union ImageType { RawImage, NV12Image }
table Image     { data:ImageType (required); duration:int = -1; }
table Clear     { priority:int; }
table Color     { data:int = -1; duration:int = -1; }
union Command   { Color, Image, Clear, Register }
table Request   { command:Command (required); }
root_type Request;

table Reply     { error:string; video:int = -1; registered:int = -1; }
```

Wire identifiers (field slot = declaration order; a union field takes two slots, type then value; the vtable offset
of slot *n* is `4 + 2n`):

| Union | Members |
| --- | --- |
| `Command` | `NONE=0`, `Color=1`, `Image=2`, `Clear=3`, `Register=4` |
| `ImageType` | `NONE=0`, `RawImage=1`, `NV12Image=2` |

## Session behaviour

1. **Connect** to the FlatBuffers port.
2. **Register** with `origin` (shown in Hyperion's priority list) and `priority`.
    - Hyperion.ng accepts **100-199** only; other values get an error reply
      (*"The priority X is not in the priority range between 100 and 199"*). HyperHDR has no range check.
    - Success reply: `registered = priority`. Both servers send it.
3. **Send images**: `Image { RawImage { data, width, height }, duration }`.
    - `data` is RGB24, row-major, exactly `width * height * 3` bytes (Hyperion.ng also accepts 4 bytes per pixel).
    - `duration` in ms; `-1` means until replaced. We send `-1`.
    - Hyperion.ng replies to every image with an empty success reply; HyperHDR replies too. **Replies must be read**,
      otherwise they pile up in socket buffers.
4. **Clear** with our priority to release it. `priority = -1` clears **all** priorities: never send it.
5. **Disconnect**: both servers clear the client's priority when the connection closes.

**Idle timeout.** Both servers close a connection that has not sent anything for a configured time. To hold the last
frame during pause, resend it periodically (well inside the timeout). Use the server's setting, not a guess, when M2
implements this.

**Images vs LED count.** Hyperion averages the picture regions configured in its LED layout, so a small picture
(around 64-160 px wide) is enough and keeps CPU use on the Hyperion side low. Keep the source aspect ratio.

## Our codec

`HyperionRequestWriter` uses the tiny `FlatBufferWriter` instead of the official FlatBuffers runtime, which keeps the
plugin dependency-free and lets us encode straight into a reused buffer ([ADR 0002](adr/0002-flatbuffers-transport.md)).

Layout rules (front to back):

- root `uoffset` at position 0;
- each table is preceded by its vtable, so the table's `soffset` is positive;
- every inline field occupies a 4-byte slot (a `ubyte` union type is padded to 4 bytes), so all fields are aligned;
- referenced objects (sub-tables, strings, vectors) come after the field that points to them, because `uoffset`s
  are unsigned and must point forward;
- strings are null-terminated; vectors and strings start 4-byte aligned.

Example, `Clear { priority: 150 }`, which is also the golden-bytes test:

```text
00000028                 size prefix (big-endian): 40 bytes
0C000000                 root uoffset -> Request table at 12
08000C00 04000800        Request vtable: 8 bytes, table 12 bytes, fields at 4 and 8
08000000                 Request table: soffset 8 -> vtable at 4
03000000                 command_type = Clear (3), padded to 4 bytes
0C000000                 command uoffset -> Clear table at 32
06000800 04000000        Clear vtable: 6 bytes, table 8 bytes, field at 4, 2 bytes padding
08000000                 Clear table: soffset 8 -> vtable at 24
96000000                 priority = 150
```

`HyperionReplyReader` decodes replies with full bounds checking; malformed input only ever raises
`HyperionProtocolException` (fuzzed with random and truncated input in the tests).

## How the codec is verified

- `OfficialHyperionCodec` (tests) decodes our output with the official Google.FlatBuffers runtime, after running the
  official **verifier** with alignment checks, the same checks Hyperion's C++ server performs before accepting a message.
- Replies in tests are built with the official `FlatBufferBuilder`.
- A golden-bytes test locks the exact layout.
- The harness verifies itself against buffers from the official builder. Google.FlatBuffers 25.2.10's
  `Verifier.VerifyUnion` passes a wrong type id to its callback, so the harness verifies unions as "type field, then
  `VerifyTable` on the value".
- *(planned, M1)* An end-to-end test against a real Hyperion.ng in a container.
