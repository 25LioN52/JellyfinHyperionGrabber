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

    /// <summary>Gets a value indicating whether the server's FFmpeg has Jellyfin's <c>tonemapx</c> filter.</summary>
    public bool HasTonemapx { get; init; }

    /// <summary>Gets a value indicating whether the server's FFmpeg has the <c>zscale</c> (zimg) filter.</summary>
    public bool HasZscale { get; init; }

    /// <summary>
    /// Returns the filter that maps a video to SDR. HDR10 and HLG prefer <c>zscale</c>: <c>tonemapx</c> darkens the
    /// picture and turns saturated reds toward magenta (it drops their green). Dolby Vision profile 5 prefers
    /// <c>tonemapx</c>, the only one that applies its metadata.
    /// </summary>
    /// <param name="range">The video's dynamic range.</param>
    /// <returns>The filter; <see cref="HdrToneMapper.None"/> for SDR, or when FFmpeg has neither.</returns>
    public HdrToneMapper GetToneMapper(VideoDynamicRange range)
    {
        if (range == VideoDynamicRange.Sdr)
        {
            return HdrToneMapper.None;
        }

        var (first, firstAvailable, second, secondAvailable) = range == VideoDynamicRange.DolbyVision
            ? (HdrToneMapper.Tonemapx, HasTonemapx, HdrToneMapper.Zscale, HasZscale)
            : (HdrToneMapper.Zscale, HasZscale, HdrToneMapper.Tonemapx, HasTonemapx);
        return firstAvailable ? first : secondAvailable ? second : HdrToneMapper.None;
    }

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
