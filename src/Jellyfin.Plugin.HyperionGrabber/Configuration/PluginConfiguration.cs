using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
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
}
