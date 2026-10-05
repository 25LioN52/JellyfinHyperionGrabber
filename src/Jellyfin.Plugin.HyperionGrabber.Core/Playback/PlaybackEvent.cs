using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// A host-independent playback report, translated from the media server's session events.
/// </summary>
public sealed record PlaybackEvent
{
    /// <summary>Gets what happened.</summary>
    public required PlaybackEventKind Kind { get; init; }

    /// <summary>Gets the media server's session id; one client connection plays at most one item at a time.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the stable id of the playing device (the device filter matches on it).</summary>
    public required string DeviceId { get; init; }

    /// <summary>Gets the device's display name.</summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>Gets the client application, for example "Kodi".</summary>
    public string Client { get; init; } = string.Empty;

    /// <summary>Gets the id of the user who is playing, or <see cref="Guid.Empty"/> when unknown.</summary>
    public Guid UserId { get; init; }

    /// <summary>Gets the id of the playing item.</summary>
    public required Guid ItemId { get; init; }

    /// <summary>Gets the id of the playing media source (version) of the item, if the client reported it.</summary>
    public string? MediaSourceId { get; init; }

    /// <summary>Gets the reported playback position, if any.</summary>
    public TimeSpan? Position { get; init; }

    /// <summary>Gets a value indicating whether playback is paused.</summary>
    public bool IsPaused { get; init; }
}
