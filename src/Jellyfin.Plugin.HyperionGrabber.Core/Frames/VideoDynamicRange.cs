namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// How a video encodes brightness and color, as far as mapping it to the SDR picture Hyperion expects needs to know.
/// </summary>
public enum VideoDynamicRange
{
    /// <summary>Standard dynamic range (BT.709 or BT.601): sent as decoded.</summary>
    Sdr = 0,

    /// <summary>HDR with the PQ transfer (SMPTE ST 2084) and BT.2020 colors: HDR10, HDR10+ and Dolby Vision with an
    /// HDR10 base layer (profiles 7 and 8.1).</summary>
    Hdr10,

    /// <summary>HDR with the HLG transfer (ARIB STD-B67) and BT.2020 colors, also Dolby Vision with an HLG base layer
    /// (profile 8.4).</summary>
    Hlg,

    /// <summary>Dolby Vision without a compatible base layer (profile 5): its colors are only right after applying the
    /// Dolby Vision metadata (RPU).</summary>
    DolbyVision,
}
