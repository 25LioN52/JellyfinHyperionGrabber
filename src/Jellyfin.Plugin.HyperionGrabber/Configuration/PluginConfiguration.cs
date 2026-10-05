using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HyperionGrabber.Configuration;

/// <summary>
/// Persisted plugin settings (XML in Jellyfin's plugin configuration folder).
/// </summary>
/// <remarks>
/// Only add properties with safe defaults: older configuration files must keep loading. Renaming or removing a
/// property needs a migration, see docs/development/jellyfin-integration.md.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the host name or IP address of the Hyperion.ng / HyperHDR server.</summary>
    public string HyperionHost { get; set; } = string.Empty;

    /// <summary>Gets or sets the FlatBuffers server port.</summary>
    public int HyperionPort { get; set; } = HyperionDefaults.FlatBuffersPort;

    /// <summary>Gets or sets the priority to register (100-199; lower wins).</summary>
    public int HyperionPriority { get; set; } = HyperionDefaults.Priority;

    /// <summary>Gets or sets a value indicating whether playback on the selected devices drives the lights.</summary>
    public bool PlaybackEnabled { get; set; }

    /// <summary>Gets or sets the devices whose playback drives the lights. None selected means no playback does.</summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Jellyfin deserializes the configuration (XML on disk, JSON from the configuration page), which needs a setter.")]
    public Collection<PlaybackDeviceSelection> PlaybackDevices { get; set; } = [];

    /// <summary>Gets or sets the users whose playback drives the lights. None selected means every user.</summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Jellyfin deserializes the configuration (XML on disk, JSON from the configuration page), which needs a setter.")]
    public Collection<PlaybackUserSelection> PlaybackUsers { get; set; } = [];

    /// <summary>Creates the playback filter described by these settings.</summary>
    /// <returns>The filter.</returns>
    public PlaybackFilter ToPlaybackFilter() => PlaybackFilter.Create(
        PlaybackEnabled,
        (PlaybackDevices ?? []).Where(d => d is not null).Select(d => d.Id),
        (PlaybackUsers ?? []).Where(u => u is not null).Select(u => u.Id));
}
