# 0013: Send the display aspect ratio; leave black bars to Hyperion's detector

**Status:** Accepted (2026-10-19)

## Context

Two kinds of video do not fill the picture the way the TV shows it:

- **Anamorphic** video has non-square pixels: a 16:9 DVD is stored at 720×576, HDV at 1440×1080. Up to 0.7 the plugin
  scaled by the stored size and sent 160×128 or 160×120 instead of 160×90. The code already said "display size", but
  the resolver passed the stored size.
- **Letterboxed** video carries black bars in the picture: a 2.39:1 film stored at 16:9 has bars at the top and bottom,
  a 4:3 programme in a 16:9 file has them at the sides. The LEDs that sample those edges show black.

For black bars, options considered:

- **Crop in the plugin** with a fixed crop from FFmpeg's `cropdetect` at the start of a playback: wrong for films whose
  aspect ratio changes (IMAX scenes), wrong after a seek into a different part, and it adds an analysis pass per start.
- **Detect per frame in the plugin** (C# on the small frame) and send the inner picture: per-frame analysis on the
  Jellyfin server, a frame size that changes during playback, and a copy of logic Hyperion already has.
- **Leave it to Hyperion.** Hyperion.ng and HyperHDR both have a black bar detector with the same settings and modes
  (`blackborderdetector`: threshold, consistency counters, modes default/classic/osd/letterbox). It runs on every
  image source, FlatBuffers included, maps the LED layout to the picture inside the bars, and follows changes during a
  film. Hyperion.ng turns it on by default, HyperHDR turns it off.

## Decision

- **Anamorphic:** `JellyfinVideoInputResolver` widens the stored size to the display aspect ratio Jellyfin keeps as
  text (`MediaStream.AspectRatio`, for example `16:9` or `2.40:1`) when `MediaStream.IsAnamorphic` is true, or unknown
  and the shape differs by more than 5 % (Jellyfin rounds the text to a common name, up to 3.75 % off). Unparsable or
  implausible ratios (outside 1:5 to 5:1) keep the stored size. FFmpeg's scalers then get an explicit output size, so
  the frame has the display shape whatever the sample aspect ratio.
- **Black bars:** the plugin does not crop. It sends the whole picture with real black bars: no subtitles or on-screen
  display are decoded, and HDR tone mapping keeps black at 0 ([ADR 0012](0012-hdr-tone-mapping.md)). The docs tell
  HyperHDR users to turn the detector on.
- The end-to-end tests turn the detector on with Hyperion.ng's defaults on both servers and check that a letterboxed
  and a pillarboxed picture light the edge LEDs with the picture's colors.

## Consequences

- Anamorphic DVDs, HDV and anamorphic scope encodes reach Hyperion in the shape the TV shows. Video with square pixels is
  unchanged.
- Black bar handling has one place, configured where the rest of the picture processing is, and works the same for every
  source of an instance. Its quality is Hyperion's: changes are picked up after its consistency count (50 frames, 2 s
  at 25 fps, by default).
- The plugin's picture is a better input for the detector than an HDMI capture: subtitles never sit in the bars unless
  they are burned into the video.
- HyperHDR users must turn the detector on; the Hyperion setup page and the troubleshooting page say how.
