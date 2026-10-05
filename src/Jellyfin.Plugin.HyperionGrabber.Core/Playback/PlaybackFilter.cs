using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Decides which playbacks turn on the lights: only selected devices, optionally only selected users.
/// </summary>
public sealed class PlaybackFilter
{
    private PlaybackFilter(bool enabled, FrozenSet<string> deviceIds, FrozenSet<Guid> userIds)
    {
        Enabled = enabled;
        DeviceIds = deviceIds;
        UserIds = userIds;
    }

    /// <summary>Gets a filter that matches nothing.</summary>
    public static PlaybackFilter Disabled { get; } = new(false, FrozenSet<string>.Empty, FrozenSet<Guid>.Empty);

    /// <summary>Gets a value indicating whether playback drives the lights at all.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the ids of the devices whose playback drives the lights. Empty means none.</summary>
    public IReadOnlySet<string> DeviceIds { get; }

    /// <summary>Gets the ids of the users whose playback drives the lights. Empty means every user.</summary>
    public IReadOnlySet<Guid> UserIds { get; }

    /// <summary>Creates a filter.</summary>
    /// <param name="enabled">Whether playback drives the lights.</param>
    /// <param name="deviceIds">Selected devices; blank ids are ignored. With none selected nothing matches.</param>
    /// <param name="userIds">Selected users; <see cref="Guid.Empty"/> is ignored. With none selected every user matches.</param>
    /// <returns>The filter.</returns>
    public static PlaybackFilter Create(bool enabled, IEnumerable<string?> deviceIds, IEnumerable<Guid> userIds)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        ArgumentNullException.ThrowIfNull(userIds);
        return new PlaybackFilter(
            enabled,
            deviceIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).ToFrozenSet(StringComparer.Ordinal),
            userIds.Where(id => id != Guid.Empty).ToFrozenSet());
    }

    /// <summary>Gets a value indicating whether playback on a device by a user drives the lights.</summary>
    /// <param name="deviceId">The playing device.</param>
    /// <param name="userId">The playing user, or <see cref="Guid.Empty"/> when unknown.</param>
    /// <returns><see langword="true"/> when the playback matches.</returns>
    public bool Matches(string deviceId, Guid userId)
        => Enabled
            && DeviceIds.Contains(deviceId)
            && (UserIds.Count == 0 || UserIds.Contains(userId));
}
