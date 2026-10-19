using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// A snapshot of what the <see cref="PlaybackMonitor"/> is doing, for the status panel.
/// </summary>
/// <param name="FollowsPlayback">Whether playback drives the lights at all.</param>
/// <param name="UserCount">The number of selected users (0 = every user).</param>
/// <param name="Targets">Each target, in the configured order.</param>
public sealed record PlaybackMonitorStatus(bool FollowsPlayback, int UserCount, IReadOnlyList<TargetStatus> Targets)
{
    /// <summary>Gets the number of devices selected for any target.</summary>
    public int DeviceCount => Targets.Sum(target => target.DeviceCount);
}
