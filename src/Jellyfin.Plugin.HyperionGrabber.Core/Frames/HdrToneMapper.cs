namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// The FFmpeg filter that maps HDR video to SDR for the lights.
/// </summary>
public enum HdrToneMapper
{
    /// <summary>The server's FFmpeg has no tone mapping filter: HDR video is sent as decoded, with pale colors.</summary>
    None = 0,

    /// <summary>Jellyfin's <c>tonemapx</c> (jellyfin-ffmpeg), what Jellyfin's own software tone mapping uses: BT.2390, and
    /// it applies Dolby Vision metadata, so profile 5 gets its colors.</summary>
    Tonemapx,

    /// <summary><c>zscale</c> (zimg) with FFmpeg's <c>tonemap</c> filter (Möbius): HDR10 and HLG; Dolby Vision profile 5
    /// keeps wrong colors.</summary>
    Zscale,
}
