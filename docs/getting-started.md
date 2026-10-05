# Getting started

Five minutes from zero to a working LED test. You need a running Jellyfin server (10.11.x or 12.x) and a running
Hyperion.ng or HyperHDR with your LEDs already working (for example with one of Hyperion's own effects).

## 1. Install the plugin

1. In Jellyfin, open **Dashboard → Plugins** and go to the repository settings (the *Repositories* tab, or the
   ⚙ button on the *Catalog* page, depending on your Jellyfin version). Add a repository:
    - **Name:** `Hyperion Grabber`
    - **URL:** `https://25lion52.github.io/JellyfinHyperionGrabber/manifest.json`
2. Open the **Catalog**, find **Hyperion Grabber** and click **Install**.
3. Restart Jellyfin when asked.

Jellyfin automatically picks the build for your server version. Details and manual installation:
[Installation](user-guide/installation.md).

## 2. Connect to Hyperion

1. Open **Dashboard → Plugins → Hyperion Grabber**.
2. Enter the **host** of the machine running Hyperion.ng/HyperHDR, for example `192.168.1.20`.
3. Keep **port** `19400` and **priority** `150` unless you changed them in Hyperion.
4. Click **Test connection**. You should see *"Connected to … and registered priority 150"*.
5. Click **Save**.

If the test fails, the message tells you why. The most common causes are the wrong port and Docker networking, see
[Troubleshooting](user-guide/troubleshooting.md).

## 3. Check your LED layout

Click **Send test pattern** and look at your TV. For 8 seconds you should see:

| Where | Color |
| --- | --- |
| Top edge | **Red** |
| Right edge | **Green** |
| Bottom edge | **Blue** |
| Left edge | **Yellow** |
| Moving | A **white light** running **clockwise**, starting at the top-left corner |

If the colors are on the wrong sides, or the light runs counter-clockwise, fix the LED layout in Hyperion:
[Hyperion.ng & HyperHDR setup](user-guide/hyperion-setup.md#led-layout).

## 4. Watch something

Open the **Playback** section, turn on **Follow playback**, select your TV and press **Save** (see
[Configuration](user-guide/configuration.md#playback)). Play a video from your library on the TV: the LEDs follow the
picture, hold it while paused and go back to Hyperion's own sources when you stop. If they do not, see
[Troubleshooting](user-guide/troubleshooting.md#the-lights-do-not-follow-a-video).
Follow the [roadmap](roadmap.md) or watch the [GitHub repository](https://github.com/25LioN52/JellyfinHyperionGrabber)
for releases.
