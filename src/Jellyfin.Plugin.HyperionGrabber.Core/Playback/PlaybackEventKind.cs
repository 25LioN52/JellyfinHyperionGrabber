namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// What a media server reported about a playback.
/// </summary>
public enum PlaybackEventKind
{
    /// <summary>Playback of an item started.</summary>
    Started,

    /// <summary>Periodic report, also sent on pause, resume and seek (there are no separate events for those).</summary>
    Progress,

    /// <summary>Playback of an item stopped.</summary>
    Stopped,
}
