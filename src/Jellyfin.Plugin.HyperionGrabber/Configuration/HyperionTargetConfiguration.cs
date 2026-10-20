using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

namespace Jellyfin.Plugin.HyperionGrabber.Configuration;

/// <summary>
/// Another Hyperion server (one per TV) and the devices whose playback it shows, added on the configuration page.
/// </summary>
public class HyperionTargetConfiguration
{
    /// <summary>Gets or sets the name shown in the status, for example the room; optional.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the host name or IP address of the Hyperion.ng / HyperHDR server.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the FlatBuffers server port.</summary>
    public int Port { get; set; } = HyperionDefaults.FlatBuffersPort;

    /// <summary>Gets or sets the priority to register (100-199; lower wins).</summary>
    public int Priority { get; set; } = HyperionDefaults.Priority;

    /// <summary>Gets or sets the devices whose playback drives this server's lights.</summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Jellyfin deserializes the configuration (XML on disk, JSON from the configuration page), which needs a setter.")]
    public Collection<PlaybackDeviceSelection> Devices { get; set; } = [];
}
