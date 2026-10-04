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
- **Several devices at once** (for example a WLED strip behind the TV and Hue lamps in the room): Hyperion.ng and
  HyperHDR support multiple **instances**, one per device. How a FlatBuffers stream reaches each instance depends on
  your server version and configuration; we are verifying the exact steps for WLED + Hue and will document them here
  *(open question, see the [roadmap](../roadmap.md))*.

## Color and smoothing

Brightness, color calibration, gamma, smoothing and black-border detection are Hyperion settings and apply to the
plugin's picture exactly as they do to any other source. The plugin never changes them.
