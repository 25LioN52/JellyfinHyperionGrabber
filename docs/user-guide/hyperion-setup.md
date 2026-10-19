# Hyperion.ng & HyperHDR setup

The plugin sends the picture; Hyperion.ng or HyperHDR decides which part of the picture each LED shows. This page
covers the three things to get right on the Hyperion side: the FlatBuffers server, the LED layout and your devices.

## FlatBuffers server

Both servers accept the plugin's frames on their **FlatBuffers server**, TCP port **19400** by default, which is
enabled out of the box.

=== "Hyperion.ng"

    1. Open the web UI (`http://<hyperion-host>:8090`).
    2. **Configuration → Network Services**: make sure the **Flatbuffers server** is enabled and note its port.

=== "HyperHDR"

    1. Open the web UI (`http://<hyperhdr-host>:8090`).
    2. In the network services settings, make sure the **FlatBuffers server** is enabled and note its port.

Use that port in the plugin. Do not use the web UI port (8090) or the JSON-RPC port (19444).

!!! warning "No authentication"
    The FlatBuffers protocol has no password. Anyone who can reach the port can control your LEDs, so keep it on your
    local network and never forward it to the internet.

## Running Hyperion next to Jellyfin

Hyperion can run on the same machine as Jellyfin (for example both in Docker on a NAS) or on another device such as
a Raspberry Pi. Either works; the plugin only needs a TCP connection to port 19400. When Jellyfin runs in Docker,
read the [Docker notes](installation.md#docker-notes) to pick the right host name.

## LED layout

Hyperion maps the picture to LEDs through the **LED layout**: for every LED, a rectangle of the picture whose average
color that LED shows. Rectangles are given as fractions of the picture: `hmin`/`hmax` horizontally (0 = left edge,
1 = right edge) and `vmin`/`vmax` vertically (0 = top, 1 = bottom).

### LEDs around the TV

Use Hyperion's classic layout (**Configuration → LED Hardware → LED Layout** in Hyperion.ng): enter the number of LEDs
per side, the input position (where the strip starts) and the direction, then check with the
[test pattern](#checking-with-the-test-pattern).

### LEDs that do not run around the TV

If your LEDs are somewhere else (behind a sideboard, on a wall, only on some sides, in a shape), the classic
top/left/right/bottom generator is the wrong tool. Instead give each LED the screen region it should follow. In
Hyperion.ng, edit the generated layout JSON in the **LED Layout** tab; each entry is one LED, in strip order:

```json
[
  { "hmin": 0.00, "hmax": 0.10, "vmin": 0.00, "vmax": 0.15 },
  { "hmin": 0.10, "hmax": 0.20, "vmin": 0.00, "vmax": 0.15 },
  { "hmin": 0.90, "hmax": 1.00, "vmin": 0.40, "vmax": 0.60 }
]
```

The example: LED 1 follows the top-left area, LED 2 the area just right of it, LED 3 the middle of the right edge.
Tips:

- Think "which part of the picture should this LED glow like?", not "where is this LED physically?".
- Regions may overlap and do not have to touch the edges; a little depth (0.10 - 0.20) gives calmer colors.
- Write the list in strip order (LED 1 is the first LED after the controller).
- A short script or spreadsheet that generates the list is easier than editing 90 entries by hand.

### Checking with the test pattern

Click **Send test pattern** on the plugin page. For 8 seconds Hyperion receives a picture with a red top edge, green
right edge, blue bottom edge, yellow left edge and a white block running clockwise around the border, starting top-left.

| What you see | What to fix in Hyperion |
| --- | --- |
| Right colors, light runs clockwise | Nothing, the layout is correct |
| Light runs counter-clockwise | Reverse the LED direction |
| Colors rotated (for example red on the left) | Change the input position / first LED |
| Left and right swapped | The layout is mirrored: reverse direction and adjust the input position |
| Some LEDs never light up | LED count too low, or those LEDs' regions are outside the picture |
| Colors look wrong everywhere (for example red shows green) | Wrong color order (RGB/GRB) in the LED device settings |

## Devices: WLED and Philips Hue

Hyperion drives the devices, so they are configured only in Hyperion:

- **WLED**: add a *WLED* LED device in Hyperion (it can discover WLED on your network). Hyperion streams to WLED
  in real time; leave WLED's own effects off while Hyperion is active.
- **Philips Hue**: add a *Philips Hue* device using the Hue Entertainment API (you need an Entertainment area in the
  Hue app and to press the bridge button while pairing).

### Several devices at once

Hyperion drives one LED device per **instance**. For a WLED strip behind the TV and Hue lamps in the room, use two
instances: open **General** (HyperHDR: **General settings**), and under **LED Hardware Instance Management** click
**Create Instance**, then **Start**. The instance selector in the top bar chooses which instance the LED Hardware,
layout and color pages configure. Give the first instance the WLED device and the second one the Hue device.

There is nothing to set up in the plugin: **the picture reaches every running instance**, because Hyperion's
FlatBuffers server is shared by all instances.

- Each instance turns the picture into colors with its **own** LED layout, smoothing and color settings. For Hue,
  give each lamp the part of the picture it should follow (for example the left and right third), and more smoothing
  than the WLED strip: Hue lamps change color more slowly.
- An instance you start while a video plays picks the picture up straight away. A **stopped** instance gets nothing.
- The plugin appears in each instance's priority list (dashboard) as `Jellyfin@<address of the Jellyfin server>`;
  Hyperion.ng and HyperHDR may write an IPv4 address as `::ffff:192.168.1.10`. To keep one instance out of the movie,
  stop it, or give that instance a source with a lower priority number than the plugin's (for example a color set
  from the Hyperion app on that instance).
- Clearing the plugin's entry while a video plays (Hyperion.ng's **Remote Control** page, Home Assistant or
  `hyperion-remote`) blanks the LEDs for about a second only: the plugin registers again and the picture comes back.
  To turn the lights off for the rest of the movie, use one of the ways above.

