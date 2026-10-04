# Security policy

## Supported versions

Only the latest release receives security fixes. The plugin repository always offers the latest build for each
supported Jellyfin line (currently 10.11.x and 12.x).

## Reporting a vulnerability

Please **do not open a public issue**. Report privately through
[GitHub security advisories](https://github.com/25LioN52/JellyfinHyperionGrabber/security/advisories/new).
Include the plugin and Jellyfin versions, what an attacker can do, and steps to reproduce.

You can expect a first response within 7 days. Confirmed issues are fixed in a new release and disclosed in a
published advisory once users have had a chance to update.

## Security model

Things to know when you deploy the plugin:

- **The plugin runs inside the Jellyfin server process** with the server's permissions. Only install releases from
  this repository's plugin manifest or GitHub releases.
- **Its HTTP endpoints are for administrators only** (`RequiresElevation` policy). They let an administrator make the
  server open TCP connections to a host and port of their choice, which is needed to test the Hyperion connection.
- **The Hyperion FlatBuffers protocol has no authentication or encryption.** Anyone who can reach the FlatBuffers port
  can control your LEDs, and video frames (tiny, low-resolution thumbnails) travel unencrypted. Keep Hyperion's
  FlatBuffers port on your local network and do not expose it to the internet.
- The plugin sends nothing anywhere except the Hyperion server you configure.
