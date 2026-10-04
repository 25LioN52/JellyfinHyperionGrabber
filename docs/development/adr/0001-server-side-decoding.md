# 0001: Decode on the server instead of capturing

**Status:** Accepted (2026-10-04)

## Context

Ambient lighting needs the picture that is on the TV. The usual options are an HDMI capture device or a screen
grabber on the playback device. Many Jellyfin setups cannot use either: on a Raspberry Pi with LibreELEC/Kodi, for
example, hardware-decoded video is shown on a display plane that screen grabbers cannot read, and HDR/DRM content is
often blocked or washed out. Capture hardware costs money and adds latency.

An existing plugin ([jellyfin-ambilight](https://github.com/gabrielprat/jellyfin-ambilight)) pre-processes every file
into color data and drives WLED directly with its own edge-based LED model. That needs a long extraction pass per
file and cannot represent LED placements that do not follow the screen edges.

## Decision

The plugin decodes the playing media source a second time on the Jellyfin server, at a very small output size, keeps
it in sync with the client using Jellyfin's playback reports, and streams the frames to Hyperion.ng/HyperHDR, which
keeps full control of LED mapping and devices.

## Consequences

- Works with every client, needs no extra hardware, and sees the original HDR/DV signal (tone mapping becomes our job).
- Frames are available ahead of display, so LED latency can be compensated.
- Sync accuracy depends on the client's playback reports; we need an extrapolating sync engine, a latency offset,
  and optionally precise client-specific clocks (Kodi JSON-RPC).
- Adds decode load on the server: hardware acceleration, GPU scaling and frame dropping are required, not optional.
- Menus, subtitles and the OSD never reach the LEDs.
- Live TV is out of scope for now (a second decode would need a second tuner).
