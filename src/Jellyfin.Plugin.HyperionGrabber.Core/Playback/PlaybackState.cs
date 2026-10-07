using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// The latest known state of a playback that drives a <see cref="IGrabSession"/>.
/// </summary>
/// <remarks>
/// Clients report the position only now and then (Jellyfin for Kodi: in whole seconds, on pause, resume and seek and
/// otherwise about every 4 minutes), so consumers estimate the current position from it: roughly with
/// <see cref="EstimatePosition"/>, precisely with a <see cref="PositionTracker"/> over all reports.
/// </remarks>
public sealed record PlaybackState
{
    /// <summary>Gets the media server's session id.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the stable id of the playing device.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Gets the device's display name.</summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>Gets the client application.</summary>
    public string Client { get; init; } = string.Empty;

    /// <summary>Gets the id of the user who is playing, or <see cref="Guid.Empty"/> when unknown.</summary>
    public Guid UserId { get; init; }

    /// <summary>Gets the id of the playing item.</summary>
    public required Guid ItemId { get; init; }

    /// <summary>Gets the id of the playing media source, if known.</summary>
    public string? MediaSourceId { get; init; }

    /// <summary>Gets the last reported position.</summary>
    public TimeSpan Position { get; init; }

    /// <summary>Gets a value indicating whether the client reported <see cref="Position"/>; when <see langword="false"/>
    /// the report had no position and <see cref="Position"/> is an estimate.</summary>
    public bool IsPositionReported { get; init; } = true;

    /// <summary>Gets a value indicating whether this is the client's playback start report: its position is the
    /// requested start position, not a reading of the player's clock.</summary>
    public bool IsStart { get; init; }

    /// <summary>Gets a value indicating whether playback is paused.</summary>
    public bool IsPaused { get; init; }

    /// <summary>Gets when the media server reported <see cref="Position"/>.</summary>
    public DateTimeOffset ReportedAt { get; init; }

    /// <summary>Estimates the playback position at a time: <see cref="Position"/> plus the time since
    /// <see cref="ReportedAt"/>, or <see cref="Position"/> while paused.</summary>
    /// <param name="now">The time to estimate for.</param>
    /// <returns>The position, never before the reported one.</returns>
    public TimeSpan EstimatePosition(DateTimeOffset now)
    {
        if (IsPaused)
        {
            return Position;
        }

        var elapsed = now - ReportedAt;
        return elapsed > TimeSpan.Zero ? Position + elapsed : Position;
    }

    /// <summary>Creates the state described by a playback report.</summary>
    /// <param name="playbackEvent">The report.</param>
    /// <param name="reportedAt">When the report arrived.</param>
    /// <returns>The state.</returns>
    public static PlaybackState From(PlaybackEvent playbackEvent, DateTimeOffset reportedAt)
    {
        ArgumentNullException.ThrowIfNull(playbackEvent);
        return new PlaybackState
        {
            SessionId = playbackEvent.SessionId,
            DeviceId = playbackEvent.DeviceId,
            DeviceName = playbackEvent.DeviceName,
            Client = playbackEvent.Client,
            UserId = playbackEvent.UserId,
            ItemId = playbackEvent.ItemId,
            MediaSourceId = playbackEvent.MediaSourceId,
            Position = playbackEvent.Position ?? TimeSpan.Zero,
            IsStart = playbackEvent.Kind == PlaybackEventKind.Started,
            IsPaused = playbackEvent.IsPaused,
            ReportedAt = reportedAt,
        };
    }
}
