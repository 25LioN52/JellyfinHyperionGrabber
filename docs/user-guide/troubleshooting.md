# Troubleshooting

Start with **Test connection** on the plugin page: its message usually names the problem. Then check Jellyfin's log
(**Dashboard → Logs**) for lines containing `HyperionGrabber`.

## Connection test fails

| Message contains | Cause | Fix |
| --- | --- | --- |
| `Could not connect … refused` | Nothing listens on that host and port | Check the host, and that Hyperion's FlatBuffers server is enabled on that port (default 19400). |
| `Timed out … connecting` | Host unreachable: wrong IP, firewall, Docker network | Ping the host from the Jellyfin machine. In Docker, `localhost` is the container itself; see [Docker notes](installation.md#docker-notes). |
| `did not confirm the registration … Is this the FlatBuffers port` | Something answered, but it is not a FlatBuffers server | You probably entered the web UI port (8090) or JSON-RPC port (19444). Use 19400. |
| `rejected the registration: The priority … range` | Priority outside 100-199 | Use a priority between 100 and 199 (default 150). |
| `Request failed (401)` / `(403)` | Not signed in as an administrator | Plugin settings are admin-only. |

## Test pattern runs but the LEDs look wrong

See the table in [Checking with the test pattern](hyperion-setup.md#checking-with-the-test-pattern).

If the LEDs do not change at all while the test pattern reports success:

- Another source with a **lower** priority number is active in Hyperion (for example a color set in the Hyperion
  app, priority 1-50). Clear it in Hyperion's dashboard, or lower the plugin's priority number.
- The LED device in Hyperion is disabled or not connected; check that Hyperion's own effects work.
- With several Hyperion instances, you are looking at an instance that does not receive the stream.

## Playback is not recognized

Look in **Dashboard → Logs** for lines from `PlaybackMonitor`:

- `Playback filter: enabled False` or `0 device(s)`: turn on **Follow playback**, select the TV under **Devices** and
  press **Save**.
- No `Lights follow playback on …` line when you press play on the TV: that device, or the user, is not selected, or
  the client did not report playback (check that it shows under **Dashboard → Activity** while playing). The device id
  changes when the Jellyfin app or Kodi add-on is reinstalled; select the device again.
- With [debug logging](#debug-logging) enabled for the plugin, `Playback started on <device> (<device id>, …)` lines show every playback
  Jellyfin reports, including the ids to compare.

## The lights do not follow a video

When playback is recognized (`Lights follow playback on …`), the next lines in **Dashboard → Logs** say what happened:

| Log line contains | Cause | Fix |
| --- | --- | --- |
| `Streaming 160x90 at 25 fps to Hyperion from …` | Streaming started | If the LEDs still do not change, see [Test pattern runs but the LEDs look wrong](#test-pattern-runs-but-the-leds-look-wrong): the same priority and device checks apply. |
| `no Hyperion server is configured` | The host is empty | Enter the host on the plugin page and press **Save**. |
| `The lights cannot follow playback: …` | A saved setting is invalid, or Jellyfin has no FFmpeg configured | Fix the named setting; check **Dashboard → Playback → Transcoding → FFmpeg path**. |
| `The lights do not follow item … it is not a video from the library` | Live TV channel or music | Not supported: re-decoding Live TV would open a second tuner stream. |
| `… is not a local file but a Http stream` | `.strm` file or another remote source | Not supported: only files Jellyfin reads from disk can be decoded a second time. |
| `… folders and disc images are not supported yet` | DVD/Blu-ray folder or ISO | Remux to MKV, or wait for disc support. |
| `… no video stream with a known size` | Jellyfin has not analyzed the file | Rescan the library (**Dashboard → Libraries → Scan All Libraries**). |
| `Hyperion is unreachable; reconnecting every 1-30 s …` | Hyperion restarted, or the network or Hyperion is down | Nothing, if Hyperion comes back: the plugin keeps retrying during playback; see [Hyperion restarted during playback](#hyperion-restarted-during-playback). Otherwise check Hyperion with **Test connection**. |
| `Streaming to Hyperion stopped … FFmpeg exited with code …` | FFmpeg could not decode the file | The message includes FFmpeg's error. Hardware decoding problems fall back to the CPU by themselves (warning `Hardware decoding with … failed`). |

When playback stops, `Stopped streaming to Hyperion: N frames sent, M dropped` tells how it went. Many dropped frames
mean Hyperion, the network or the decoder could not keep up: lower the **Frame rate** on the plugin page, or enable
hardware decoding for the video's codec in Jellyfin.

## Hyperion restarted during playback

When Hyperion restarts (a Docker container update, a NAS reboot, some settings changes) or the network drops, the
lights go off and come back by themselves while the video keeps playing. The plugin keeps decoding and tries to
reconnect after 1 s, then after 2, 4, 8 and 16 s, and from then on every 30 s. Usually Hyperion is back within a few
seconds; after a long outage the lights can take up to 30 s longer than Hyperion. They come back at the current picture,
not the one from when the connection was lost. Pausing longer than the pause release time stops the attempts until you
resume.

In **Dashboard → Logs** an outage looks like this:

```text
[WRN] Hyperion at 192.168.1.10:19400 closed the connection
[WRN] Hyperion is unreachable; reconnecting every 1-30 s while decoding goes on: The connection to Hyperion at 192.168.1.10:19400 is closed.
[INF] Connected to Hyperion at 192.168.1.10:19400 with priority 150
[INF] Reconnected to Hyperion after 3 attempt(s) and 7 s; streaming again
```

There is one warning per outage, not one per attempt. With [debug logging](#debug-logging), every failed attempt adds
`Connecting to Hyperion failed (attempt N): …; next attempt in S s`. If the lights do not come back although Hyperion
runs again, check that its FlatBuffers server is enabled after the restart, and use **Test connection**.

## The lights are late or early

The plugin sends each picture when the client's reported playback position reaches it. The client, Hyperion's
smoothing, the network and the LED controller each add a little delay, and the sum differs per setup (around a second
with Kodi on a Raspberry Pi and WLED is not unusual). Because the server decodes ahead of the TV, the plugin can send
pictures early to cancel it:

1. Play a video with clear scene cuts and watch when the LEDs change compared to the TV.
2. Lights change **after** the picture: set **Light timing offset (ms)** to a positive value, for example `500`;
   **before** the picture: a negative value.
3. Save, start the video again (the offset applies from the next playback) and adjust in steps of 100-200 ms.

Also check Hyperion's own delay: **Configuration → Image Processing → Smoothing** (Hyperion.ng) adds its smoothing
time and any *update delay* to every change. A shorter smoothing time or an update delay of 0 makes the lights react
faster.

How precise the timing can be depends on the client ([Compatibility](../compatibility.md#jellyfin-clients)). Most apps
report their position to the millisecond every few seconds. The Jellyfin add-on for Kodi reports whole seconds and,
during playback, only every few minutes: right after starting a video the lights can be up to about a second off,
and every pause, resume or seek makes them more precise. Tune the offset after pausing and resuming a few times.

To see what the plugin knows, turn on debug logging for it (below) and look for
`Position report … moved the estimate by …; uncertainty ±…` lines: a large move means the client's report disagreed
with the plugin's estimate, the uncertainty shows how precise the estimate is.

## Debug logging

Jellyfin reads extra log settings from `logging.json` in its configuration folder (Docker: usually
`/config/config/logging.json`, next to `logging.default.json`). Create it with:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Override": {
        "Jellyfin.Plugin.HyperionGrabber": "Debug"
      }
    }
  }
}
```

Restart Jellyfin; the plugin's Debug lines then appear in **Dashboard → Logs**. Delete the file (and restart) to turn
it off again.

## Docker on a NAS (for example UGREEN, Synology, Unraid)

- Jellyfin in a container reaches Hyperion on the same NAS through the NAS's LAN IP (bridge network) or
  `localhost` (host network). `host.docker.internal` only works with `extra_hosts: ["host.docker.internal:host-gateway"]`.
- If Hyperion runs in a container too, publish port 19400 (`-p 19400:19400`) or put both containers on the same
  Docker network and use the container name.

## Still stuck?

Ask in [GitHub Discussions](https://github.com/25LioN52/JellyfinHyperionGrabber/discussions) with your plugin and
Jellyfin versions, Docker or native, Hyperion.ng/HyperHDR version and the log lines mentioning `HyperionGrabber`.
