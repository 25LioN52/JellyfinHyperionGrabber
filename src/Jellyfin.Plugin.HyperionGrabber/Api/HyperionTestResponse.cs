using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

namespace Jellyfin.Plugin.HyperionGrabber.Api;

/// <summary>
/// Result shown on the configuration page.
/// </summary>
/// <param name="Success">Whether the action succeeded.</param>
/// <param name="Message">Human-readable outcome.</param>
/// <param name="ElapsedMilliseconds">Duration of the action.</param>
/// <param name="FramesSent">Frames sent by the test pattern.</param>
public sealed record HyperionTestResponse(bool Success, string Message, long ElapsedMilliseconds, int FramesSent)
{
    /// <summary>Creates a response from a diagnostics result.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The response.</returns>
    public static HyperionTestResponse From(HyperionTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(result.Success, result.Message, (long)result.Elapsed.TotalMilliseconds, result.FramesSent);
    }

    /// <summary>Creates a failed response for invalid input.</summary>
    /// <param name="message">What is wrong.</param>
    /// <returns>The response.</returns>
    public static HyperionTestResponse Invalid(string message) => new(false, message, 0, 0);
}
