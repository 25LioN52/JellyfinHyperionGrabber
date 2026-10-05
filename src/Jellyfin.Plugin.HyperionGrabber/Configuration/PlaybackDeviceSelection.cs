namespace Jellyfin.Plugin.HyperionGrabber.Configuration;

/// <summary>
/// A device selected on the configuration page.
/// </summary>
public class PlaybackDeviceSelection
{
    /// <summary>Gets or sets Jellyfin's device id (what the filter matches on).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the device name when it was selected, shown while the device is offline.</summary>
    public string Name { get; set; } = string.Empty;
}
