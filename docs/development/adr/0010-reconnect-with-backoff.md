# 0010: Reconnect to Hyperion with backoff from a connect task the ticks check

**Status:** Accepted (2026-10-09)

## Context

Until 0.4 any Hyperion failure ended the stream for the rest of the playback ([ADR 0008](0008-streaming-session-pacing.md)):
a refused connect, a timeout, a lost connection, a rejected registration. Hyperion often runs in Docker on a NAS and
restarts on container updates, NAS reboots and some settings changes, so the lights stayed off until the next movie.

Constraints:

- Connecting can take up to `ConnectTimeout` + `ReplyTimeout` (5 s + 5 s). The tick loop paces frames at up to 60 fps
  and must not freeze that long, or the decoder-lag restart logic sees a 10 s gap.
- FFmpeg blocks when the frame pool is full, so frames must keep flowing back to the pool while disconnected.
- Stopping must stay bounded by `StopTimeout` (5 s), kill FFmpeg and leave no connection open.
- A long pause releases Hyperion on purpose; that must not turn into reconnect attempts.

Options considered:

- **Connect inline in the tick, as before, with a retry delay:** simplest, but each attempt can stall the loop for 10 s.
- **A separate reconnect loop task that hands over a connection:** decouples timing, but a second task with its own
  lifetime, cancellation and hand-over races for what one field can do.
- **One connect task owned by the session, started by a tick and checked by later ticks.**

## Decision

The session keeps at most one connect in flight (`Task<IHyperionConnection>` plus a linked cancellation source).
A tick that has a frame to send and no connection starts it, if the backoff wait is over; later ticks check
`IsCompleted` and take the result, never awaiting it. A connect that completes synchronously is used in the same tick.

- **Backoff:** after the n-th consecutive failure the next attempt waits `min(2^(n-1), 30)` s: 1, 2, 4, 8, 16, 30,
  30 ... A lost connection counts as the first failure, so the first reconnect comes 1 s after the loss. The schedule
  runs on the injected `TimeProvider`; it is a timestamp the ticks compare with, not a timer. A **successful send**
  resets it (not a successful connect: a server that accepts and then drops every connection still backs off).
- **What is retried:** `HyperionConnectionException` (refused, timed out, lost, closed before confirming) and also
  `HyperionProtocolException` (Hyperion rejected the registration). A rejection can be temporary (an instance that is
  being enabled or still starting), our settings are already validated (the priority range), and one attempt every
  30 s costs nothing. Anything else is a bug and ends the session as before; FFmpeg failures (`FrameSourceException`)
  still end the stream.
- **While disconnected**, for the first minute (`StopDecodingAfter`), decoding continues, due frames are copied as
  the last frame and returned to the pool (counted as dropped), and no keep-alives are sent. The first frame after
  reconnecting is the one due at that tick, so the lights resume at the playback position. Seeks, the position tracker
  and the light timing offset are untouched.
- **A longer outage** stops FFmpeg; the connect attempts go on (every 30 s by then). When one succeeds, FFmpeg starts
  at the playback position, as on resume after a pause release, and seeks made meanwhile are covered by that. Without
  this, Hyperion switched off on purpose or a wrong address would make every playback decode the whole video for
  nothing. A decoder started during the outage (a seek, or a connection that dropped again before its first frame)
  gets the full minute again.
- **Pause release** gives up a connect in flight and resets the backoff; no attempts are made while released, and on
  resume the first frame connects at once.
- **Stop** cancels the connect in flight without waiting for it; should it still return a connection, that connection
  is disposed at once. `DisposeAsync` therefore never waits for a connect, and a disposed session never starts one.
- **One priority holder.** After a stop timeout the old session may still be clearing its priority while a new one
  registers the same priority; the old Clear or close can then remove the new session's input in Hyperion. The factory
  passes each session the previous session's `HyperionReleased` task (its connection closed and no connect left in
  flight, without waiting for FFmpeg), and the new session's first connect waits for it, at most `StopTimeout`.
- **Logging:** one Warning when the connection is lost or cannot be made, Debug per failed attempt with the next delay,
  Information on reconnect with the attempt count and downtime. `HyperionClient` no longer logs a Warning when the
  server closes the socket before confirming the registration (Docker's port proxy accepts connections while the
  container restarts); the caller reports it.

## Consequences

- The lights come back within about a second of Hyperion being reachable after a short restart, and at most 30 s
  after a long outage. A pause longer than the release time ends the attempts until resume.
- While Hyperion is down the server still decodes the video for up to a minute; that is the price of resuming at the
  right picture without restarting FFmpeg after a short restart. After a longer outage the lights return about as fast
  as after a long pause (FFmpeg opens and seeks the file first).
- A connect that ignores cancellation can keep a socket open for its own timeouts after a stop; its connection is closed
  when it completes. `HyperionClient` honours cancellation, so in practice it ends at once.
- A new session may wait up to 5 s for a previous session stuck in its release before connecting.
- Tests: `FakeTimeProvider`-driven session tests for the backoff schedule, reset, pool, a connect that never completes,
  dispose during a wait and during a connect, pause release while disconnected, and the hand-over between sessions;
  one test with the real `HyperionClient` against `FakeHyperionServer.DropConnections()`.
