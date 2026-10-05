namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Starts <see cref="IGrabSession"/>s for playbacks that match the <see cref="PlaybackFilter"/>.
/// </summary>
public interface IGrabSessionFactory
{
    /// <summary>Starts driving the lights for a playback. Must return quickly; start the work on its own task.</summary>
    /// <param name="state">The playback's current state.</param>
    /// <returns>The running session.</returns>
    IGrabSession Start(PlaybackState state);
}
