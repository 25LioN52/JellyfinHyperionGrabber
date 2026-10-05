using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// The server's FFmpeg installation and hardware acceleration settings (Jellyfin's, provided by the plugin).
/// </summary>
public sealed record FfmpegSettings
{
    /// <summary>Gets the path of the FFmpeg executable; empty when the server has none configured.</summary>
    public required string FfmpegPath { get; init; }

    /// <summary>Gets the hardware acceleration selected by the administrator.</summary>
    public HardwareAcceleration HardwareAcceleration { get; init; }

    /// <summary>Gets the DRM render node for VA-API / QuickSync on Linux, if configured.</summary>
    public string? HardwareDevice { get; init; }

    /// <summary>
    /// Gets the codecs the administrator enabled for hardware decoding (FFmpeg/Jellyfin codec names such as
    /// <c>h264</c> or <c>hevc</c>).
    /// </summary>
    public IReadOnlyCollection<string> HardwareDecodingCodecs { get; init; } = [];

    /// <summary>
    /// Returns the acceleration to use for a video codec: hardware only when the administrator enabled hardware
    /// decoding for that codec, like Jellyfin's own transcoder. An unknown codec gets hardware acceleration; the
    /// frame source falls back to the CPU if it fails.
    /// </summary>
    /// <param name="codec">The video codec, or <see langword="null"/> when unknown.</param>
    /// <returns>The acceleration to use.</returns>
    public HardwareAcceleration GetAcceleration(string? codec)
    {
        if (HardwareAcceleration == HardwareAcceleration.None || string.IsNullOrEmpty(codec))
        {
            return HardwareAcceleration;
        }

        foreach (var enabled in HardwareDecodingCodecs)
        {
            if (string.Equals(enabled, codec, StringComparison.OrdinalIgnoreCase))
            {
                return HardwareAcceleration;
            }
        }

        return HardwareAcceleration.None;
    }
}
