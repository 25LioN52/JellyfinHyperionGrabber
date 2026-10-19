namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// The picture a session sends to Hyperion, for the status panel.
/// </summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="DynamicRange">How the video encodes brightness and color.</param>
/// <param name="ToneMapper">The filter that maps HDR to SDR; <see cref="HdrToneMapper.None"/> for SDR video, or when the
/// server's FFmpeg has none.</param>
public sealed record PictureFormat(int Width, int Height, VideoDynamicRange DynamicRange, HdrToneMapper ToneMapper)
{
    /// <summary>Describes the picture of a frame source.</summary>
    /// <param name="options">What the frame source decodes.</param>
    /// <returns>The picture.</returns>
    public static PictureFormat From(FfmpegFrameSourceOptions options)
    {
        System.ArgumentNullException.ThrowIfNull(options);
        return new PictureFormat(
            options.OutputWidth,
            options.OutputHeight,
            options.DynamicRange,
            options.IsToneMapped ? options.ToneMapper : HdrToneMapper.None);
    }
}
