using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Streaming;

/// <summary>
/// Streams one playback to Hyperion: resolves the video, decodes from the playback position and, on every tick of a
/// timer running at the frame rate, sends the frame that is due.
/// </summary>
/// <remarks>
/// <para>All work runs on the session's own task; <see cref="Update"/> only publishes the newest playback state.</para>
/// <para>Hyperion is connected when the first frame is due, not before: opening and seeking a large file can take
/// longer than Hyperion's idle timeout, which would close a connection that has nothing to send yet.</para>
/// <para>Each tick estimates the playback position (a <see cref="PositionTracker"/> over the client's reports, moving on
/// with time unless paused) and sends the newest decoded frame at or before it. Older due frames are dropped, so a slow
/// Hyperion, network or decoder means fewer frames, never a queue. The frame source pools its buffers; while none is
/// free, FFmpeg blocks.</para>
/// <para>The light timing offset (<see cref="StreamingSettings.LatencyOffset"/>) is added to that estimate, so frames are
/// decoded and sent earlier (positive) or later (negative) to cancel the delay of the client, Hyperion and the LEDs.</para>
/// <para>While no new frame is due (pause, end of the video, slow decoder) the last frame is resent every
/// <see cref="KeepAliveInterval"/>, because Hyperion.ng and HyperHDR close FlatBuffers connections that stay silent.
/// After a pause longer than <see cref="StreamingSettings.PauseRelease"/>, FFmpeg is stopped and the Hyperion connection
/// closed, so Hyperion shows its default again; on resume decoding restarts at the playback position.</para>
/// <para>FFmpeg is restarted at the playback position when a report moves the estimate by more than
/// <see cref="SeekThreshold"/> (a seek), and when the decoder lags more than <see cref="LagTolerance"/> behind playback
/// after <see cref="RestartGrace"/>.</para>
/// <para>When Hyperion cannot be reached, rejects the registration or drops the connection, the session reconnects with
/// exponential backoff (<see cref="FirstRetryDelay"/> doubling up to <see cref="MaxRetryDelay"/>, reset by a successful
/// send). The connect runs as one task the ticks check, so a slow connect never stalls pacing; meanwhile decoding goes
/// on and due frames are dropped, so the first frame after reconnecting is the one at the playback position.</para>
/// <para>A decoding error stops the stream and releases Hyperion's priority; the session then idles until it is
/// disposed.</para>
/// </remarks>
internal sealed partial class StreamingGrabSession : IGrabSession
{
    /// <summary>How often the last frame is resent while no new frame is due; Hyperion's idle timeout is 5 s by default.</summary>
    internal static readonly TimeSpan KeepAliveInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>A report that moves the estimated position this far is a seek; decoding restarts at the new position.</summary>
    internal static readonly TimeSpan SeekThreshold = TimeSpan.FromSeconds(1);

    /// <summary>How far the decoder may lag behind playback before it is restarted at the playback position.</summary>
    internal static readonly TimeSpan LagTolerance = TimeSpan.FromSeconds(2);

    /// <summary>Time a decoder gets after starting (opening and seeking the file) before lagging counts.</summary>
    internal static readonly TimeSpan RestartGrace = TimeSpan.FromSeconds(5);

    /// <summary>Longest time <see cref="DisposeAsync"/> waits for the session to release FFmpeg and Hyperion.</summary>
    internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Wait before the first reconnect attempt after a failure; doubles with every further failure.</summary>
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Longest wait between reconnect attempts.</summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly StreamingGrabSessionFactory _factory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource<bool> _streamingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _hyperionReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _previousHyperionReleased;
    private readonly PositionTracker _tracker; // Owned by the run task.
    private PlaybackState _state;
    private Task _run = Task.CompletedTask;
    private long _framesSent;
    private long _framesDropped;
    private long _restarts;
    private long _ticks;
    private int _disposed;

    // Owned by the run task.
    private PlaybackState _appliedState;
    private IHyperionConnection? _connection;
    private IFrameSource? _source;
    private VideoFrame? _pending;
    private TimeSpan _nextPosition;
    private DateTimeOffset _sourceStartedAt;
    private bool _sourceHasFrames;
    private bool _sourceEnded;
    private TimeSpan _latencyOffset;
    private byte[] _lastFrame = [];
    private bool _hasLastFrame;
    private DateTimeOffset _lastSentAt;
    private DateTimeOffset? _pausedSince;
    private bool _releasedForPause;
    private Task<IHyperionConnection>? _connecting;
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Belongs to the connect in flight: disposed when its result is taken or, once given up, after it completed (the final release always gives it up).")]
    private CancellationTokenSource? _connectCancellation;
    private int _connectAttempts; // Since the last successful send.
    private int _failures; // Since the last successful send.
    private DateTimeOffset? _disconnectedSince;
    private DateTimeOffset _retryAt;

