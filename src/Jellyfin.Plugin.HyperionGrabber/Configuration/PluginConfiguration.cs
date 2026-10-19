using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Jellyfin.Plugin.HyperionGrabber.Core.Streaming;
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
    /// <summary>Gets or sets the host name or IP address of the (first) Hyperion.ng / HyperHDR server.</summary>
    public string HyperionHost { get; set; } = string.Empty;

    /// <summary>Gets or sets the FlatBuffers server port.</summary>
    public int HyperionPort { get; set; } = HyperionDefaults.FlatBuffersPort;

    /// <summary>Gets or sets the priority to register (100-199; lower wins).</summary>
    public int HyperionPriority { get; set; } = HyperionDefaults.Priority;

    /// <summary>Gets or sets the name of the first Hyperion server, shown in the status; optional.</summary>
    public string HyperionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets more Hyperion servers (one per TV), each with its own devices. The first server is
    /// <see cref="HyperionHost"/> with <see cref="PlaybackDevices"/>, as before multiple targets existed.
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Jellyfin deserializes the configuration (XML on disk, JSON from the configuration page), which needs a setter.")]
    public Collection<HyperionTargetConfiguration> AdditionalTargets { get; set; } = [];

    /// <summary>Gets or sets the number of frames per second sent to Hyperion during playback (1-60).</summary>
    public int FramesPerSecond { get; set; } = FfmpegFrameSourceOptions.DefaultFramesPerSecond;

    /// <summary>
    /// Gets or sets how many milliseconds earlier (positive) or later (negative) the lights show the picture, to cancel
    /// the delay between the TV and the LEDs (-2000 to 2000).
    /// </summary>
    public int LatencyOffsetMilliseconds { get; set; }

    /// <summary>Gets or sets how many seconds a pause may last before the lights are released (0 = hold the frame).</summary>
    public int PauseReleaseSeconds { get; set; } = (int)StreamingSettings.DefaultPauseRelease.TotalSeconds;

    /// <summary>Gets or sets a value indicating whether the position of Kodi clients is read from Kodi over JSON-RPC.</summary>
    public bool KodiClockEnabled { get; set; } = true;

    /// <summary>Gets or sets Kodi's JSON-RPC TCP port (1-65535).</summary>
    public int KodiJsonRpcPort { get; set; } = StreamingSettings.DefaultKodiPort;

    /// <summary>Gets or sets a value indicating whether playback on the selected devices drives the lights.</summary>
    public bool PlaybackEnabled { get; set; }

    /// <summary>Gets or sets the devices whose playback drives the first Hyperion server's lights.</summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Jellyfin deserializes the configuration (XML on disk, JSON from the configuration page), which needs a setter.")]
    public Collection<PlaybackDeviceSelection> PlaybackDevices { get; set; } = [];

    /// <summary>Gets or sets the users whose playback drives the lights. None selected means every user.</summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Jellyfin deserializes the configuration (XML on disk, JSON from the configuration page), which needs a setter.")]
    public Collection<PlaybackUserSelection> PlaybackUsers { get; set; } = [];

    /// <summary>Creates the streaming settings described by these settings.</summary>
    /// <returns>The settings.</returns>
    public StreamingSettings ToStreamingSettings() => new()
    {
        FramesPerSecond = FramesPerSecond,
        LatencyOffset = TimeSpan.FromMilliseconds(LatencyOffsetMilliseconds),
        PauseRelease = TimeSpan.FromSeconds(PauseReleaseSeconds),
        KodiClock = KodiClockEnabled,
        KodiPort = KodiJsonRpcPort,
    };

    /// <summary>Creates the playback filter described by these settings.</summary>
    /// <returns>The filter.</returns>
    public PlaybackFilter ToPlaybackFilter() => PlaybackFilter.Create(
        PlaybackEnabled,
        ToTargets(),
        (PlaybackUsers ?? []).Where(u => u is not null).Select(u => u.Id));

    /// <summary>Lists the Hyperion servers: the first one (also without a host, so the status can say it is missing),
    /// then the additional ones that have a host.</summary>
    /// <returns>The targets, in the configured order.</returns>
    public IReadOnlyList<PlaybackTarget> ToTargets()
    {
        var targets = new List<PlaybackTarget>
        {
            PlaybackTarget.Create(HyperionName, Hyperion(HyperionHost, HyperionPort, HyperionPriority), DeviceIds(PlaybackDevices)),
        };
        foreach (var target in AdditionalTargets ?? [])
        {
            if (target is not null && !string.IsNullOrWhiteSpace(target.Host))
            {
                targets.Add(PlaybackTarget.Create(target.Name, Hyperion(target.Host, target.Port, target.Priority), DeviceIds(target.Devices)));
            }
        }

        return targets;
    }

    private static HyperionClientOptions? Hyperion(string? host, int port, int priority)
        => string.IsNullOrWhiteSpace(host) ? null : new HyperionClientOptions { Host = host.Trim(), Port = port, Priority = priority };

    private static IEnumerable<string> DeviceIds(Collection<PlaybackDeviceSelection>? devices)
        => (devices ?? []).Where(d => d is not null).Select(d => d.Id);
}
