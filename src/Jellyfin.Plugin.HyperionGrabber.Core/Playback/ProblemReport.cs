using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Something that kept, or keeps, the lights from following playback.
/// </summary>
/// <param name="Time">When it happened.</param>
/// <param name="Message">What happened, in words for the user.</param>
public sealed record ProblemReport(DateTimeOffset Time, string Message);
