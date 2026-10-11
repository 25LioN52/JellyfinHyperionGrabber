using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// A snapshot of what a <see cref="IGrabSession"/> is doing, for the status panel.
/// </summary>
public sealed record GrabSessionStatus
{
    /// <summary>Gets what the session is doing.</summary>
    public required GrabSessionPhase Phase { get; init; }

    /// <summary>Gets what keeps the lights from following playback right now, if anything.</summary>
    public string? Problem { get; init; }

    /// <summary>Gets the most recent problem of this session, also after it was resolved.</summary>
    public ProblemReport? LastProblem { get; init; }

    /// <summary>Gets the estimated playback position (updated about once a second).</summary>
    public TimeSpan Position { get; init; }

    /// <summary>Gets how far <see cref="Position"/> may be off, from the precision of the client's reports.</summary>
    public TimeSpan PositionUncertainty { get; init; }

    /// <summary>Gets the pictures sent to Hyperion per second, over the last second.</summary>
    public double FramesPerSecond { get; init; }

    /// <summary>Gets the configured frame rate.</summary>
    public int TargetFramesPerSecond { get; init; }

    /// <summary>Gets the number of pictures sent to Hyperion.</summary>
    public long FramesSent { get; init; }

    /// <summary>Gets the number of decoded pictures that were not sent (late, or Hyperion unreachable).</summary>
    public long FramesDropped { get; init; }

    /// <summary>Gets the number of times decoding restarted to follow seeks or a lagging decoder.</summary>
    public long DecoderRestarts { get; init; }

    /// <summary>Gets the decoder in use, or <see langword="null"/> while none runs.</summary>
    public HardwareAcceleration? Decoder { get; init; }

    /// <summary>Gets a value indicating whether hardware decoding failed and the video is decoded on the CPU.</summary>
    public bool IsHardwareFallback { get; init; }
}
