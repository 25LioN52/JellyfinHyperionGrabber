using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.HyperionGrabber.Api;

/// <summary>
/// Devices and users from Jellyfin's recent sessions, offered on the configuration page for the playback filter.
/// </summary>
/// <param name="Devices">Devices, most recently active first.</param>
/// <param name="Users">Users, by name.</param>
public sealed record PlaybackClientsResponse(IReadOnlyList<PlaybackClientDevice> Devices, IReadOnlyList<PlaybackClientUser> Users)
{
    /// <summary>Creates the response from Jellyfin's sessions.</summary>
    /// <param name="sessions">Jellyfin's current sessions.</param>
    /// <returns>One entry per device and per user.</returns>
    internal static PlaybackClientsResponse From(IEnumerable<SessionInfo> sessions)
    {
        var list = sessions.Where(s => s is not null && !string.IsNullOrEmpty(s.DeviceId)).ToList();
        var devices = list
            .GroupBy(s => s.DeviceId, StringComparer.Ordinal)
            .Select(g => g.MaxBy(s => s.LastActivityDate)!)
            .OrderByDescending(s => s.LastActivityDate)
            .Select(s => new PlaybackClientDevice(s.DeviceId, s.DeviceName ?? s.DeviceId, s.Client ?? string.Empty, s.UserName, s.LastActivityDate))
            .ToList();
        var users = list
            .Where(s => s.UserId != Guid.Empty)
            .GroupBy(s => s.UserId)
            .Select(g => new PlaybackClientUser(g.Key, g.First().UserName ?? g.Key.ToString("N")))
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PlaybackClientsResponse(devices, users);
    }
}
