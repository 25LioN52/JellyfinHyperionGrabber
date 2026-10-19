namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Starts <see cref="IGrabSession"/>s for playbacks that match a target of the <see cref="PlaybackFilter"/>.
/// </summary>
public interface IGrabSessionFactory
{
    /// <summary>Starts driving a target's lights for a playback. Must return quickly; start the work on its own task.</summary>
    /// <param name="state">The playback's current state.</param>
    /// <param name="target">The target whose lights follow the playback.</param>
    /// <returns>The running session.</returns>
    IGrabSession Start(PlaybackState state, PlaybackTarget target);
}
