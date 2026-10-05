using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Streaming;

/// <summary>
/// Where playback is streamed to and how (from the plugin configuration).
/// </summary>
public sealed record StreamingSettings
{
    /// <summary>Largest light timing offset in either direction.</summary>
    public static readonly TimeSpan MaxLatencyOffset = TimeSpan.FromSeconds(2);

    /// <summary>Default time a pause may last before the lights are released.</summary>
    public static readonly TimeSpan DefaultPauseRelease = TimeSpan.FromSeconds(15);

    /// <summary>Longest configurable pause before the lights are released.</summary>
    public static readonly TimeSpan MaxPauseRelease = TimeSpan.FromHours(1);

    /// <summary>Gets the Hyperion server, or <see langword="null"/> when none is configured.</summary>
    public HyperionClientOptions? Hyperion { get; init; }

    /// <summary>Gets the number of frames per second decoded and sent to Hyperion.</summary>
    public int FramesPerSecond { get; init; } = FfmpegFrameSourceOptions.DefaultFramesPerSecond;

    /// <summary>
    /// Gets how much earlier (positive) or later (negative) than the estimated playback position frames are shown,
    /// to cancel the delay between the TV picture and the LEDs.
    /// </summary>
    public TimeSpan LatencyOffset { get; init; }

    /// <summary>Gets how long a pause may last before FFmpeg and Hyperion are released; zero keeps holding the frame.</summary>
    public TimeSpan PauseRelease { get; init; } = DefaultPauseRelease;

    /// <summary>Returns human-readable problems with these settings; empty when they are valid.</summary>
    /// <returns>Validation errors.</returns>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (LatencyOffset.Duration() > MaxLatencyOffset)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"Light timing offset must be between -{MaxLatencyOffset.TotalMilliseconds:0} and {MaxLatencyOffset.TotalMilliseconds:0} ms."));
        }

        if (PauseRelease < TimeSpan.Zero || PauseRelease > MaxPauseRelease)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"Release after pause must be between 0 and {MaxPauseRelease.TotalSeconds:0} seconds."));
        }

        return errors;
    }
}
