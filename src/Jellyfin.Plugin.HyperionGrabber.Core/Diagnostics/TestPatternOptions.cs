using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

/// <summary>
/// How the test pattern is played.
/// </summary>
public sealed record TestPatternOptions
{
    /// <summary>Longest allowed test run.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60);

    /// <summary>Gets the frame width in pixels.</summary>
    public int Width { get; init; } = 64;

    /// <summary>Gets the frame height in pixels.</summary>
    public int Height { get; init; } = 36;

    /// <summary>Gets the frame rate.</summary>
    public int FramesPerSecond { get; init; } = 25;

    /// <summary>Gets how long the pattern plays before the priority is cleared.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Gets how long the marker takes for one lap around the border.</summary>
    public TimeSpan LapDuration { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Returns human-readable problems with these options; empty when they are valid.
    /// </summary>
    /// <returns>Validation errors.</returns>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (Width is < 2 or > 320 || Height is < 2 or > 320)
        {
            errors.Add("Test pattern size must be between 2x2 and 320x320 pixels.");
        }

        if (FramesPerSecond is < 1 or > 60)
        {
            errors.Add("Test pattern frame rate must be between 1 and 60.");
        }

        if (Duration <= TimeSpan.Zero || Duration > MaxDuration)
        {
            errors.Add("Test pattern duration must be between 1 and 60 seconds.");
        }

        if (LapDuration <= TimeSpan.Zero)
        {
            errors.Add("Test pattern lap duration must be positive.");
        }

        return errors;
    }
}
