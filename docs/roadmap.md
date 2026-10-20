# Roadmap

Milestones are deliberately small so each one ships something usable. Items become GitHub issues when work starts.

## M0: Foundation *(done; first release will be v0.1.0)*

- [x] Hyperion.ng / HyperHDR FlatBuffers client: register, images, clear, reply draining, timeouts
- [x] Encoder verified against the official FlatBuffers runtime and verifier; golden bytes; fuzzed reply parser
- [x] LED layout test pattern and **Test connection** / **Send test pattern** on the configuration page
- [x] One codebase, two builds: Jellyfin 10.11 (.NET 9) and Jellyfin 12 (.NET 10), one repository URL
- [x] CI (Linux + Windows), CodeQL, release automation, plugin repository on GitHub Pages
- [x] Documentation site, contributor and agent guides, Claude Code review skill (`/review-pr`)

## M1: Playback to LEDs

- [x] Playback monitor: react to playback start/stop on selected devices (device and user filter on the config page)
- [x] Frame source: Jellyfin's FFmpeg decodes the playing media source from its position, scaled on the GPU to
      about 160 px wide, keeping the aspect ratio; uses Jellyfin's hardware acceleration settings, CPU fallback
- [x] Stream to Hyperion at a configurable frame rate (default 25), bounded buffers, frame dropping under load
- [x] Reconnect with backoff when Hyperion restarts; release the priority on stop *(shipped in 0.5)*
- [x] End-to-end test against a real Hyperion.ng and HyperHDR in CI (containers); verify the multi-instance
      (WLED + Hue) behaviour *(done: the picture reaches every running instance, see
      [Several devices at once](user-guide/hyperion-setup.md#several-devices-at-once))*
- [x] Diagnostics: status panel on the config page (connected, fps, current session, last problem) *(shipped in 0.6)*

## M2: Sync

- [x] Pause holds the last frame (keep-alive so Hyperion does not drop the connection); resume continues *(shipped
      with M1 streaming)*
- [x] Seek detection and restart at the new position *(shipped with M1 streaming)*
- [x] Position tracking that knows each client's report precision (Kodi's whole seconds), tolerates clock drift and
      gets more precise with every report *(shipped in 0.4)*
- [x] Release the lights after a long pause (default 15 s), resume at the playback position *(shipped in 0.3)*
- [x] Light timing offset (ms), one value for all devices *(shipped in 0.3)*
- [ ] Per-device timing offset, with a calibration video and guide
- [x] Precise clock for Kodi clients via Kodi's JSON-RPC (position and events in real time) *(shipped in 0.7)*
- [ ] Playback speed changes

## M3: Picture quality

- [x] HDR10 / HLG tone mapping to SDR; Dolby Vision (including profile 5) color handling
      ([ADR 0012](development/adr/0012-hdr-tone-mapping.md))
- [x] Anamorphic and letterboxed sources: the display aspect ratio is sent; black bars are left to Hyperion's
      detector ([ADR 0013](development/adr/0013-black-bars-and-aspect-ratio.md))
- [x] Multiple targets: map devices to different Hyperion servers (one per TV). Instances of one server all get the
      same picture, so they cannot serve two TVs ([ADR 0014](development/adr/0014-multiple-targets.md))

## M4: Polish

- [ ] Auto-discovery of Hyperion.ng / HyperHDR on the network (mDNS/SSDP)
- [ ] Live LED preview on the config page
- [ ] Per-library / per-item opt-out, intros and trailers handling
- [ ] Localization of the config page

## Not planned (for now)

- Driving WLED or Hue directly without Hyperion: Hyperion does this better and keeps the layout flexible.
- Live TV: re-decoding would open a second tuner stream. May be revisited.
