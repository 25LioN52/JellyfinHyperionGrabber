using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Streaming;

/// <summary>
/// Where playback is streamed to and how fast (from the plugin configuration).
/// </summary>
public sealed record StreamingSettings
{
    /// <summary>Gets the Hyperion server, or <see langword="null"/> when none is configured.</summary>
    public HyperionClientOptions? Hyperion { get; init; }

    /// <summary>Gets the number of frames per second decoded and sent to Hyperion.</summary>
    public int FramesPerSecond { get; init; } = FfmpegFrameSourceOptions.DefaultFramesPerSecond;
}
