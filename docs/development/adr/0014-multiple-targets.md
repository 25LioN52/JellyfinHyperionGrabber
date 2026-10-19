# 0014: Several targets, each a Hyperion server and priority with its own devices

**Status:** Accepted (2026-10-19)

## Context

Up to 0.7 the plugin had one Hyperion target. A home with LEDs on two TVs could light only one of them, and of all
selected devices the most recently started playback won, so a movie in the bedroom took the living room's lights.

The roadmap asked for "different Hyperion servers or instances (one per TV)". Instances are not possible: Hyperion.ng
and HyperHDR hand every FlatBuffers picture to every running instance (`FlatBufferClient` emits
`setGlobalInputImage` in Hyperion.ng, HyperHDR's `FlatBuffersServerConnection` does the same, and the end-to-end tests
confirm it). A FlatBuffers client cannot pick an instance, and priorities cannot keep one TV's picture off another's
instance either. Instances stay what they are: several LED devices (WLED, Hue) showing the same picture.

The configuration is a public contract (rule 6): `HyperionHost`, `HyperionPort`, `HyperionPriority` and
`PlaybackDevices` are on users' servers.

## Decision

- **A target is a Hyperion server and priority** (`PlaybackTarget`: name, `HyperionClientOptions`, device ids), identified
  by `PlaybackTargetKey` (host compared ignoring case, port, priority). Two TVs need two servers: two devices, or two
  Hyperion containers on different FlatBuffers ports.
- **Configuration without a migration:** the existing fields stay the first target (plus an optional `HyperionName`);
  new targets go in `AdditionalTargets` (name, host, port, priority, devices). Old XML loads unchanged and means one
  target. Additional entries without a host are ignored. Users, frame rate, timing offset, pause release and Kodi
  settings stay shared.
- **The filter** holds the targets. Of targets with the same key only the first is kept (they would fight over one
  priority), and at most 8, because every playing target costs an FFmpeg process on the server. The configuration page
  checks both before saving.
- **The monitor decides per target:** the most recently started matching playback of that target's devices drives it.
  Targets are independent; a playback on a device selected for two targets drives both (two sessions, two decoders:
  rare, and simpler than sharing one decoder between sessions with different Hyperion connections). A target whose key
  changes is a new target, so editing a server, port or priority restarts its session; renaming it does not. Sessions
  that end together (one playback on two targets, a filter change) are disposed at the same time, so a hanging
  Hyperion delays only its own target. Last problems are kept per target and dropped with it.
- **Release ordering per key:** a new session waits for the previous session of the same server and priority to
  release it (as before for the one target); sessions of other targets never wait for each other.
- **Status:** `PlaybackMonitorStatus` lists every target (`TargetStatus`); the status API returns one overall sentence
  and one entry per target. With one target the panel looks as before.

## Consequences

- Each TV's lights follow that TV only. Existing installations keep working without any change.
- The first target lives in top-level fields and the others in a list. The asymmetry is the price of not migrating
  saved configurations; the page hides it by showing the first server at the top and the others under **More TVs**.
- Two TVs on one Hyperion server with two instances cannot be told apart; the docs say so and show the alternative.
- Tests: target filtering and keys; monitor tests for independent targets, a shared device, concurrent stops, key and
  name changes and per-target status; session tests for per-key release ordering and the target's server; an
  integration test with real FFmpeg and two fake Hyperion servers; configuration tests for old XML, the new XML and the
  page's JSON.
