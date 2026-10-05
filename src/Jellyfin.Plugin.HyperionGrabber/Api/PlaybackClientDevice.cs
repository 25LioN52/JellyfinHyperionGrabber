using System;

namespace Jellyfin.Plugin.HyperionGrabber.Api;

/// <summary>
/// A device seen in a recent Jellyfin session.
/// </summary>
/// <param name="Id">Jellyfin's device id.</param>
/// <param name="Name">Device name.</param>
/// <param name="Client">Client application, for example "Kodi".</param>
/// <param name="UserName">User of the device's most recent session, if signed in.</param>
/// <param name="LastActivity">Last activity of the device (UTC).</param>
public sealed record PlaybackClientDevice(string Id, string Name, string Client, string? UserName, DateTime LastActivity);
