using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

/// <summary>
/// Outcome of a connection test or test pattern run, phrased for an administrator.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Message">What happened, including how to fix a failure where possible.</param>
/// <param name="Elapsed">How long the operation took.</param>
/// <param name="FramesSent">Number of frames sent (test pattern only).</param>
public sealed record HyperionTestResult(bool Success, string Message, TimeSpan Elapsed, int FramesSent = 0);
