# 0009: Track the playback position as a range that every report narrows

**Status:** Accepted (2026-10-07)

## Context

The lights can only be as accurate as the plugin's idea of where the client is. Clients report their position to
Jellyfin now and then, and very differently ([Jellyfin integration](../jellyfin-integration.md#playback-events-input-for-m1m2)):
most apps report milliseconds every 3-10 s, but Jellyfin for Kodi truncates the position to **whole seconds** and,
during playback, reports only about **every 4 minutes** (plus pause, resume and seek). Its start report carries the
requested start position rather than the player's clock.

Until 0.3 the session moved its estimate to every report (`last report + elapsed`). With Kodi each report put the
estimate 0-1 s behind the real position, by a different amount each time, so the lights' delay changed after every
pause, resume or periodic report. The first real test (Kodi on LibreELEC, WLED) showed about 1 s of delay that a
constant light timing offset could not remove.

Options considered:

- **Re-anchor on every report** (status quo): exact for precise clients, up to 1 s late and jumping for Kodi.
- **Add half a second to whole-second reports:** right on average, but every report still moves the lights by up to
  half a second.
- **Read Kodi's clock over JSON-RPC:** precise, but only for Kodi, needs a Kodi setting and a second connection.
  Still possible later as an optional source; it does not help any other client.
- **Keep the range the real position can be in and narrow it with every report.**

## Decision

A `PositionTracker` (Core) per session keeps the range `[earliest, latest]` of the real position at a point in time:

- The first report gives ± 1 s: clients send it at different moments around the first picture.
- A report in whole seconds (above 0) is taken as truncated, covering the following second; any other position is
  taken as it is. Both get ± 100 ms for the time the report took. Position 0 is taken as exact (every client reports
  the very start as 0).
- A report that overlaps the range narrows it to the overlap; one that does not (a seek, or clocks that drifted apart)
  replaces it.
- While playing the range moves on with time and widens by 0.1 % of the elapsed time on each side (the client may play
  slightly faster or slower than the server's clock, for example 24 Hz output of 23.976 fps video). While paused it
  stays still.
- The estimate is the middle of the range. A report that moves the estimate by more than 1 s is a seek (decoding
  restarts there); the light timing offset is added on top, as before.

Each report is logged at Debug with how far it moved the estimate and the remaining uncertainty.

## Consequences

- Precise clients behave as before: a report narrows the range to about ± 100 ms around itself, and consistent
  reports average out small jitter.
- Jellyfin for Kodi no longer jumps up to a second late at each report. After a truncated report the estimate is the
  middle of that second (at most about half a second off), and every further report at another point of a second
  narrows it: in a test with four Kodi-like reports the error went from up to 0.95 s (re-anchoring) to 0.01 s. A
  truncated pause or resume report that agrees with a precise estimate leaves it untouched.
- Right after the start, a Kodi client can still be up to about half a second off until a pause, seek or periodic
  report narrows the range; an optional Kodi clock (JSON-RPC) remains possible for exact sync from the first second.
- A precise client that happens to report an exact whole second gets a wider range than needed; the overlap with what
  is already known keeps the estimate, so this only costs precision right after a seek.
- The rules use only the reports' values and arrival times, so they work for any client, including ones not checked
  yet; clients that report more often converge faster.
