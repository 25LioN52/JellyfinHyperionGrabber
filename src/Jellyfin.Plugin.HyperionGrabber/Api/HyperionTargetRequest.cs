using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

namespace Jellyfin.Plugin.HyperionGrabber.Api;

/// <summary>
/// Hyperion server to test, as entered on the configuration page (not yet saved).
/// </summary>
public class HyperionTargetRequest
{
    /// <summary>Gets or sets the host name or IP address.</summary>
    public string? Host { get; set; }

    /// <summary>Gets or sets the FlatBuffers port.</summary>
    public int Port { get; set; } = HyperionDefaults.FlatBuffersPort;

    /// <summary>Gets or sets the priority.</summary>
    public int Priority { get; set; } = HyperionDefaults.Priority;

    /// <summary>Gets or sets how long the test pattern plays, in seconds (test pattern only).</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Converts the request to client options.</summary>
    /// <returns>Unvalidated client options.</returns>
    public HyperionClientOptions ToClientOptions() => new()
    {
        Host = Host?.Trim() ?? string.Empty,
        Port = Port,
        Priority = Priority,
    };
}
