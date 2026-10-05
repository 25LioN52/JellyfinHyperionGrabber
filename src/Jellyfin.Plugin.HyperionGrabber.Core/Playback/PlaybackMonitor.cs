using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Turns playback reports into at most one running <see cref="IGrabSession"/> for the (single) Hyperion target.
/// </summary>
/// <remarks>
/// <para><see cref="Post"/> and <see cref="UpdateFilter"/> never block, so they are safe to call from the media server's
/// event handlers. Reports are queued in a small bounded queue (the oldest is dropped if it ever fills) and processed in
/// order on the monitor's own task, which is also the only caller of the sessions.</para>
/// <para>Of all playbacks that match the <see cref="PlaybackFilter"/>, the one that started most recently drives the
/// target. When it stops, the most recent remaining matching playback takes over with its last reported state.</para>
/// <para>A progress report for a session the monitor has not seen start (for example after a server restart or when a
/// device is added to the filter mid-playback) counts as a start; a report with a different item restarts the
/// session.</para>
/// </remarks>
public sealed partial class PlaybackMonitor : IAsyncDisposable
{
    /// <summary>Maximum number of queued reports; clients report a few times per minute, so this is never reached in practice.</summary>
    internal const int QueueCapacity = 256;

    /// <summary>Maximum number of playbacks tracked at once; the least recently reported one is forgotten first.</summary>
    internal const int MaxTrackedPlaybacks = 64;

    private readonly IGrabSessionFactory _factory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Channel<Report> _queue;

    // Owned by the processing task.
    private readonly Dictionary<string, TrackedPlayback> _playing = new(StringComparer.Ordinal);
    private PlaybackFilter _appliedFilter = PlaybackFilter.Disabled;
    private ActiveSession? _active;
    private long _sequence;

