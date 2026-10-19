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
      (*"The priority X is not in the priority range between 100 and 199"*). HyperHDR 22 sends no error; it logs one
      and ignores priorities outside 50-250.
    - Success reply: `registered = priority`. Both servers send it.
3. **Send images**: `Image { RawImage { data, width, height }, duration }`.
    - `data` is RGB24, row-major, exactly `width * height * 3` bytes (Hyperion.ng also accepts 4 bytes per pixel).
    - `duration` in ms; `-1` means until replaced. We send `-1`.
    - Both servers reply to every image with a success reply carrying `registered = <the connection's priority>`.
      **Replies must be read**, otherwise they pile up in socket buffers.
4. **Clear** with our priority to release it. `priority = -1` clears **all** priorities: never send it.
   Hyperion.ng then forgets the connection's priority (later replies say `registered = -1`, and later images are
   dropped until the client registers again); HyperHDR answers `Clear` and `Color` with an empty reply.
5. **Disconnect**: both servers clear the client's priority when the connection closes.

**Registering again.** A reply with `registered = -1` and no error means Hyperion.ng no longer shows our images:
an instance got an image for a priority it does not have, typically because its remote control (web UI, Home
Assistant, `hyperion-remote`: JSON-RPC `clear` / `clearall`) cleared it while our connection stayed open
(`Hyperion::setInputImage` → `globalRegRequired` → `FlatBufferClient::registationRequired`). It drops every such
image, so without a new `Register` the LEDs stay dark. `HyperionClient` therefore sends `Register` again before the
next image or color, at most once a second (Hyperion.ng also asks for images that were already on their way), and logs
it at Debug. It never registers before a `Clear`. The same rule covers the other `-1` replies: after our own `Clear`
on Hyperion.ng a new `Register` is needed anyway, and after HyperHDR's empty `Clear`/`Color` reply one extra
`Register` is harmless. HyperHDR 22 never asks: an instance registers a missing priority itself on the next image.

**Idle timeout.** Both servers close a connection that has not sent anything for a configured time (the FlatBuffers
server's `timeout`, 5 s by default). The FlatBuffers protocol cannot read that setting, so a streaming session resends
its last frame whenever no new frame was sent for 500 ms (pause, end of the video, slow decoder): safe for any timeout
of 1 s or more ([ADR 0008](adr/0008-streaming-session-pacing.md)).

**Images vs LED count.** Hyperion averages the picture regions configured in its LED layout, so a small picture
(around 64-160 px wide) is enough and keeps CPU use on the Hyperion side low. Keep the source aspect ratio.

**Origin.** Both servers list a FlatBuffers client as `<origin>@<client address>` with component `FLATBUFSERVER`;
the address comes from Qt's dual-stack socket, so IPv4 clients appear as `::ffff:a.b.c.d`.

## Multiple instances

Both servers run **one** FlatBuffers server for all instances (one instance per LED device), and every running
instance receives every client's registration, images and clears:

- **Hyperion.ng 2.2** (`FlatBufferServer.cpp`, `Hyperion.cpp`, `hyperiond.cpp`): the client connection emits
  `registerGlobalInput` / `setGlobalInputImage` / `clearGlobalInput` on `GlobalSignals`, and every `Hyperion` instance
  connects to them. When an instance starts while clients are connected, `HyperionDaemon` calls
  `FlatBufferServer::registerClients`, so it shows the stream at once. An instance that gets an image for a priority it
  does not know asks for a re-registration with a reply `registered = -1`, and we register again (see *Registering
  again* above). A `Register` goes to every instance, so it is harmless for those that still had the priority. When
  the last instance stops, the server closes and drops all connections.
- **HyperHDR 22** (`FlatBuffersServer.cpp`, `HyperHdrInstance.cpp`): images go to `GlobalSignals::SignalSetGlobalImage`,
  which every `HyperHdrInstance` handles; an instance registers the priority on the first image it receives, so an
  instance started later joins with the next frame.

So one plugin connection drives a WLED instance and a Hue instance together, each through its own LED layout,
smoothing and calibration; priorities are per instance. The end-to-end tests verify this on both servers, including
that the picture comes back after the remote control clears our priority.

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
- End-to-end tests run our client against a real Hyperion.ng (official APT package) and HyperHDR (official release
  package), each in Docker, and read the result back over their JSON-RPC API: the priority list and the colors each
  instance computes for its LEDs ([Testing](testing.md#end-to-end-tests-against-hyperionng-and-hyperhdr)).
