namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Tells what the lights are doing, for the status panel.
/// </summary>
public interface IPlaybackStatusSource
{
    /// <summary>Describes the playback that drives the lights and its session. Thread-safe; returns at once.</summary>
    /// <returns>A snapshot.</returns>
    PlaybackMonitorStatus GetStatus();
}
