using System;

namespace Jellyfin.Plugin.HyperionGrabber.Api;

/// <summary>
/// A user seen in a recent Jellyfin session.
/// </summary>
/// <param name="Id">Jellyfin user id.</param>
/// <param name="Name">User name.</param>
public sealed record PlaybackClientUser(Guid Id, string Name);
