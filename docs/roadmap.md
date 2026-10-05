# Roadmap

Milestones are deliberately small so each one ships something usable. Items become GitHub issues when work starts.

## M0: Foundation *(done; first release will be v0.1.0)*

- [x] Hyperion.ng / HyperHDR FlatBuffers client: register, images, clear, reply draining, timeouts
- [x] Encoder verified against the official FlatBuffers runtime and verifier; golden bytes; fuzzed reply parser
- [x] LED layout test pattern and **Test connection** / **Send test pattern** on the configuration page
- [x] One codebase, two builds: Jellyfin 10.11 (.NET 9) and Jellyfin 12 (.NET 10), one repository URL
- [x] CI (Linux + Windows), CodeQL, release automation, plugin repository on GitHub Pages
- [x] Documentation site, contributor and agent guides, Claude Code review on every PR

## M1: Playback to LEDs

- [ ] Playback monitor: react to playback start/stop on selected devices (device and user filter on the config page)
- [x] Frame source: Jellyfin's FFmpeg decodes the playing media source from its position, scaled on the GPU to
      about 160 px wide, keeping the aspect ratio; uses Jellyfin's hardware acceleration settings, CPU fallback
- [ ] Stream to Hyperion at a configurable frame rate (default 25), bounded buffers, frame dropping under load
- [ ] Reconnect with backoff when Hyperion restarts; release the priority on stop
- [ ] End-to-end test against a real Hyperion.ng in CI (container); verify the multi-instance (WLED + Hue) behaviour
- [ ] Diagnostics: status panel on the config page (connected, fps, current session)

## M2: Sync

- [ ] Pause holds the last frame (keep-alive so Hyperion does not drop the connection); resume continues
- [ ] Seek detection and restart at the new position; drift correction between position reports
- [ ] Per-device latency offset (ms), with a calibration video and guide
- [ ] Optional precise clock for Kodi clients via Kodi's JSON-RPC (position and events in real time)
- [ ] Playback speed changes

## M3: Picture quality

- [ ] HDR10 / HLG tone mapping to SDR; Dolby Vision (including profile 5) color handling
- [ ] Anamorphic and letterboxed sources
- [ ] Multiple targets: map devices to different Hyperion servers or instances (one per TV)

## M4: Polish

- [ ] Auto-discovery of Hyperion.ng / HyperHDR on the network (mDNS/SSDP)
- [ ] Live LED preview on the config page
- [ ] Per-library / per-item opt-out, intros and trailers handling
- [ ] Localization of the config page

## Not planned (for now)

- Driving WLED or Hue directly without Hyperion: Hyperion does this better and keeps the layout flexible.
- Live TV: re-decoding would open a second tuner stream. May be revisited.
