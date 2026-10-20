# Configuration

Open **Dashboard → Plugins → Hyperion Grabber**. Settings are saved with **Save**; the **Test connection** and
**Send test pattern** buttons use the values currently in the form, so you can try them before saving.

## Status

The **Status** section at the top of the page shows what the lights are doing, updated every few seconds while the
page is open. With [several Hyperion servers](#more-tvs) each one gets its own entry with these lines:

| Line | Meaning |
| --- | --- |
| First line | What happens now: waiting for playback, starting, streaming, paused, the lights released after a long pause, reconnecting to Hyperion, or why the lights do not follow the current playback. If **Follow playback** is off or no device is selected, it says so. |
| ✖ … | What keeps the lights from following playback right now, for example `Hyperion is unreachable: Could not connect …`. |
| Playing | The video and the device that drive the lights. |
| Position | Where the plugin thinks the playback is, and how far off that can be (±). With the Jellyfin add-on for Kodi the ± shrinks with every pause, resume and seek. |
| Pictures | Pictures sent to Hyperion per second compared to the frame rate you set, how many were sent and dropped, and how often decoding restarted (seeks). Many drops or a rate well below the setting mean the server, the network or Hyperion cannot keep up. |
| Timing | Where the position comes from: **Kodi's clock** when the plugin reads it from Kodi, otherwise the client's reports to Jellyfin (with the reason if Kodi's clock cannot be used). |
| Decoder | The hardware decoder in use, or **CPU**. *Hardware decoding failed* means Jellyfin's hardware acceleration did not work for this video; see the log for FFmpeg's message. |
| Picture | The size of the picture sent to Hyperion, and for HDR video how it becomes SDR, for example `160×90, HDR10, tone mapped to SDR with tonemapx`. See [HDR and Dolby Vision](#hdr-and-dolby-vision). |
| Last problem | The most recent problem with its time, also after it was resolved (for example a Hyperion restart during the last movie). |

## Hyperion server

| Setting | Default | Description |
| --- | --- | --- |
| **Name (optional)** | *(empty)* | Shown in the status when you have [several Hyperion servers](#more-tvs), for example `Living room`. |
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

### Kodi's clock

Jellyfin for Kodi tells Jellyfin the position only in whole seconds and, while playing, only every few minutes. For
exact timing the plugin reads the position from Kodi itself over Kodi's remote control interface (JSON-RPC on TCP port
9090), at the address Jellyfin sees for the Kodi device. In Kodi turn on:

**Settings → Services → Control → Allow remote control from applications on other systems**

The plugin only reads where the video is; it never controls Kodi. Kodi's remote control interface has no password,
so with this setting on, any device on your network can control Kodi; that is how Kodi works. The **Timing** line of
the [status](#status) shows whether Kodi's clock is used. Without the setting, or when Kodi cannot be reached (a
firewall, or Jellyfin seeing a proxy instead of the Kodi device), the plugin uses Jellyfin's reports as before.

### HDR and Dolby Vision

LEDs cannot show HDR, so the plugin turns HDR video into SDR colors before sending it (tone mapping). There is nothing
to set up: HDR10, HDR10+, HLG and Dolby Vision, including profile 5 (common in streaming rips, where it otherwise shows
green and purple), are detected from Jellyfin's media info and mapped automatically. Jellyfin's own **Enable tone
mapping** setting under **Dashboard → Playback → Transcoding** only affects transcoding for clients, not the lights.

The plugin uses Jellyfin's FFmpeg, and Jellyfin's bundled FFmpeg (in every official package and Docker image) does
all of this. With a custom FFmpeg build, HDR10 and HLG need zimg (the `zscale` filter), and Dolby Vision profile 5
keeps wrong colors; the **Picture** line of the [status](#status) says so. Tone mapping happens on the small picture
after scaling and adds almost no load.

If Hyperion restarts or the network drops during playback, the plugin reconnects by itself (after 1 s, then up to every
30 s) and the lights continue at the current picture; see
[Hyperion restarted during playback](troubleshooting.md#hyperion-restarted-during-playback).

| Setting | Default | Description |
| --- | --- | --- |
| **Follow playback** | off | Turns playback detection on. |
| **Frame rate** | `25` | Pictures per second sent to Hyperion, 1-60. 25 looks smooth; lower it (for example to 10-15) if the Jellyfin server is short on CPU, especially without hardware decoding. Applies from the next playback. |
| **Read the position from Kodi (precise timing)** | on | For playback in Kodi, the plugin asks Kodi itself where the video is, to the millisecond, instead of relying on Jellyfin's whole-second reports. Needs **Settings → Services → Control → Allow remote control from applications on other systems** in Kodi; without it nothing changes. Other clients are not affected. See [Kodi's clock](#kodis-clock). |
| **Kodi remote control port** | `9090` | Kodi's JSON-RPC TCP port. Applies from the next playback. |
| **Light timing offset (ms)** | `0` | Shifts the lights against the picture, -2000 to 2000. If the lights change **after** the TV picture, increase it; if they change **before**, make it negative. See [Lights are late or early](troubleshooting.md#the-lights-are-late-or-early). Applies from the next playback. |
| **Release the lights after pausing for (seconds)** | `15` | While paused the lights hold the picture; after this time FFmpeg stops and Hyperion shows its default (effect, other source or off) until you resume. `0` holds the picture for the whole pause. |
| **Devices** | none | Devices whose playback drives the lights of the Hyperion server above, usually the TV your LEDs are mounted on. With none selected (here and under [More TVs](#more-tvs)), no playback does. |
| **Users (optional)** | none | Only playback by these users drives the lights. Leave all unticked to react to every user. |

The lists show the devices and users of Jellyfin's recent sessions, most recently active device first. If your TV is
missing, start playing something on it and press **Refresh**. Saved devices stay in the list (marked
*not seen recently*) while they are offline. Jellyfin identifies a device by its app installation, so after
reinstalling the Jellyfin app or add-on on the TV, select the device again.

If two devices selected for the same Hyperion server play at the same time, the one that started most recently drives
the lights; when it stops, the other one takes over. Devices of [other Hyperion servers](#more-tvs) do not affect it.

The picture is decoded with Jellyfin's FFmpeg and the hardware acceleration set under
**Dashboard → Playback → Transcoding**, scaled to 160 pixels wide in the shape the TV shows (anamorphic DVDs
included) and, for HDR video, tone mapped to SDR; see [Installation](installation.md#docker-notes). Black bars of
widescreen films are handled by Hyperion; see [Black bars](hyperion-setup.md#black-bars-widescreen-films).

## More TVs

For LEDs on a second TV, press **Add a Hyperion server** under **More TVs**, enter that TV's Hyperion server (name,
host, FlatBuffers port, priority), use its own **Test connection** and **Send test pattern**, and tick the devices
that play on that TV. Up to 8 Hyperion servers in total.

- **Each TV needs its own Hyperion.ng or HyperHDR server**, for example a Raspberry Pi per TV or a second Hyperion
  container on another port; see [Several TVs](hyperion-setup.md#several-tvs). Instances of one server all show the
  same picture, so they cannot serve two TVs.
- Each server follows only its own devices: a movie started in the bedroom does not take the living room's lights.
- A device ticked for two servers drives both (for example a strip on one server and room lamps on another). Each
  costs its own decoder on the Jellyfin server.
- **Users (optional)**, frame rate, light timing offset, pause release and Kodi's clock apply to every server.
- Two servers may not use the same host, port and priority; the page says so when you save.
- With several servers the [status](#status) shows one entry per server, with its name.

Changing a server's host, port or priority while it plays restarts its lights at once; other changes apply from the
next playback.

## More playback settings *(planned)*

Later releases add a timing offset per device. See the [roadmap](../roadmap.md).
