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
- With debug logging enabled for the plugin, `Playback started on <device> (<device id>, …)` lines show every playback
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
| `Streaming to Hyperion stopped … Could not connect` / `Lost the connection` | Hyperion was unreachable or restarted | Check Hyperion; the lights come back with the next playback. Reconnecting during playback is *(planned)*. |
| `Streaming to Hyperion stopped … FFmpeg exited with code …` | FFmpeg could not decode the file | The message includes FFmpeg's error. Hardware decoding problems fall back to the CPU by themselves (warning `Hardware decoding with … failed`). |

When playback stops, `Stopped streaming to Hyperion: N frames sent, M dropped` tells how it went. Many dropped frames
mean Hyperion, the network or the decoder could not keep up: lower the **Frame rate** on the plugin page, or enable
hardware decoding for the video's codec in Jellyfin.

## Docker on a NAS (for example UGREEN, Synology, Unraid)

- Jellyfin in a container reaches Hyperion on the same NAS through the NAS's LAN IP (bridge network) or
  `localhost` (host network). `host.docker.internal` only works with `extra_hosts: ["host.docker.internal:host-gateway"]`.
- If Hyperion runs in a container too, publish port 19400 (`-p 19400:19400`) or put both containers on the same
  Docker network and use the container name.

## Still stuck?

Ask in [GitHub Discussions](https://github.com/25LioN52/JellyfinHyperionGrabber/discussions) with your plugin and
Jellyfin versions, Docker or native, Hyperion.ng/HyperHDR version and the log lines mentioning `HyperionGrabber`.
