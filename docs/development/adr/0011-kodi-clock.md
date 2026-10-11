# 0011: Read the playback position from Kodi over JSON-RPC

**Status:** Accepted (2026-10-11)

## Context

Jellyfin for Kodi reports the position in whole seconds and, during playback, only about every 4 minutes. Its start
report carries the requested start position, not the player's clock. `PositionTracker`
([ADR 0009](0009-position-tracking.md)) narrows its estimate with every report, but right after a start it can still
be off by up to a second, and by a different amount per playback. In the maintainer's tests one movie needed a light
timing offset above 100 ms and another exactly 100 ms. No offset setting can fix a variation like that.

Kodi itself knows the position to the millisecond and announces pause, resume and seek as they happen, through its
JSON-RPC API.

Options considered:

- **Jellyfin's reports only:** works for every client, but Kodi stays imprecise.
- **HTTP JSON-RPC (port 8080):** needs Kodi's web server and usually a user name and password, and it cannot push
  notifications, so it needs fast polling.
- **WebSocket JSON-RPC (port 9090):** pushes notifications, but needs a WebSocket client and handshake for what the raw
  TCP server already offers.
- **Raw TCP JSON-RPC (port 9090):** no authentication, notifications pushed, one setting in Kodi.

## Decision

For a playback whose client name contains "Kodi" (Jellyfin for Kodi, JellyCon), the session starts a `KodiClock`. It
connects to the client address Jellyfin sees (`SessionInfo.RemoteEndPoint`) on the configured port (default 9090) and
runs on its own task:

- **Requests:** `Player.GetActivePlayers` (the video player), `Player.GetItem` (`file`) and `Player.GetProperties`
  (`time`, `speed`). It only reads; it never controls Kodi.
- **When it reads:** every 2 s, and right after `Player.OnPause`, `OnResume`, `OnSeek`, `OnSpeedChanged`, `OnPlay`,
  `OnAVStart` and `OnStop`. A new playback in Kodi makes it look up the player and its item again.
- **Framing:** Kodi sends JSON objects back to back with no delimiter. `JsonObjectSplitter` splits them by counting
  braces and brackets outside strings, as Kodi's own server does. A message over 1 MiB ends the connection.
  Notifications that are not about the player are ignored.
- **Readings:** each reading is time-stamped at the middle of its round trip, with half the round trip plus 20 ms as
  its uncertainty. `PositionTracker.Apply(ClockReading)` treats it as a precise report, so readings and Jellyfin's
  coarse reports narrow the same range. A reading that moves the estimate by more than 1 s is a seek, often seconds
  before Jellyfin's report.
- **Trust:**
  - Readings are used only after one agrees with the client's reports, within 2 s plus the tracker's uncertainty.
    Another Kodi at the same address (reverse proxy, NAT) or another video therefore never moves the lights; the
    status panel says why the readings are ignored.
  - If the file Kodi plays names a Jellyfin item id (`/Videos/{id}/`, add-on playback) and it is not this item, the
    clock stops.
- **Failures:**
  - Kodi not reachable (remote control off, firewall): the clock gives no readings, says how to turn remote control
    on, and tries again every 30 s.
  - A lost connection is retried in the same way.
  - In both cases the session keeps using the client's reports, exactly as before.
- **Settings:** **Read the position from Kodi** (default on) and **Kodi remote control port** (default 9090). Both have
  defaults that keep old configurations working.

## Consequences

- With remote control turned on in Kodi, the position is known to within a few tens of milliseconds from the first
  reading. The light timing offset then only has to cover the constant delay of the TV, Hyperion and the LEDs, and
  should no longer differ between playbacks.
- The user has to turn on **Settings → Services → Control → Allow remote control from applications on other systems**
  in Kodi. It is off by default, and without it nothing changes.
- The plugin now also connects to the Kodi device at the address Jellyfin sees for it. That is documented in the FAQ
  and How it works, and can be turned off.
- Kodi's TCP JSON-RPC has no authentication. The plugin only reads, but anything on the network can talk to Kodi once
  the setting is on; this is Kodi's design, and the user guide says so.
- One extra task and one TCP connection per followed Kodi playback, and a few small requests every 2 s; nothing per
  frame.
- Tests: the splitter (chunking, strings, limits), the connection (split answers with notifications glued in front,
  errors, lost connection, refused connect) and the clock (readings, notification-driven reading, polling, other
  item, no player, unreachable, reconnect, dispose) run against a fake Kodi on loopback with fake time. Session tests
  with a fake clock cover agreement, disagreement, seeks and disposal.
