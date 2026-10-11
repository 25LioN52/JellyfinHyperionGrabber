using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// The playback position read from the player itself (for example Kodi's JSON-RPC), more precise than its reports to
/// the media server.
/// </summary>
/// <param name="Position">The position the player showed.</param>
/// <param name="IsPaused">Whether the player was paused.</param>
/// <param name="At">When the player showed it (the middle of the request's round trip).</param>
/// <param name="Uncertainty">How far the real position at <paramref name="At"/> may be from <paramref name="Position"/>.</param>
internal sealed record ClockReading(TimeSpan Position, bool IsPaused, DateTimeOffset At, TimeSpan Uncertainty);