    private PlaybackFilter _filter = PlaybackFilter.Disabled;
    private Task _processing = Task.CompletedTask;
    private int _started;
    private long _processedReports;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackMonitor"/> class. Call <see cref="Start"/> to begin processing.
    /// </summary>
    /// <param name="factory">Starts the sessions that drive the lights.</param>
    /// <param name="timeProvider">Clock used to time-stamp reports.</param>
    /// <param name="logger">Logger.</param>
    public PlaybackMonitor(IGrabSessionFactory factory, TimeProvider timeProvider, ILogger<PlaybackMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _factory = factory;
        _timeProvider = timeProvider;
        _logger = logger;
        _queue = Channel.CreateBounded<Report>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                AllowSynchronousContinuations = false,
            },
            _ => Log.ReportDropped(_logger));
    }

    /// <summary>Gets the current filter.</summary>
    public PlaybackFilter Filter => Volatile.Read(ref _filter);

    /// <summary>Gets the number of reports and filter changes processed so far (lets tests wait without delays).</summary>
    internal long ProcessedReports => Interlocked.Read(ref _processedReports);

    /// <summary>Queues a playback report. Never blocks; ignored after the monitor stopped.</summary>
    /// <param name="playbackEvent">The report.</param>
    public void Post(PlaybackEvent playbackEvent)
    {
        ArgumentNullException.ThrowIfNull(playbackEvent);
        _queue.Writer.TryWrite(new Report(playbackEvent, _timeProvider.GetUtcNow()));
    }

    /// <summary>Replaces the filter. Playbacks that no longer match stop driving the lights; ones that now match start.</summary>
    /// <param name="filter">The new filter.</param>
    public void UpdateFilter(PlaybackFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Volatile.Write(ref _filter, filter);
        _queue.Writer.TryWrite(new Report(null, _timeProvider.GetUtcNow()));
    }

    /// <summary>Starts processing reports on a background task.</summary>
    /// <exception cref="InvalidOperationException">The monitor was already started.</exception>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The playback monitor was already started.");
        }

        _processing = Task.Run(ProcessAsync);
    }

    /// <summary>Stops processing and disposes the running session, if any.</summary>
    /// <param name="cancellationToken">Stops waiting; the session is still disposed in the background.</param>
    /// <returns>A task that completes when the running session was disposed.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        return _processing.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var report in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                Track(report);
                await ReconcileAsync(report.Event).ConfigureAwait(false);
                Interlocked.Increment(ref _processedReports);
            }
        }
        finally
        {
            await StopActiveAsync().ConfigureAwait(false);
            _playing.Clear();
        }
    }

    private void Track(Report report)
    {
        if (report.Event is not { } playbackEvent)
        {
            return;
        }

        var sessionId = playbackEvent.SessionId;
        if (playbackEvent.Kind == PlaybackEventKind.Stopped)
        {
            _playing.Remove(sessionId);
            Log.PlaybackStopped(_logger, playbackEvent.DeviceName, playbackEvent.DeviceId);
            return;
        }

        var state = PlaybackState.From(playbackEvent, report.ReceivedAt);
        if (playbackEvent.Kind == PlaybackEventKind.Progress
            && _playing.TryGetValue(sessionId, out var tracked)
            && tracked.State.ItemId == state.ItemId)
        {
            _playing[sessionId] = tracked with { State = state };
            return;
        }

        if (!_playing.ContainsKey(sessionId) && _playing.Count >= MaxTrackedPlaybacks)
        {
            ForgetLeastRecentlyReported();
        }

        _playing[sessionId] = new TrackedPlayback(state, ++_sequence);
        Log.PlaybackStarted(_logger, state.DeviceName, state.DeviceId, state.Client, state.UserId, state.ItemId);
    }

    private void ForgetLeastRecentlyReported()
    {
        string? oldest = null;
        var oldestReport = DateTimeOffset.MaxValue;
        foreach (var (sessionId, tracked) in _playing)
        {
            if (tracked.State.ReportedAt < oldestReport)
            {
                oldest = sessionId;
                oldestReport = tracked.State.ReportedAt;
            }
        }

        if (oldest is not null)
        {
            _playing.Remove(oldest);
        }
    }

    private async Task ReconcileAsync(PlaybackEvent? playbackEvent)
    {
        var filter = Volatile.Read(ref _filter);
        if (!ReferenceEquals(filter, _appliedFilter))
        {
            _appliedFilter = filter;
            Log.FilterChanged(_logger, filter.Enabled, filter.DeviceIds.Count, filter.UserIds.Count);
        }

        // Runs once per playback report (a few per minute), not per frame, so LINQ is fine here.
        var desired = _playing.Values
            .Where(tracked => filter.Matches(tracked.State.DeviceId, tracked.State.UserId))
            .MaxBy(tracked => tracked.Sequence);

        if (_active is { } active && (desired is null || desired.Sequence != active.Sequence))
        {
            await StopActiveAsync().ConfigureAwait(false);
        }

        if (desired is null)
        {
            return;
        }

        if (_active is null)
        {
            StartSession(desired);
        }
        else if (playbackEvent?.SessionId == _active.SessionId)
        {
            UpdateSession(_active, desired.State);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A faulty session must not stop the monitor; the next report retries.")]
    private void StartSession(TrackedPlayback playback)
    {
        var state = playback.State;
        try
        {
            _active = new ActiveSession(state.SessionId, playback.Sequence, _factory.Start(state));
            Log.SessionStarted(_logger, state.DeviceName, state.Client);
        }
        catch (Exception ex)
        {
            Log.SessionStartFailed(_logger, state.DeviceName, ex);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A faulty session must not stop the monitor.")]
    private void UpdateSession(ActiveSession active, PlaybackState state)
    {
        try
        {
            active.Session.Update(state);
        }
        catch (Exception ex)
        {
            Log.SessionUpdateFailed(_logger, state.DeviceName, ex);
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A faulty session must not stop the monitor.")]
    private async Task StopActiveAsync()
    {
        if (_active is not { } active)
        {
            return;
        }

        _active = null;
        try
        {
            await active.Session.DisposeAsync().ConfigureAwait(false);
            Log.SessionStopped(_logger, active.SessionId);
        }
        catch (Exception ex)
        {
            Log.SessionStopFailed(_logger, active.SessionId, ex);
        }
    }

    private readonly record struct Report(PlaybackEvent? Event, DateTimeOffset ReceivedAt);

    private sealed record TrackedPlayback(PlaybackState State, long Sequence);

    private sealed record ActiveSession(string SessionId, long Sequence, IGrabSession Session);

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Lights follow playback on {DeviceName} ({Client})")]
        public static partial void SessionStarted(ILogger logger, string deviceName, string client);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Lights stopped following playback session {SessionId}")]
        public static partial void SessionStopped(ILogger logger, string sessionId);

        [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Could not start the lights for playback on {DeviceName}")]
        public static partial void SessionStartFailed(ILogger logger, string deviceName, Exception exception);

        [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "The lights session for {DeviceName} failed to handle a playback update")]
        public static partial void SessionUpdateFailed(ILogger logger, string deviceName, Exception exception);

        [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "The lights session for playback session {SessionId} failed to stop cleanly")]
        public static partial void SessionStopFailed(ILogger logger, string sessionId, Exception exception);

        [LoggerMessage(EventId = 6, Level = LogLevel.Debug, Message = "Playback started on {DeviceName} ({DeviceId}, {Client}) by user {UserId}: item {ItemId}")]
        public static partial void PlaybackStarted(ILogger logger, string deviceName, string deviceId, string client, Guid userId, Guid itemId);

        [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Playback stopped on {DeviceName} ({DeviceId})")]
        public static partial void PlaybackStopped(ILogger logger, string deviceName, string deviceId);

        [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Playback filter: enabled {Enabled}, {DeviceCount} device(s), {UserCount} user(s) (0 = all users)")]
        public static partial void FilterChanged(ILogger logger, bool enabled, int deviceCount, int userCount);

        [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Dropped a playback report because the queue was full")]
        public static partial void ReportDropped(ILogger logger);
    }
}