    private StreamingGrabSession(StreamingGrabSessionFactory factory, PlaybackState state, Task previousHyperionReleased)
    {
        _factory = factory;
        _previousHyperionReleased = previousHyperionReleased;
        _timeProvider = factory.TimeProvider;
        _logger = factory.SessionLogger;
        _state = state;
        _appliedState = state;
        _tracker = new PositionTracker(state);
    }

    /// <summary>Gets a task that completes with <see langword="true"/> once frames are being paced, or with
    /// <see langword="false"/> when the session ended without streaming.</summary>
    internal Task<bool> StreamingStarted => _streamingStarted.Task;

    /// <summary>Gets the session's task; it completes when streaming stopped and FFmpeg and Hyperion were released.</summary>
    internal Task Completion => _run;

    /// <summary>Gets a task that completes once the session stopped and no longer holds, or can still open, a Hyperion
    /// connection; unlike <see cref="Completion"/> it does not wait for FFmpeg.</summary>
    internal Task HyperionReleased => _hyperionReleased.Task;

    /// <summary>Gets the number of decoded frames sent to Hyperion (keep-alive repeats not included).</summary>
    internal long FramesSent => Interlocked.Read(ref _framesSent);

    /// <summary>Gets the number of decoded frames dropped because a newer frame was already due.</summary>
    internal long FramesDropped => Interlocked.Read(ref _framesDropped);

    /// <summary>Gets the number of times decoding restarted to follow the playback position.</summary>
    internal long Restarts => Interlocked.Read(ref _restarts);

    /// <summary>Gets the number of timer ticks handled so far (lets tests wait without delays).</summary>
    internal long Ticks => Interlocked.Read(ref _ticks);

    /// <inheritdoc />
    public void Update(PlaybackState state)
    {
        var previous = Interlocked.Exchange(ref _state, state);
        if (previous.IsPaused != state.IsPaused)
        {
            Log.PauseChanged(_logger, state.IsPaused, state.Position);
        }
    }

