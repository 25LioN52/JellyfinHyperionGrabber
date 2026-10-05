using System;

namespace Jellyfin.Plugin.HyperionGrabber.Configuration;

/// <summary>
/// A user selected on the configuration page.
/// </summary>
public class PlaybackUserSelection
{
    /// <summary>Gets or sets the Jellyfin user id (what the filter matches on).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the user name when it was selected, shown when the user has no recent session.</summary>
    public string Name { get; set; } = string.Empty;
}
