using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;

namespace Jellyfin.Plugin.HyperionGrabber.Playback;

/// <summary>
/// Supplies the playback filter from the saved configuration and reports when it changes.
/// </summary>
internal interface IPlaybackFilterSource
{
    /// <summary>Raised after the configuration was saved, on the thread that saved it.</summary>
    event EventHandler? Changed;

    /// <summary>Gets the filter described by the current configuration.</summary>
    /// <returns>The filter.</returns>
    PlaybackFilter GetCurrent();
}
