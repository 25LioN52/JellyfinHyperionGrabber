# Testing

## Stack

- **xUnit v3** on **Microsoft.Testing.Platform** (opted in via `global.json`). Filters use
  `--filter-class` / `--filter-method`; coverage uses Microsoft's code coverage extension
  (`--coverage --coverage-output-format cobertura`).
- Every test project targets `net9.0` and `net10.0`: each test runs once per Jellyfin line.
- `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) for time, NSubstitute only for Jellyfin interfaces.

## Test projects

| Project | Covers |
| --- | --- |
| `Core.Tests` | Codec, reply parser, client against the fake server, options validation, test pattern, player, tester |
| `Tests` | Plugin wiring: ids and resources consistent across C#/HTML/JS/`plugin.json`, configuration XML compatibility, controller validation and behaviour |
| `TestSupport` | `FakeHyperionServer`, `RecordingSink`, `OfficialHyperionCodec` (shared, not a test project) |

## The fake Hyperion server

`FakeHyperionServer.Start()` listens on `127.0.0.1` with an OS-assigned port and records every request:

```csharp
await using var server = FakeHyperionServer.Start();                 // or RegistrationMode.Reject / Silent
await using var client = await HyperionClient.ConnectAsync(new() { Host = server.Host, Port = server.Port }, logger, ct);
var register = await server.NextRequestAsync<RegisterRequest>();     // awaits, with a 5 s timeout
server.DropConnections();                                            // simulate a Hyperion restart
```

It decodes with the **official** FlatBuffers runtime and verifier, so a test that passes proves Hyperion would accept
the bytes. Do not change the fake server to make our encoder pass.

## Kinds of tests and where they matter

| Kind | Example | Why |
| --- | --- | --- |
| Cross-implementation | encoder output decoded by Google.FlatBuffers | Our codec is hand-written |
| Golden bytes | `WriteClear_ProducesExactWireFormat` | Accidental layout changes are caught and must be justified |
| Robustness / fuzz | random and truncated replies | Network input is untrusted |
| Contract | test pattern colors and direction | Users follow the documented pattern |
| Consistency | GUID in C#, JS, `plugin.json` | Duplicated identifiers drift otherwise |
| Compatibility | old configuration XML still loads | Users' servers keep old files |
| Time-dependent | `PlayAsync_PacesFramesWithTheTimeProvider` | Deterministic, no wall-clock flakiness |

## Rules

- No fixed sleeps for synchronization (`TestHelpers.WaitUntilAsync`, awaitable fake server, `FakeTimeProvider`).
- Fixed seeds for random data.
- Network only on loopback with OS-assigned ports.
- Pass `TestContext.Current.CancellationToken`.

## Planned

- *(M1)* End-to-end test against a real Hyperion.ng container in CI: register, send the test pattern, read the
  priority list over JSON-RPC.
- *(M1)* FFmpeg pipeline tests with generated clips (solid colors, color bars, an HDR sample) asserting colors at known
  positions.
- *(M2)* Sync engine tests driven entirely by `FakeTimeProvider` and scripted playback events.