    /// <summary>
    /// Stops streaming, kills FFmpeg and clears Hyperion's priority; waits at most <see cref="StopTimeout"/>.
    /// </summary>
    /// <returns>A task that completes when the session stopped, or when the wait timed out.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await _run.WaitAsync(StopTimeout, _timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The task finishes releasing in the background; the monitor must not stall on it.
            Log.StopTimedOut(_logger, StopTimeout.TotalSeconds);
            return;
        }

        _stopping.Dispose();
    }

    /// <summary>Starts a session on its own task.</summary>
    /// <param name="factory">Dependencies.</param>
    /// <param name="state">The playback's state.</param>
    /// <param name="previousHyperionReleased">The previous session's <see cref="HyperionReleased"/>: after its stop timed
    /// out it may still be clearing the same priority, so this session connects only after that (or after
    /// <see cref="StopTimeout"/>).</param>
    /// <returns>The running session.</returns>
    internal static StreamingGrabSession Start(StreamingGrabSessionFactory factory, PlaybackState state, Task previousHyperionReleased)
    {
        var session = new StreamingGrabSession(factory, state, previousHyperionReleased);
        session._run = Task.Run(session.RunAsync);
        return session;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Top level of the session's task: any failure must stop this playback's lights and be logged now, not surface later as a failed dispose.")]
    private async Task RunAsync()
    {
        var token = _stopping.Token;
        var streaming = false;
        try
        {
            if (Prepare() is not { } plan)
            {
                return;
            }

            streaming = true;
            await StreamAsync(plan, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Playback stopped.
        }
        catch (FrameSourceException ex)
        {
            Log.StreamFailed(_logger, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Unexpected(_logger, ex);
        }
        finally
        {
            await ReleaseAsync(final: true).ConfigureAwait(false);
            _streamingStarted.TrySetResult(false);
            if (streaming)
            {
                Log.Stopped(_logger, FramesSent, FramesDropped, Restarts);
            }
        }
    }

    /// <returns>What to stream, or <see langword="null"/> (logged) when this playback cannot drive the lights.</returns>
    private Plan? Prepare()
    {
        var state = Volatile.Read(ref _state);
        var settings = _factory.StreamingSettings.GetSettings();
        if (settings.Hyperion is not { } hyperion)
        {
            Log.NotConfigured(_logger);
            return null;
        }

        IReadOnlyList<string> errors = [.. hyperion.GetValidationErrors(), .. settings.GetValidationErrors()];
        if (errors.Count > 0)
        {
            Log.InvalidSettings(_logger, string.Join(" ", errors));
            return null;
        }

        var video = _factory.Resolver.Resolve(state.ItemId, state.MediaSourceId);
        if (!video.IsSupported)
        {
            Log.Unsupported(_logger, state.ItemId, video.Reason);
            return null;
        }

        var frames = FfmpegFrameSourceOptions.Create(_factory.FfmpegSettings.GetSettings(), video.Input, TimeSpan.Zero) with
        {
            FramesPerSecond = settings.FramesPerSecond,
        };
        errors = frames.GetValidationErrors();
        if (errors.Count > 0)
        {
            Log.InvalidSettings(_logger, string.Join(" ", errors));
            return null;
        }

        return new Plan(hyperion, frames, settings.LatencyOffset, settings.PauseRelease);
    }

    private async Task StreamAsync(Plan plan, CancellationToken token)
    {
        var options = plan.Frames;
        var interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / options.FramesPerSecond);
        _latencyOffset = plan.LatencyOffset;
        _lastFrame = new byte[options.FrameLength];
        _hasLastFrame = false;
        _lastSentAt = DateTimeOffset.MinValue;

        using var timer = new PeriodicTimer(interval, _timeProvider);
        var now = _timeProvider.GetUtcNow();
        ApplyReport(Volatile.Read(ref _state));
        var position = TargetPosition(now);
        StartSource(options, position, now);
        Log.Streaming(_logger, options.OutputWidth, options.OutputHeight, options.FramesPerSecond, position);
        _streamingStarted.TrySetResult(true);

        // Per tick: no allocations of our own; the frame is copied into _lastFrame so its pooled buffer returns at once.
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            try
            {
                now = _timeProvider.GetUtcNow();
                var state = Volatile.Read(ref _state);
                var seek = ApplyReport(state);
                if (await HandlePauseAsync(state, plan, now).ConfigureAwait(false))
                {
                    continue;
                }

                position = TargetPosition(now);
                if (seek)
                {
                    Log.Seek(_logger, position);
                    await RestartSourceAsync(options).ConfigureAwait(false);
                    continue;
                }

                // The due frame goes back to the pool whatever happens next (a decoding failure throws below).
                var due = TakeDueFrame(position, interval);
                bool isNewFrame;
                try
                {
                    if (IsDecoderLagging(position, now))
                    {
                        if (due is not null)
                        {
                            Interlocked.Increment(ref _framesDropped);
                        }

                        await RestartSourceAsync(options).ConfigureAwait(false);
                        continue;
                    }

                    isNewFrame = due is not null;
                    if (due is not null)
                    {
                        due.Rgb24.Span.CopyTo(_lastFrame);
                        _hasLastFrame = true;
                    }
                    else if (!_hasLastFrame || now - _lastSentAt < KeepAliveInterval)
                    {
                        continue;
                    }
                }
                finally
                {
                    due?.Dispose();
                }

                if (GetConnection(plan.Hyperion, now, token) is not { } connection)
                {
                    // (Re)connecting: this frame cannot be shown; the one due when the connection is up will be.
                    if (isNewFrame)
                    {
                        Interlocked.Increment(ref _framesDropped);
                    }

                    continue;
                }

                try
                {
                    await connection.SendImageAsync(_lastFrame, options.OutputWidth, options.OutputHeight, token).ConfigureAwait(false);
                }
                catch (HyperionConnectionException ex)
                {
                    _connection = null;
                    await connection.DisposeAsync().ConfigureAwait(false); // Already closed: returns at once.
                    if (isNewFrame)
                    {
                        Interlocked.Increment(ref _framesDropped);
                    }

                    OnConnectionFailed(ex, now);
                    continue;
                }

                _lastSentAt = now;
                if (isNewFrame)
                {
                    Interlocked.Increment(ref _framesSent);
                }

                if (_disconnectedSince is { } since)
                {
                    var downtime = Math.Round((now - since).TotalSeconds, 1); // Once per outage.
                    Log.Reconnected(_logger, _connectAttempts, downtime);
                }

                ResetReconnect();
            }
            finally
            {
                Interlocked.Increment(ref _ticks);
            }
        }
    }

    /// <summary>
    /// Returns the open connection; otherwise starts a connect when none is in flight and the backoff wait is over, and
    /// takes its result once it completed. Never waits.
    /// </summary>
    /// <returns>The connection, or <see langword="null"/> while (re)connecting.</returns>
    private IHyperionConnection? GetConnection(HyperionClientOptions options, DateTimeOffset now, CancellationToken token)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        if (_connecting is null)
        {
            if (now < _retryAt)
            {
                return null;
            }

            token.ThrowIfCancellationRequested();
            _connectAttempts++;
            _connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            _connecting = ConnectAsync(options, _connectCancellation.Token);
        }

        // A connect that finished synchronously or between ticks is used right away.
        var connecting = _connecting;
        if (!connecting.IsCompleted)
        {
            return null;
        }

        _connecting = null;
        _connectCancellation!.Dispose();
        _connectCancellation = null;
        if (connecting.IsCompletedSuccessfully)
        {
            _connection = connecting.Result;
            return _connection;
        }

        token.ThrowIfCancellationRequested();
        if (connecting.Exception?.InnerException is not { } failure
            || failure is not (HyperionConnectionException or HyperionProtocolException))
        {
            // Not Hyperion but a bug: end the session, logged as unexpected.
            connecting.GetAwaiter().GetResult();
            throw new InvalidOperationException("The Hyperion connect was cancelled.");
        }

        OnConnectionFailed(failure, now);
        return null;
    }

    /// <summary>Connects once the previous session released Hyperion, so two sessions never hold the same priority.</summary>
    private async Task<IHyperionConnection> ConnectAsync(HyperionClientOptions options, CancellationToken cancellationToken)
    {
        if (!_previousHyperionReleased.IsCompleted)
        {
            Log.WaitingForPreviousSession(_logger);
            try
            {
                await _previousHyperionReleased.WaitAsync(StopTimeout, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Its connection is stuck and Hyperion drops it by its own timeout; do not keep the lights off for it.
            }
        }

        return await _factory.Connect(options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Schedules the next attempt: 1 s after the first failure, then doubling, at most 30 s.</summary>
    private void OnConnectionFailed(Exception failure, DateTimeOffset now)
    {
        _failures++;
        if (_disconnectedSince is null)
        {
            _disconnectedSince = now;
            Log.Reconnecting(_logger, failure.Message);
        }

        var delay = MaxRetryDelay;
        if (_failures <= 5)
        {
            delay = FirstRetryDelay * (1 << (_failures - 1));
            delay = delay < MaxRetryDelay ? delay : MaxRetryDelay;
        }

        _retryAt = now + delay;
        Log.ConnectFailed(_logger, _connectAttempts, failure.Message, delay.TotalSeconds);
    }

    private void ResetReconnect()
    {
        _connectAttempts = 0;
        _failures = 0;
        _disconnectedSince = null;
        _retryAt = DateTimeOffset.MinValue;
    }

    /// <summary>Gives up a connect in flight: cancels it and closes its connection should it still be made.</summary>
    /// <returns>A task that completes once that connect finished and its connection, if any, was closed.</returns>
    private Task AbandonConnect()
    {
        if (_connecting is not { } connecting)
        {
            return Task.CompletedTask;
        }

        var cancellation = _connectCancellation!;
        _connecting = null;
        _connectCancellation = null;
        return CloseLateConnectionAsync(connecting, cancellation);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Runs unobserved after the session gave up the connect; a failure must be logged, not left as an unobserved task exception.")]
    private async Task CloseLateConnectionAsync(Task<IHyperionConnection> connecting, CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            var connection = await connecting.ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HyperionConnectionException or HyperionProtocolException)
        {
            // Expected: the connect was cancelled or failed.
        }
        catch (Exception ex)
        {
            Log.Unexpected(_logger, ex);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    /// <summary>The position to show now: the estimated playback position plus the light timing offset, never negative.</summary>
    private TimeSpan TargetPosition(DateTimeOffset now)
    {
        var position = _tracker.Estimate(now) + _latencyOffset;
        return position > TimeSpan.Zero ? position : TimeSpan.Zero;
    }

    /// <summary>
    /// Releases FFmpeg and Hyperion once a pause lasts longer than the configured time, and starts decoding again at the
    /// playback position on resume.
    /// </summary>
    /// <returns><see langword="true"/> while released: the tick has nothing to do.</returns>
    private async Task<bool> HandlePauseAsync(PlaybackState state, Plan plan, DateTimeOffset now)
    {
        if (!state.IsPaused)
        {
            _pausedSince = null;
            if (_releasedForPause)
            {
                _releasedForPause = false;
                var position = TargetPosition(now);
                Log.ResumedAfterRelease(_logger, position);
                StartSource(plan.Frames, position, now);
            }

            return false;
        }

        _pausedSince ??= now;
        if (_releasedForPause)
        {
            return true;
        }

        if (plan.PauseRelease <= TimeSpan.Zero || now - _pausedSince.Value < plan.PauseRelease)
        {
            return false;
        }

        // The connection closes (Hyperion clears the priority and shows its default); a new one opens with the next frame,
        // without a backoff wait. While released no attempts are made.
        _releasedForPause = true;
        _hasLastFrame = false;
        await ReleaseAsync().ConfigureAwait(false);
        Log.ReleasedForPause(_logger, plan.PauseRelease.TotalSeconds);
        return true;
    }

    /// <summary>Feeds a new report to the position tracker.</summary>
    /// <returns>Whether it moved the estimated position by more than <see cref="SeekThreshold"/>: a seek.</returns>
    private bool ApplyReport(PlaybackState state)
    {
        if (ReferenceEquals(state, _appliedState))
        {
            return false;
        }

        _appliedState = state;
        var moved = _tracker.Apply(state);
        Log.Report(_logger, state.Position, state.IsPositionReported, state.IsPaused, moved, _tracker.Uncertainty);
        return moved.Duration() > SeekThreshold;
    }

    /// <summary>Takes the newest decoded frame at or before <paramref name="position"/>, dropping older ones.</summary>
    private VideoFrame? TakeDueFrame(TimeSpan position, TimeSpan interval)
    {
        VideoFrame? due = null;
        while (_pending is not null || _source!.TryReadFrame(out _pending))
        {
            _sourceHasFrames = true;
            if (_pending.Position > position)
            {
                break;
            }

            if (due is not null)
            {
                due.Dispose();
                Interlocked.Increment(ref _framesDropped);
            }

            due = _pending;
            _pending = null;
            _nextPosition = due.Position + interval;
        }

        return due;
    }

    /// <summary>Checks whether the decoder fell too far behind playback; rethrows a decoding failure.</summary>
    private bool IsDecoderLagging(TimeSpan position, DateTimeOffset now)
    {
        var source = _source!;
        if (_pending is null && source.Completion.IsCompleted)
        {
            // Throws the FrameSourceException when FFmpeg failed; otherwise the video has ended.
            source.Completion.GetAwaiter().GetResult();
            _sourceEnded = true;
        }

        // Only a decoder that delivers frames and had time to open and seek the file counts as lagging.
        var lag = position - (_pending?.Position ?? _nextPosition);
        if (_sourceEnded || !_sourceHasFrames || lag <= LagTolerance || now - _sourceStartedAt < RestartGrace)
        {
            return false;
        }

        Log.Lagging(_logger, lag, position);
        return true;
    }

    private async Task RestartSourceAsync(FfmpegFrameSourceOptions options)
    {
        Interlocked.Increment(ref _restarts);
        _pending?.Dispose();
        _pending = null;
        if (_source is { } previous)
        {
            _source = null;
            await previous.DisposeAsync().ConfigureAwait(false);
        }

        // Stopping FFmpeg takes a moment; start the new one at the position as of now.
        var now = _timeProvider.GetUtcNow();
        StartSource(options, TargetPosition(now), now);
    }

    private void StartSource(FfmpegFrameSourceOptions options, TimeSpan position, DateTimeOffset now)
    {
        _source = _factory.StartFrameSource(options with { StartPosition = position });
        _nextPosition = position;
        _sourceStartedAt = now;
        _sourceHasFrames = false;
        _sourceEnded = false;
    }

    /// <summary>
    /// Kills FFmpeg and clears Hyperion's priority at the same time; each is bounded by its own timeouts. A connect in
    /// flight is cancelled and not waited for; its connection is closed as soon as it is made.
    /// </summary>
    /// <param name="final">Whether the session ends: completes <see cref="HyperionReleased"/> once Hyperion is released.</param>
    private async Task ReleaseAsync(bool final = false)
    {
        var abandoned = AbandonConnect();
        ResetReconnect();
        var connection = _connection;
        _connection = null;
        _pending?.Dispose();
        _pending = null;
        var source = _source;
        _source = null;
        var stopDecoding = source is null ? Task.CompletedTask : source.DisposeAsync().AsTask();
        var clear = connection is null ? Task.CompletedTask : connection.DisposeAsync().AsTask();
        if (final)
        {
            _ = SignalHyperionReleasedAsync(Task.WhenAll(clear, abandoned));
        }

        await Task.WhenAll(stopDecoding, clear).ConfigureAwait(false);
    }

    private async Task SignalHyperionReleasedAsync(Task released)
    {
        await released.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _hyperionReleased.TrySetResult();
    }

    private sealed record Plan(HyperionClientOptions Hyperion, FfmpegFrameSourceOptions Frames, TimeSpan LatencyOffset, TimeSpan PauseRelease);

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Streaming {Width}x{Height} at {Fps} fps to Hyperion from {Position}")]
        public static partial void Streaming(ILogger logger, int width, int height, int fps, TimeSpan position);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Stopped streaming to Hyperion: {Sent} frames sent, {Dropped} dropped, {Restarts} decoder restarts")]
        public static partial void Stopped(ILogger logger, long sent, long dropped, long restarts);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "The lights do not follow item {ItemId}: {Reason}")]
        public static partial void Unsupported(ILogger logger, Guid itemId, string reason);

        [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The lights cannot follow playback: no Hyperion server is configured (Dashboard → Plugins → Hyperion Grabber)")]
        public static partial void NotConfigured(ILogger logger);

        [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "The lights cannot follow playback: {Problems}")]
        public static partial void InvalidSettings(ILogger logger, string problems);

        [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Streaming to Hyperion stopped; the lights follow the next playback again: {Reason}")]
        public static partial void StreamFailed(ILogger logger, string reason);

        [LoggerMessage(EventId = 7, Level = LogLevel.Error, Message = "Streaming to Hyperion failed unexpectedly")]
        public static partial void Unexpected(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Streaming did not stop within {Seconds} s; it finishes in the background")]
        public static partial void StopTimedOut(ILogger logger, double seconds);

        [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message = "Playback moved to {Position}; restarting decoding there")]
        public static partial void Seek(ILogger logger, TimeSpan position);

        [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "Decoding lags {Lag} behind playback; restarting it at {Position}")]
        public static partial void Lagging(ILogger logger, TimeSpan lag, TimeSpan position);

        [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "Playback paused: {Paused} at {Position}")]
        public static partial void PauseChanged(ILogger logger, bool paused, TimeSpan position);

        [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Paused for {Seconds} s: released Hyperion until playback resumes")]
        public static partial void ReleasedForPause(ILogger logger, double seconds);

        [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Playback resumed: streaming to Hyperion again from {Position}")]
        public static partial void ResumedAfterRelease(ILogger logger, TimeSpan position);

        [LoggerMessage(EventId = 14, Level = LogLevel.Debug, Message = "Position report {Position} (reported: {Reported}, paused: {Paused}) moved the estimate by {Moved}; uncertainty ±{Uncertainty}")]
        public static partial void Report(ILogger logger, TimeSpan position, bool reported, bool paused, TimeSpan moved, TimeSpan uncertainty);

        [LoggerMessage(EventId = 15, Level = LogLevel.Warning, Message = "Hyperion is unreachable; reconnecting every 1-30 s while decoding goes on: {Reason}")]
        public static partial void Reconnecting(ILogger logger, string reason);

        [LoggerMessage(EventId = 16, Level = LogLevel.Debug, Message = "Connecting to Hyperion failed (attempt {Attempt}): {Reason}; next attempt in {Seconds} s")]
        public static partial void ConnectFailed(ILogger logger, int attempt, string reason, double seconds);

        [LoggerMessage(EventId = 17, Level = LogLevel.Information, Message = "Reconnected to Hyperion after {Attempts} attempt(s) and {Seconds} s; streaming again")]
        public static partial void Reconnected(ILogger logger, int attempts, double seconds);

        [LoggerMessage(EventId = 18, Level = LogLevel.Debug, Message = "Waiting for the previous playback to release Hyperion before connecting")]
        public static partial void WaitingForPreviousSession(ILogger logger);
    }
}
