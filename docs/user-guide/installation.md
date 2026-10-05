# Installation

## From the plugin repository (recommended)

In Jellyfin open **Dashboard → Plugins**, go to the repository settings (the *Repositories* tab, or the ⚙ button on
the *Catalog* page, depending on your version) and add this URL:

```text
https://25lion52.github.io/JellyfinHyperionGrabber/manifest.json
```

Then install **Hyperion Grabber** from the catalog and restart Jellyfin. Updates appear in the catalog like any
other plugin; enable *automatic updates* on the plugin page if you want them installed for you.

### Which build do I get?

There is one repository URL for all supported Jellyfin versions. Each release contains one build per Jellyfin line,
and Jellyfin installs the newest build it supports:

| Jellyfin server | Build | Plugin version looks like |
| --- | --- | --- |
| 10.11.x | .NET 9 | `0.2.0.0` |
| 12.x | .NET 10 | `0.2.0.1` |

The last number only tells the builds apart. When you upgrade Jellyfin from 10.11 to 12, the plugin page offers the
12 build as an update.

## Manual installation

1. Download the zip for your Jellyfin line from the
   [releases page](https://github.com/25LioN52/JellyfinHyperionGrabber/releases):
   `hyperion-grabber_<version>_jellyfin-10.11.zip` or `..._jellyfin-12.zip`.
2. Stop Jellyfin.
3. Create a folder `Hyperion Grabber_<version>` inside Jellyfin's `plugins` folder and extract the zip into it:
    - Docker: `/config/plugins/`
    - Debian/Ubuntu packages: `/var/lib/jellyfin/plugins/`
    - Windows: `%LOCALAPPDATA%\jellyfin\plugins\` (or `%PROGRAMDATA%\Jellyfin\Server\plugins\`)
4. Start Jellyfin and check **Dashboard → Plugins**: the plugin should be *Active*.

## Docker notes

- The plugin connects from inside the Jellyfin container to Hyperion. If Hyperion runs **on the same host**:
    - with `network_mode: host`, use `localhost` or the host's LAN IP;
    - with the default bridge network, use the host's LAN IP (for example `192.168.1.20`), or
      `host.docker.internal` after adding `extra_hosts: ["host.docker.internal:host-gateway"]` to the Jellyfin service.
- If Hyperion runs in another container on the same Docker network, use that container's service name.
- *(planned)* Streaming during playback will use Jellyfin's own FFmpeg and hardware acceleration settings
  (**Dashboard → Playback → Transcoding**), including the list of codecs enabled for hardware decoding; other codecs,
  and any hardware failure, fall back to decoding on the CPU. For Intel QuickSync in Docker, pass `/dev/dri` to the
  container as described in
  [Jellyfin's hardware acceleration guide](https://jellyfin.org/docs/general/post-install/transcoding/hardware-acceleration/).

## Uninstall

**Dashboard → Plugins → Hyperion Grabber → Uninstall**, then restart Jellyfin. The plugin clears its Hyperion priority
when it stops, so your LEDs return to whatever Hyperion shows otherwise.
