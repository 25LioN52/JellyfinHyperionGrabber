# FAQ

**Does it capture my TV screen?**
No. The Jellyfin server decodes the same video file a second time, at a tiny resolution, and keeps it in step with
your client. Nothing is captured, so no capture card or TV app is needed. See [How it works](../how-it-works.md).

**Which clients work?**
Any client that reports playback to the Jellyfin server: Kodi with the Jellyfin add-on (including LibreELEC on a
Raspberry Pi), the Android TV, webOS, Tizen, iOS and web clients. How precisely the lights follow seeking and pausing
depends on how often the client reports its position; see [Compatibility](../compatibility.md).

**Do I need Hyperion? Can it talk to WLED directly?**
You need Hyperion.ng or HyperHDR. They do the LED mapping, color calibration and smoothing, and support far more
devices than this plugin could. Doing it in Hyperion also means your LED layout can be anything.

**Hyperion.ng or HyperHDR?**
Both work the same way with this plugin. HyperHDR focuses on HDR capture and has some advanced smoothing options;
Hyperion.ng has a larger device list. Use the one you already have.

**Will it slow down my server?**
The plugin decodes at a very small output size and uses Jellyfin's hardware acceleration (for example Intel
QuickSync). On a modern Intel NAS CPU a 4K stream with hardware decoding is light. Without hardware decoding, 4K HEVC
can be heavy on low-power CPUs: lower the **Frame rate** on the plugin page. When decoding cannot keep up, the plugin
skips pictures rather than falling behind.

**Does it work with HDR and Dolby Vision?**
That is a planned headline feature: HDR10 and Dolby Vision are tone-mapped to SDR colors before sending, so the LEDs
are not washed out.

**What about subtitles, menus and the on-screen display?**
They are not part of the decoded video, so the lights follow the film only, which is usually what you want.

**What happens when I pause?**
The LEDs hold the last frame. If the pause lasts longer than 15 seconds (configurable, 0 = always hold), the plugin
releases its Hyperion priority and Hyperion shows its default until you resume; the lights pick up the picture again
on resume. When playback stops, the priority is released at once.

**The lights are slightly late. Can I fix that?**
Yes: set **Light timing offset (ms)** on the plugin page. See [Lights are late or early](troubleshooting.md#the-lights-are-late-or-early).

**Is anything sent to the internet?**
No. The plugin only connects to the Hyperion server you configure.
