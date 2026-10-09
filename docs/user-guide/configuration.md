# Configuration

Open **Dashboard → Plugins → Hyperion Grabber**. Settings are saved with **Save**; the **Test connection** and
**Send test pattern** buttons use the values currently in the form, so you can try them before saving.

## Hyperion server

| Setting | Default | Description |
| --- | --- | --- |
| **Host** | *(empty)* | Host name or IP address of the machine running Hyperion.ng or HyperHDR. See [Docker notes](installation.md#docker-notes) if Jellyfin runs in a container. |
| **FlatBuffers port** | `19400` | Port of Hyperion's FlatBuffers server. Not the web UI port (8090) and not the JSON-RPC port (19444). |
| **Priority** | `150` | Hyperion priority of the Jellyfin picture, 100-199. Lower numbers win. |

### Choosing a priority

Hyperion shows the source with the **lowest** priority number that is active. Typical values:

| Source | Usual priority |
| --- | --- |
| Colors/effects set manually in the Hyperion UI or apps | 1 - 50 |
| **Jellyfin (this plugin)** | **150** |
| Hyperion's own USB/screen grabbers | 240 - 250 |
| Background effect | 254 |

With the default, starting a movie overrides grabbers and the background effect, while a color you pick in the
Hyperion app still wins. Hyperion.ng only accepts 100-199 from FlatBuffers clients.

## Playback

Choose which playback drives the lights. Nothing happens until you turn on **Follow playback** and select at least
one device.

While a matching video plays, the plugin decodes it on the server and sends the picture to Hyperion. Pausing holds
the last picture; after a longer pause (15 s by default) the lights go back to Hyperion's default until you resume.
Seeking jumps with the video, and stopping releases the priority so Hyperion falls back to whatever it shows
otherwise. Only videos from your libraries are streamed; see
[what is not supported](troubleshooting.md#the-lights-do-not-follow-a-video).

If Hyperion restarts or the network drops during playback, the plugin reconnects by itself (after 1 s, then up to every
30 s) and the lights continue at the current picture; see
[Hyperion restarted during playback](troubleshooting.md#hyperion-restarted-during-playback).

| Setting | Default | Description |
| --- | --- | --- |
| **Follow playback** | off | Turns playback detection on. |
| **Frame rate** | `25` | Pictures per second sent to Hyperion, 1-60. 25 looks smooth; lower it (for example to 10-15) if the Jellyfin server is short on CPU, especially without hardware decoding. Applies from the next playback. |
| **Light timing offset (ms)** | `0` | Shifts the lights against the picture, -2000 to 2000. If the lights change **after** the TV picture, increase it; if they change **before**, make it negative. See [Lights are late or early](troubleshooting.md#the-lights-are-late-or-early). Applies from the next playback. |
| **Release the lights after pausing for (seconds)** | `15` | While paused the lights hold the picture; after this time FFmpeg stops and Hyperion shows its default (effect, other source or off) until you resume. `0` holds the picture for the whole pause. |
| **Devices** | none | Devices whose playback drives the lights, usually the TV your LEDs are mounted on. With none selected, no playback does. |
| **Users (optional)** | none | Only playback by these users drives the lights. Leave all unticked to react to every user. |

The lists show the devices and users of Jellyfin's recent sessions, most recently active device first. If your TV is
missing, start playing something on it and press **Refresh**. Saved devices stay in the list (marked
*not seen recently*) while they are offline. Jellyfin identifies a device by its app installation, so after
reinstalling the Jellyfin app or add-on on the TV, select the device again.

If two selected devices play at the same time, the one that started most recently drives the lights; when it stops,
the other one takes over.

The picture is decoded with Jellyfin's FFmpeg and the hardware acceleration set under
**Dashboard → Playback → Transcoding**, scaled to 160 pixels wide; see [Installation](installation.md#docker-notes).

## More playback settings *(planned)*

Later releases add: a timing offset per device, HDR tone mapping, and multiple Hyperion targets (one per TV). See the
[roadmap](../roadmap.md).
