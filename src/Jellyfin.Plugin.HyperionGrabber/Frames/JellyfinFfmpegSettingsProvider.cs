using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.HyperionGrabber.Frames;

/// <summary>
/// Reads Jellyfin's FFmpeg path and hardware acceleration settings (Dashboard → Playback → Transcoding).
/// </summary>
public class JellyfinFfmpegSettingsProvider : IFfmpegSettingsProvider
{
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _configurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinFfmpegSettingsProvider"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Jellyfin's media encoder (knows the FFmpeg path in use).</param>
    /// <param name="configurationManager">Server configuration (encoding options).</param>
    public JellyfinFfmpegSettingsProvider(IMediaEncoder mediaEncoder, IServerConfigurationManager configurationManager)
    {
        _mediaEncoder = mediaEncoder;
        _configurationManager = configurationManager;
    }

    /// <inheritdoc />
    public FfmpegSettings GetSettings()
    {
        var encoding = _configurationManager.GetEncodingOptions();
        var acceleration = Map(encoding.HardwareAccelerationType);
        return new FfmpegSettings
        {
            FfmpegPath = _mediaEncoder.EncoderPath ?? string.Empty,
            HardwareAcceleration = acceleration,

            // Jellyfin uses its VA-API device for VA-API, and for QSV and AMF on Linux.
            HardwareDevice = acceleration is HardwareAcceleration.Vaapi or HardwareAcceleration.Qsv or HardwareAcceleration.Amf
                ? NullIfEmpty(encoding.VaapiDevice)
                : null,
            HardwareDecodingCodecs = encoding.HardwareDecodingCodecs ?? [],
        };
    }

    /// <summary>
    /// Maps Jellyfin's hardware acceleration type; types we do not support decode on the CPU.
    /// </summary>
    /// <param name="type">Jellyfin's setting.</param>
    /// <returns>The Core equivalent.</returns>
    internal static HardwareAcceleration Map(HardwareAccelerationType type) => type switch
    {
        HardwareAccelerationType.amf => HardwareAcceleration.Amf,
        HardwareAccelerationType.qsv => HardwareAcceleration.Qsv,
        HardwareAccelerationType.nvenc => HardwareAcceleration.Nvenc,
        HardwareAccelerationType.vaapi => HardwareAcceleration.Vaapi,
        HardwareAccelerationType.videotoolbox => HardwareAcceleration.VideoToolbox,
        HardwareAccelerationType.rkmpp => HardwareAcceleration.Rkmpp,

        // v4l2m2m has no scaler that works for our pipeline; none is software.
        _ => HardwareAcceleration.None,
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
