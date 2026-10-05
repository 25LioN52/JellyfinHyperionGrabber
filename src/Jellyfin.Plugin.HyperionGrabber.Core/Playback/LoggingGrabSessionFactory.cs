using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Placeholder <see cref="IGrabSessionFactory"/> that only logs matching playback, used until frame streaming
/// (roadmap M1) replaces it.
/// </summary>
public sealed partial class LoggingGrabSessionFactory : IGrabSessionFactory
{
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingGrabSessionFactory"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public LoggingGrabSessionFactory(ILogger<LoggingGrabSessionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IGrabSession Start(PlaybackState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Log.Started(_logger, state.ItemId, state.MediaSourceId, state.Position);
        return new Session(_logger);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Playback of item {ItemId} (media source {MediaSourceId}) at {Position} matches the filter; streaming frames to Hyperion is not available in this version yet")]
        public static partial void Started(ILogger logger, Guid itemId, string? mediaSourceId, TimeSpan position);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Playback position {Position}, paused {IsPaused}")]
        public static partial void Updated(ILogger logger, TimeSpan position, bool isPaused);
    }

    private sealed class Session(ILogger logger) : IGrabSession
    {
        public void Update(PlaybackState state) => Log.Updated(logger, state.Position, state.IsPaused);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
