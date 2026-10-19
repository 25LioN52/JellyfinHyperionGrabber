using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Decides which playbacks turn on which lights: each <see cref="PlaybackTarget"/> follows its selected devices,
/// optionally only for selected users.
/// </summary>
public sealed class PlaybackFilter
{
    /// <summary>Largest number of targets; each playing one costs an FFmpeg process on the server.</summary>
    public const int MaxTargets = 8;

    private PlaybackFilter(bool enabled, IReadOnlyList<PlaybackTarget> targets, FrozenSet<Guid> userIds)
    {
        Enabled = enabled;
        Targets = targets;
        UserIds = userIds;
        DeviceIds = targets.SelectMany(target => target.DeviceIds).ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>Gets a filter that matches nothing.</summary>
    public static PlaybackFilter Disabled { get; } = new(false, [], FrozenSet<Guid>.Empty);

    /// <summary>Gets a value indicating whether playback drives the lights at all.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the targets, each with its own devices; their keys are unique.</summary>
    public IReadOnlyList<PlaybackTarget> Targets { get; }

    /// <summary>Gets the ids of the devices selected for any target. Empty means no playback drives the lights.</summary>
    public IReadOnlySet<string> DeviceIds { get; }

    /// <summary>Gets the ids of the users whose playback drives the lights. Empty means every user.</summary>
    public IReadOnlySet<Guid> UserIds { get; }

    /// <summary>Creates a filter.</summary>
    /// <param name="enabled">Whether playback drives the lights.</param>
    /// <param name="targets">The targets. Of targets with the same key only the first is kept (they would fight over
    /// one priority), and only the first <see cref="MaxTargets"/>.</param>
    /// <param name="userIds">Selected users; <see cref="Guid.Empty"/> is ignored. With none selected every user matches.</param>
    /// <returns>The filter.</returns>
    public static PlaybackFilter Create(bool enabled, IEnumerable<PlaybackTarget> targets, IEnumerable<Guid> userIds)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(userIds);
        var keys = new HashSet<PlaybackTargetKey>();
        var unique = targets.Where(target => target is not null && keys.Add(target.Key)).Take(MaxTargets).ToArray();
        return new PlaybackFilter(enabled, unique, userIds.Where(id => id != Guid.Empty).ToFrozenSet());
    }

    /// <summary>Gets a value indicating whether playback on a device by a user drives a target.</summary>
    /// <param name="target">The target.</param>
    /// <param name="deviceId">The playing device.</param>
    /// <param name="userId">The playing user, or <see cref="Guid.Empty"/> when unknown.</param>
    /// <returns><see langword="true"/> when the playback matches.</returns>
    public bool Matches(PlaybackTarget target, string deviceId, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Enabled
            && target.DeviceIds.Contains(deviceId)
            && (UserIds.Count == 0 || UserIds.Contains(userId));
    }

    /// <summary>Gets a value indicating whether playback on a device by a user drives any target.</summary>
    /// <param name="deviceId">The playing device.</param>
    /// <param name="userId">The playing user, or <see cref="Guid.Empty"/> when unknown.</param>
    /// <returns><see langword="true"/> when the playback matches a target.</returns>
    public bool Matches(string deviceId, Guid userId)
    {
        foreach (var target in Targets)
        {
            if (Matches(target, deviceId, userId))
            {
                return true;
            }
        }

        return false;
    }
}
