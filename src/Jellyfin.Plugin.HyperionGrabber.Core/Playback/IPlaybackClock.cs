using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Reads the playback position from the player itself, next to its reports to the media server.
/// </summary>
/// <remarks>Runs on its own task; both members are thread-safe and return at once. Disposing stops it within a few
/// seconds.</remarks>
internal interface IPlaybackClock : IAsyncDisposable
{
    /// <summary>Gets the newest reading, or <see langword="null"/> while none is available.</summary>
    ClockReading? Latest { get; }

    /// <summary>Gets what the clock is doing, in words for the status panel.</summary>
    string Description { get; }
}
