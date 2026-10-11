namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// What a <see cref="IGrabSession"/> is doing, as shown on the status panel.
/// </summary>
public enum GrabSessionPhase
{
    /// <summary>Reading the settings, opening the video, waiting for the first picture.</summary>
    Starting,

    /// <summary>Sending pictures to Hyperion.</summary>
    Streaming,

    /// <summary>Playback is paused; the lights hold the last picture.</summary>
    Paused,

    /// <summary>Paused longer than the release time: Hyperion shows its default until playback resumes.</summary>
    PausedReleased,

    /// <summary>Hyperion cannot be reached or dropped the connection; reconnecting while decoding goes on.</summary>
    Reconnecting,

    /// <summary>Hyperion has been unreachable for over a minute: decoding stopped, connect attempts go on.</summary>
    WaitingForHyperion,

    /// <summary>The lights do not follow this playback (see <see cref="GrabSessionStatus.Problem"/>).</summary>
    Stopped,
}
