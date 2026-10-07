# Compatibility

## Jellyfin server

| Jellyfin | Supported | Build | Notes |
| --- | --- | --- | --- |
| 12.x | Yes | .NET 10 (plugin version `x.y.z.1`) | Built against 12.0.0, `targetAbi` 12.0.0.0 |
| 10.11.x | Yes | .NET 9 (plugin version `x.y.z.0`) | Built against 10.11.0, `targetAbi` 10.11.0.0 |
| 10.10 and older | No | | Upgrade Jellyfin |

Both builds come from the same source code and the same release, served from one repository URL.

## Lighting server

| Server | Supported | Notes |
| --- | --- | --- |
| Hyperion.ng 2.x | Yes | FlatBuffers server (default port 19400). Priorities 100-199. |
| HyperHDR | Yes | Same FlatBuffers protocol and port. |
| Hyperion "classic" (1.x) | No | Unmaintained; use Hyperion.ng. |

The plugin only uses the parts of the FlatBuffers protocol that both servers implement identically (Register,
RawImage RGB, Clear).

## Jellyfin clients

Any client that reports playback to the server works. Sync precision depends on how often and how precisely it
reports:

| Client | Position reports | Expected sync |
| --- | --- | --- |
| Jellyfin for Kodi (incl. LibreELEC) | Whole seconds; on pause/resume/seek, otherwise about every 4 minutes | Within about half a second after start, more precise with every pause, resume and report; set the light timing offset. Precise Kodi sync is planned |
| Jellyfin Web, Jellyfin Media Player, LG webOS, Samsung Tizen | Milliseconds, every 10 s and on pause/seek | Good |
| Android TV | Milliseconds, every 3 s and on pause/seek | Good |
| Android (phone, tablet) | Milliseconds, every 10 s | Good |
| Swiftfin (iOS, tvOS) | Every 5 s and on pause/seek | Good (expected) |
| JellyCon, Infuse, Findroid, Roku, others | Not checked | Please report your results |

## LED devices

Whatever your Hyperion.ng/HyperHDR supports: WLED, Philips Hue (Entertainment API), Adalight/HyperSerial,
Nanoleaf, and more. They are configured in Hyperion only.

## Reference setup

The maintainer's test setup, useful if you have similar hardware:

| Part | Model |
| --- | --- |
| Jellyfin server | UGREEN NASync DXP4800 Plus: Intel Pentium Gold 8505 (Alder Lake, 5 cores), Intel UHD Graphics (Xe-LP, 48 EU) with QuickSync (H.264, HEVC 10-bit, VP9, AV1 decode); Jellyfin in Docker |
| Lighting server | Hyperion.ng on the same NAS |
| LEDs | WLED, 90 LEDs; Philips Hue |
| Client | Kodi with Jellyfin add-on on LibreELEC, Raspberry Pi |

Tested your setup? Tell us in [Discussions](https://github.com/25LioN52/JellyfinHyperionGrabber/discussions) and we
will add it here.
