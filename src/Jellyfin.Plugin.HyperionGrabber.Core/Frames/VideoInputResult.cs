using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// Outcome of <see cref="IVideoInputResolver.Resolve"/>: a video to decode, or the reason there is none.
/// </summary>
public sealed record VideoInputResult
{
    private VideoInputResult(VideoInput? input, string? reason)
    {
        Input = input;
        Reason = reason;
    }

    /// <summary>Gets the video to decode, when supported.</summary>
    public VideoInput? Input { get; }

    /// <summary>Gets why the item cannot drive the lights, when not supported (for the log).</summary>
    public string? Reason { get; }

    /// <summary>Gets a value indicating whether <see cref="Input"/> is set.</summary>
    [MemberNotNullWhen(true, nameof(Input))]
    [MemberNotNullWhen(false, nameof(Reason))]
    public bool IsSupported => Input is not null;

    /// <summary>Creates a result for a video that can be decoded.</summary>
    /// <param name="input">The video.</param>
    /// <returns>The result.</returns>
    public static VideoInputResult Supported(VideoInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new VideoInputResult(input, null);
    }

    /// <summary>Creates a result for an item that cannot drive the lights.</summary>
    /// <param name="reason">Why, completing "The lights do not follow this playback because ...".</param>
    /// <returns>The result.</returns>
    public static VideoInputResult Unsupported(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new VideoInputResult(null, reason);
    }
}
