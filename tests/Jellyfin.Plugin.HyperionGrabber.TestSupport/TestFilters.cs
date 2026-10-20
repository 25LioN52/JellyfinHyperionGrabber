using System;
using System.Collections.Generic;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;

namespace Jellyfin.Plugin.HyperionGrabber.TestSupport;

/// <summary>
/// Playback filters for tests.
/// </summary>
public static class TestFilters
{
    /// <summary>Creates a filter with one target, as configured before multiple targets existed.</summary>
    /// <param name="enabled">Whether playback drives the lights.</param>
    /// <param name="deviceIds">The target's devices.</param>
    /// <param name="userIds">Selected users; none means every user.</param>
    /// <param name="hyperion">The target's Hyperion server, if the test needs one.</param>
    /// <returns>The filter.</returns>
    public static PlaybackFilter OneTarget(bool enabled, IEnumerable<string?> deviceIds, IEnumerable<Guid> userIds, HyperionClientOptions? hyperion = null)
        => PlaybackFilter.Create(enabled, [PlaybackTarget.Create("TV", hyperion, deviceIds)], userIds);
}