This is tested automatically against Hyperion.ng and HyperHDR 22, each with two instances, on every change to the
plugin.

## Black bars (widescreen films)

Many films are wider than the TV: a 2.39:1 film comes with black bars at the top and bottom, a 4:3 show with bars at
the sides. Without help, the LEDs at those edges stay dark. Hyperion's **Blackbar detector** finds the bars and makes
the LED layout follow the picture inside them; it notices when the shape changes during a film (IMAX scenes) after
about two seconds.

=== "Hyperion.ng"

    On by default. To check: pick the instance in the top bar, open **Image Processing → Blackbar detector** and make
    sure **Enable** is ticked.

=== "HyperHDR"

    **Off by default.** Pick the instance in the top bar, open **Image Processing → Blackbar detector**, tick
    **Enable** and save. Do this for every instance (WLED, Hue, ...).

**Mode** *Default* works for most films. If grey-ish bars are not detected, raise the **Threshold** a little (for
example from 5 to 10 %).

The plugin's picture is a good input for the detector: it contains no subtitles or on-screen display, so nothing
appears in the bars unless subtitles are burned into the video. HDR films keep black bars black (see
[HDR and Dolby Vision](configuration.md#hdr-and-dolby-vision)). The plugin also sends anamorphic video (for example
16:9 DVDs) in the shape the TV shows. This is tested automatically against Hyperion.ng and HyperHDR with
letterboxed and pillarboxed pictures.

## Several TVs

The plugin sends one TV's picture to one Hyperion server. For LEDs on two TVs, run **one Hyperion.ng or HyperHDR server
per TV**, then add the second one on the plugin page under [More TVs](configuration.md#more-tvs):

- **A Raspberry Pi (or any device) per TV**, each running Hyperion with that TV's LEDs. Enter each one's address.
- **Two Hyperion containers on one machine**, for example on a NAS. Give the second container other ports: with
  bridge networking map its container ports to free host ports (for example `19401:19400`, `8091:8090` and
  `19445:19444`); with host networking change its ports under **Network Services** in its web UI. Enter the host with
  port `19401` in the plugin.

Two **instances** of one server cannot serve two TVs: every running instance shows the same picture (see
[Several devices at once](#several-devices-at-once)). Use instances for several LED devices on the same TV.

## Color and smoothing

Brightness, color calibration, gamma, smoothing and black bar detection are Hyperion settings and apply to the
plugin's picture exactly as they do to any other source. The plugin never changes them.
