using System;
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
/// <para>Each tick estimates the playback position (last report plus the time since, unless paused) and sends the
/// newest decoded frame at or before it. Older due frames are dropped, so a slow Hyperion, network or decoder means
/// fewer frames, never a queue. The frame source pools its buffers; while none is free, FFmpeg blocks.</para>
/// <para>While no new frame is due (pause, end of the video, slow decoder) the last frame is resent every
/// <see cref="KeepAliveInterval"/>, because Hyperion.ng and HyperHDR close FlatBuffers connections that stay silent.</para>
/// <para>FFmpeg is restarted at the playback position when a report jumps more than <see cref="SeekThreshold"/> away
/// from the estimate (a seek), and when the decoder lags more than <see cref="LagTolerance"/> behind playback after
/// <see cref="RestartGrace"/>.</para>
/// <para>A failure (Hyperion unreachable, decoding error, lost connection) stops the stream and releases Hyperion's
/// priority; the session then idles until it is disposed.</para>
/// </remarks>
internal sealed partial class StreamingGrabSession : IGrabSession
{
    /// <summary>How often the last frame is resent while no new frame is due; Hyperion's idle timeout is 5 s by default.</summary>
    internal static readonly TimeSpan KeepAliveInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>A report this far from the estimated position is a seek; decoding restarts at the new position.</summary>
    internal static readonly TimeSpan SeekThreshold = TimeSpan.FromSeconds(1);

    /// <summary>How far the decoder may lag behind playback before it is restarted at the playback position.</summary>
    internal static readonly TimeSpan LagTolerance = TimeSpan.FromSeconds(2);

    /// <summary>Time a decoder gets after starting (opening and seeking the file) before lagging counts.</summary>
    internal static readonly TimeSpan RestartGrace = TimeSpan.FromSeconds(5);

    /// <summary>Longest time <see cref="DisposeAsync"/> waits for the session to release FFmpeg and Hyperion.</summary>
    internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly StreamingGrabSessionFactory _factory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource<bool> _streamingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    private StreamingGrabSession(StreamingGrabSessionFactory factory, PlaybackState state)
    {
        _factory = factory;
        _timeProvider = factory.TimeProvider;
        _logger = factory.SessionLogger;
        _state = state;
        _appliedState = state;
    }

    /// <summary>Gets a task that completes with <see langword="true"/> once frames are being paced, or with
    /// <see langword="false"/> when the session ended without streaming.</summary>
    internal Task<bool> StreamingStarted => _streamingStarted.Task;

    /// <summary>Gets the session's task; it completes when streaming stopped and FFmpeg and Hyperion were released.</summary>
    internal Task Completion => _run;

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
    /// <returns>The running session.</returns>
    internal static StreamingGrabSession Start(StreamingGrabSessionFactory factory, PlaybackState state)
    {
        var session = new StreamingGrabSession(factory, state);
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
        catch (Exception ex) when (ex is HyperionConnectionException or HyperionProtocolException or FrameSourceException)
        {
            Log.StreamFailed(_logger, ex.Message);
        }
        catch (Exception ex)
        {
            Log.Unexpected(_logger, ex);
        }
        finally
        {
            await ReleaseAsync().ConfigureAwait(false);
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

        var errors = hyperion.GetValidationErrors();
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

        return new Plan(hyperion, frames);
    }

    private async Task StreamAsync(Plan plan, CancellationToken token)
    {
        var options = plan.Frames;
        var interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / options.FramesPerSecond);
        var lastFrame = new byte[options.FrameLength];
        var hasLastFrame = false;
        var lastSentAt = DateTimeOffset.MinValue;

        using var timer = new PeriodicTimer(interval, _timeProvider);
        var now = _timeProvider.GetUtcNow();
        var position = Volatile.Read(ref _state).EstimatePosition(now);
        StartSource(options, position, now);
        Log.Streaming(_logger, options.OutputWidth, options.OutputHeight, options.FramesPerSecond, position);
        _streamingStarted.TrySetResult(true);

        // Per tick: no allocations of our own; the frame is copied into lastFrame so its pooled buffer returns at once.
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            try
            {
                now = _timeProvider.GetUtcNow();
                var state = Volatile.Read(ref _state);
                position = state.EstimatePosition(now);
                if (IsSeek(state, position, now))
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
                        due.Rgb24.Span.CopyTo(lastFrame);
                        hasLastFrame = true;
                    }
                    else if (!hasLastFrame || now - lastSentAt < KeepAliveInterval)
                    {
                        continue;
                    }
                }
                finally
                {
                    due?.Dispose();
                }

                _connection ??= await _factory.Connect(plan.Hyperion, token).ConfigureAwait(false);
                await _connection.SendImageAsync(lastFrame, options.OutputWidth, options.OutputHeight, token).ConfigureAwait(false);
                lastSentAt = now;
                if (isNewFrame)
                {
                    Interlocked.Increment(ref _framesSent);
                }
            }
            finally
            {
                Interlocked.Increment(ref _ticks);
            }
        }
    }

    /// <summary>Checks whether a new report moved the position by more than <see cref="SeekThreshold"/>.</summary>
    private bool IsSeek(PlaybackState state, TimeSpan position, DateTimeOffset now)
    {
        if (ReferenceEquals(state, _appliedState))
        {
            return false;
        }

        var previous = _appliedState.EstimatePosition(now);
        _appliedState = state;
        return (position - previous).Duration() > SeekThreshold;
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
        StartSource(options, Volatile.Read(ref _state).EstimatePosition(now), now);
    }

    private void StartSource(FfmpegFrameSourceOptions options, TimeSpan position, DateTimeOffset now)
    {
        _source = _factory.StartFrameSource(options with { StartPosition = position });
        _nextPosition = position;
        _sourceStartedAt = now;
        _sourceHasFrames = false;
        _sourceEnded = false;
    }

    /// <summary>Kills FFmpeg and clears Hyperion's priority at the same time; each is bounded by its own timeouts.</summary>
    private async Task ReleaseAsync()
    {
        var connection = _connection;
        _connection = null;
        _pending?.Dispose();
        _pending = null;
        var source = _source;
        _source = null;
        var stopDecoding = source is null ? Task.CompletedTask : source.DisposeAsync().AsTask();
        var clear = connection is null ? Task.CompletedTask : connection.DisposeAsync().AsTask();
        await Task.WhenAll(stopDecoding, clear).ConfigureAwait(false);
    }

    private sealed record Plan(HyperionClientOptions Hyperion, FfmpegFrameSourceOptions Frames);

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
    }
}
