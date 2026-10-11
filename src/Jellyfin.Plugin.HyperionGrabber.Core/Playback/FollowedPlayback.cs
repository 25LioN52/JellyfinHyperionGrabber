using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// The playback that drives the lights, with its session's status.
/// </summary>
/// <param name="DeviceName">The playing device.</param>
/// <param name="Client">The client application.</param>
/// <param name="ItemId">The playing item.</param>
/// <param name="Since">When the lights started following it.</param>
/// <param name="Session">What its session is doing.</param>
public sealed record FollowedPlayback(string DeviceName, string Client, Guid ItemId, DateTimeOffset Since, GrabSessionStatus Session);
