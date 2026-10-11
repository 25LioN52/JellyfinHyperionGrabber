namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// A snapshot of what the <see cref="PlaybackMonitor"/> is doing, for the status panel.
/// </summary>
/// <param name="FollowsPlayback">Whether playback drives the lights at all.</param>
/// <param name="DeviceCount">The number of selected devices.</param>
/// <param name="UserCount">The number of selected users (0 = every user).</param>
/// <param name="Playback">The playback that drives the lights now, if any.</param>
/// <param name="LastProblem">The most recent problem, from the current session or one that already stopped.</param>
public sealed record PlaybackMonitorStatus(bool FollowsPlayback, int DeviceCount, int UserCount, FollowedPlayback? Playback, ProblemReport? LastProblem);
