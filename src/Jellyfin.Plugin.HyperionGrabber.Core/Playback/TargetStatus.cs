namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// What one target's lights are doing, for the status panel.
/// </summary>
/// <param name="Name">The target's name (see <see cref="PlaybackTarget.Name"/>).</param>
/// <param name="Key">The server and priority it registers with.</param>
/// <param name="DeviceCount">The number of devices selected for it.</param>
/// <param name="Playback">The playback that drives it now, if any.</param>
/// <param name="LastProblem">Its most recent problem, from the current session or one that already stopped.</param>
public sealed record TargetStatus(string Name, PlaybackTargetKey Key, int DeviceCount, FollowedPlayback? Playback, ProblemReport? LastProblem);
