using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;

namespace Jellyfin.Plugin.HyperionGrabber.Api;

/// <summary>
/// What the lights are doing, for the status panel on the configuration page: one sentence overall and the status of
/// each Hyperion server.
/// </summary>
public sealed record StatusResponse
{
    /// <summary>Gets a machine-readable state: <c>Off</c>, <c>NoDevices</c> or <c>On</c>.</summary>
    public required string State { get; init; }

    /// <summary>Gets one sentence for the user; with one Hyperion server, that server's summary.</summary>
    public required string Summary { get; init; }

    /// <summary>Gets each Hyperion server's status, in the configured order.</summary>
    public IReadOnlyList<TargetStatusResponse> Targets { get; init; } = [];

    /// <summary>Creates the response.</summary>
    /// <param name="status">The monitor's status.</param>
    /// <param name="itemName">Looks up a playing item's name, if Jellyfin still knows it.</param>
    /// <returns>The response.</returns>
    internal static StatusResponse From(PlaybackMonitorStatus status, Func<Guid, string?> itemName)
    {
        ArgumentNullException.ThrowIfNull(status);
        var targets = status.Targets.Select(target => TargetStatusResponse.From(target, itemName)).ToList();
        if (!status.FollowsPlayback)
        {
            return new StatusResponse { State = "Off", Summary = "Follow playback is off: the lights do not follow any playback.", Targets = targets };
        }

        if (status.DeviceCount == 0)
        {
            return new StatusResponse { State = "NoDevices", Summary = "No device is selected: tick your TV under Devices and save.", Targets = targets };
        }

        return new StatusResponse
        {
            State = "On",
            Summary = targets.Count == 1
                ? targets[0].Summary
                : string.Create(CultureInfo.InvariantCulture, $"Following playback on {targets.Count} Hyperion servers."),
            Targets = targets,
        };
    }
}
