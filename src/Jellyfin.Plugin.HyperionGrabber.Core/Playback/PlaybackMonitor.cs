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
/// Turns playback reports into at most one running <see cref="IGrabSession"/> per <see cref="PlaybackTarget"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Post"/> and <see cref="UpdateFilter"/> never block, so they are safe to call from the media server's
/// event handlers. Reports are queued in a small bounded queue (the oldest is dropped if it ever fills) and processed in
/// order on the monitor's own task, which is also the only caller of the sessions.</para>
/// <para>Targets are independent. For each, of all playbacks that match it (its devices, the filter's users), the one
/// that started most recently drives it. When that playback stops, the most recent remaining matching playback takes
/// over with its last reported state. A playback on a device selected for two targets drives both. A target whose
/// server or priority changes (a new <see cref="PlaybackTargetKey"/>) is a new target: its session restarts.</para>
/// <para>A progress report for a session the monitor has not seen start (for example after a server restart or when a
/// device is added to the filter mid-playback) counts as a start; a report with a different item restarts the
/// session.</para>
/// <para><see cref="GetStatus"/> reads what the processing task published (each target's followed playback and last
/// problem) plus the sessions' own status, so it never waits for the queue.</para>
/// </remarks>
public sealed partial class PlaybackMonitor : IAsyncDisposable, IPlaybackStatusSource
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
    private readonly Dictionary<PlaybackTargetKey, ActiveSession> _active = [];
    private readonly Dictionary<PlaybackTargetKey, ProblemReport> _lastProblems = []; // From sessions that stopped or could not start.
    private PlaybackFilter _appliedFilter = PlaybackFilter.Disabled;
    private long _sequence;

    private PlaybackFilter _filter = PlaybackFilter.Disabled;

    // Written by the processing task (a new dictionary each time, never changed afterwards), read by GetStatus.
    private IReadOnlyDictionary<PlaybackTargetKey, PublishedTarget> _published = new Dictionary<PlaybackTargetKey, PublishedTarget>();
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

    /// <summary>Stops processing and disposes the running sessions.</summary>
    /// <param name="cancellationToken">Stops waiting; the sessions are still disposed in the background.</param>
    /// <returns>A task that completes when the running sessions were disposed.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        return _processing.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    /// <inheritdoc />
    public PlaybackMonitorStatus GetStatus()
    {
        var filter = Filter;
        var published = Volatile.Read(ref _published);
        var targets = new List<TargetStatus>(filter.Targets.Count);
        foreach (var target in filter.Targets)
        {
            published.TryGetValue(target.Key, out var state);
            var playback = state?.Playback;
            var session = playback?.Session.GetStatus();
            targets.Add(new TargetStatus(
                target.Name,
                target.Key,
                target.DeviceIds.Count,
                playback is null || session is null ? null : new FollowedPlayback(playback.DeviceName, playback.Client, playback.ItemId, playback.Since, session),
                Latest(session?.LastProblem, state?.LastProblem)));
        }

        return new PlaybackMonitorStatus(filter.Enabled, filter.UserIds.Count, targets);
    }

    private static ProblemReport? Latest(ProblemReport? first, ProblemReport? second)
        => first is null ? second : second is null || first.Time >= second.Time ? first : second;

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
        catch (Exception ex) when (LogFault(ex))
        {
            // Not reached: LogFault returns false, so the exception keeps propagating and faults the task.
        }
        finally
        {
            await StopSessionsAsync([.. _active.Keys]).ConfigureAwait(false);
            _playing.Clear();
        }
    }

    /// <summary>Logs an unexpected failure of the processing task, which would otherwise only surface on stop.</summary>
    private bool LogFault(Exception exception)
    {
        Log.MonitorFailed(_logger, exception);
        return false;
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
            if (playbackEvent.Position is null)
            {
                // Without a position the report would read as a seek to the start; keep the estimate instead.
                state = state with { Position = tracked.State.EstimatePosition(report.ReceivedAt), IsPositionReported = false };
            }

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
            Log.FilterChanged(_logger, filter.Enabled, filter.Targets.Count, filter.DeviceIds.Count, filter.UserIds.Count);
        }

        // Runs once per playback report (a few per minute), not per frame, so LINQ is fine here.
        var desired = new Dictionary<PlaybackTargetKey, TrackedPlayback?>();
        foreach (var target in filter.Targets)
        {
            desired[target.Key] = _playing.Values
                .Where(tracked => filter.Matches(target, tracked.State.DeviceId, tracked.State.UserId))
                .MaxBy(tracked => tracked.Sequence);
        }

        // Sessions of targets that are gone or now follow another playback stop together, so stopping one playback
        // that drives several targets takes the time of one stop.
        var stale = _active
            .Where(pair => !desired.TryGetValue(pair.Key, out var playback) || playback?.Sequence != pair.Value.Sequence)
            .Select(pair => pair.Key)
            .ToList();
        await StopSessionsAsync(stale).ConfigureAwait(false);

        // A removed target's problems go with it (also those its session reported while stopping).
        foreach (var key in _lastProblems.Keys.Where(key => !desired.ContainsKey(key)).ToList())
        {
            _lastProblems.Remove(key);
        }

        foreach (var target in filter.Targets)
        {
            if (desired[target.Key] is not { } playback)
            {
                continue;
            }

            if (!_active.TryGetValue(target.Key, out var active))
            {
                StartSession(target, playback);
            }
            else if (playbackEvent?.SessionId == active.SessionId)
            {
                UpdateSession(active, playback.State);
            }
        }

        Publish();
    }

    /// <summary>Publishes each target's playback and last problem for <see cref="GetStatus"/>.</summary>
    private void Publish()
    {
        var published = new Dictionary<PlaybackTargetKey, PublishedTarget>();
        foreach (var (key, active) in _active)
        {
            published[key] = new PublishedTarget(active.Playback, _lastProblems.GetValueOrDefault(key));
        }

        foreach (var (key, problem) in _lastProblems)
        {
            published.TryAdd(key, new PublishedTarget(null, problem));
        }

        Volatile.Write(ref _published, published);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A faulty session must not stop the monitor; the next report retries.")]
    private void StartSession(PlaybackTarget target, TrackedPlayback playback)
    {
        var state = playback.State;
        try
        {
            var session = _factory.Start(state, target);
            var published = new PublishedPlayback(state.DeviceName, state.Client, state.ItemId, _timeProvider.GetUtcNow(), session);
            _active[target.Key] = new ActiveSession(state.SessionId, playback.Sequence, target.Name, published);
            Log.SessionStarted(_logger, state.DeviceName, state.Client, target.Name);
        }
        catch (Exception ex)
        {
            Log.SessionStartFailed(_logger, state.DeviceName, target.Name, ex);
            _lastProblems[target.Key] = new ProblemReport(_timeProvider.GetUtcNow(), $"Could not start the lights for playback on {state.DeviceName}: {ex.Message}");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A faulty session must not stop the monitor.")]
    private void UpdateSession(ActiveSession active, PlaybackState state)
    {
        try
        {
            active.Playback.Session.Update(state);
        }
        catch (Exception ex)
        {
            Log.SessionUpdateFailed(_logger, state.DeviceName, ex);
        }
    }

    /// <summary>Disposes the sessions of some targets at the same time; each dispose is bounded by the session.</summary>
    private async Task StopSessionsAsync(List<PlaybackTargetKey> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        var stopping = new List<(PlaybackTargetKey Key, Task<ProblemReport?> Stop)>(keys.Count);
        foreach (var key in keys.Where(_active.ContainsKey))
        {
            stopping.Add((key, StopSessionAsync(_active[key])));
            _active.Remove(key);
        }

        Publish();
        await Task.WhenAll(stopping.Select(pair => pair.Stop)).ConfigureAwait(false);

        // Keep the stopped sessions' last problems for the status panel (merged here, on the processing task; all are done).
        foreach (var (key, stop) in stopping)
        {
            if (Latest(await stop.ConfigureAwait(false), _lastProblems.GetValueOrDefault(key)) is { } problem)
            {
                _lastProblems[key] = problem;
            }
        }

        Publish();
    }

    /// <returns>The stopped session's last problem, if any.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A faulty session must not stop the monitor.")]
    private async Task<ProblemReport?> StopSessionAsync(ActiveSession active)
    {
        var session = active.Playback.Session;
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
            Log.SessionStopped(_logger, active.SessionId, active.TargetName);
            return session.GetStatus().LastProblem;
        }
        catch (Exception ex)
        {
            Log.SessionStopFailed(_logger, active.SessionId, active.TargetName, ex);
            return null;
        }
    }

    private readonly record struct Report(PlaybackEvent? Event, DateTimeOffset ReceivedAt);

    private sealed record TrackedPlayback(PlaybackState State, long Sequence);

    private sealed record ActiveSession(string SessionId, long Sequence, string TargetName, PublishedPlayback Playback);

    private sealed record PublishedPlayback(string DeviceName, string Client, Guid ItemId, DateTimeOffset Since, IGrabSession Session);

    private sealed record PublishedTarget(PublishedPlayback? Playback, ProblemReport? LastProblem);

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Lights follow playback on {DeviceName} ({Client}) through {Target}")]
        public static partial void SessionStarted(ILogger logger, string deviceName, string client, string target);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Lights of {Target} stopped following playback session {SessionId}")]
        public static partial void SessionStopped(ILogger logger, string sessionId, string target);

        [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Could not start the lights of {Target} for playback on {DeviceName}")]
        public static partial void SessionStartFailed(ILogger logger, string deviceName, string target, Exception exception);

        [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "The lights session for {DeviceName} failed to handle a playback update")]
        public static partial void SessionUpdateFailed(ILogger logger, string deviceName, Exception exception);

        [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "The lights session of {Target} for playback session {SessionId} failed to stop cleanly")]
        public static partial void SessionStopFailed(ILogger logger, string sessionId, string target, Exception exception);

        [LoggerMessage(EventId = 6, Level = LogLevel.Debug, Message = "Playback started on {DeviceName} ({DeviceId}, {Client}) by user {UserId}: item {ItemId}")]
        public static partial void PlaybackStarted(ILogger logger, string deviceName, string deviceId, string client, Guid userId, Guid itemId);

        [LoggerMessage(EventId = 7, Level = LogLevel.Debug, Message = "Playback stopped on {DeviceName} ({DeviceId})")]
        public static partial void PlaybackStopped(ILogger logger, string deviceName, string deviceId);

        [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Playback filter: enabled {Enabled}, {TargetCount} Hyperion server(s), {DeviceCount} device(s), {UserCount} user(s) (0 = all users)")]
        public static partial void FilterChanged(ILogger logger, bool enabled, int targetCount, int deviceCount, int userCount);

        [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Dropped a playback report because the queue was full")]
        public static partial void ReportDropped(ILogger logger);

        [LoggerMessage(EventId = 10, Level = LogLevel.Critical, Message = "The playback monitor stopped unexpectedly; the lights no longer follow playback until Jellyfin restarts")]
        public static partial void MonitorFailed(ILogger logger, Exception exception);
    }
}
