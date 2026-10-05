using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.HyperionGrabber.Playback;

/// <summary>
/// Translates Jellyfin's session events into Core <see cref="PlaybackEvent"/>s.
/// </summary>
internal static class PlaybackEventMapper
{
    /// <summary>Maps a Jellyfin playback event.</summary>
    /// <param name="kind">Which <see cref="MediaBrowser.Controller.Session.ISessionManager"/> event was raised.</param>
    /// <param name="args">The event arguments.</param>
    /// <returns>The event, or <see langword="null"/> when it cannot be attributed to a session, device and item.</returns>
    public static PlaybackEvent? Map(PlaybackEventKind kind, PlaybackProgressEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var session = args.Session;
        var sessionId = session?.Id;
        var deviceId = session?.DeviceId ?? args.DeviceId;
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(deviceId))
        {
            return null;
        }

        // A stop only needs the session; starts and progress need the item to decode it later.
        if (args.Item is null && kind != PlaybackEventKind.Stopped)
        {
            return null;
        }

        return new PlaybackEvent
        {
            Kind = kind,
            SessionId = sessionId,
            DeviceId = deviceId,
            DeviceName = session?.DeviceName ?? args.DeviceName ?? deviceId,
            Client = session?.Client ?? args.ClientName ?? string.Empty,
            UserId = session?.UserId ?? Guid.Empty,
            ItemId = args.Item?.Id ?? Guid.Empty,
            MediaSourceId = args.MediaSourceId,
            Position = args.PlaybackPositionTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null,
            IsPaused = args.IsPaused,
        };
    }
}
