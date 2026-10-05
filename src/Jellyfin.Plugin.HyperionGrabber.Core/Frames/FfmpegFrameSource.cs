using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// Decodes a video with FFmpeg into small RGB24 frames at a constant frame rate.
/// </summary>
/// <remarks>
/// <para>
/// Frames come from a fixed pool of <see cref="FfmpegFrameSourceOptions.BufferCount"/> buffers. While every buffer
/// is held by the consumer, the source stops reading FFmpeg's stdout, the pipe fills up and FFmpeg blocks: it never
/// decodes ahead unboundedly, and pausing is just not reading.
/// </para>
/// <para>
/// When hardware decoding fails before the first frame (no device, unsupported codec or profile), the source restarts
/// FFmpeg on the CPU. The FFmpeg process is always killed and awaited on end of stream, error and disposal.
/// </para>
/// </remarks>
public sealed partial class FfmpegFrameSource : IFrameSource
{
    private const int StderrLinesKept = 8;
    private const int MaxStderrLineLength = 400;
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(5);

    private readonly FfmpegFrameSourceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Channel<VideoFrame> _free;
    private readonly Channel<VideoFrame> _ready;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _processLock = new();
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Borrowed reference to the process owned (and disposed) by RunProcessAsync; only used to kill it.")]
    private Process? _process;
    private Task _run = Task.CompletedTask;
    private long _framesRead;
    private int _activeAcceleration;
    private int _disposed;

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing a VideoFrame returns it to the pool; the pool channel owns the frames.")]
    private FfmpegFrameSource(FfmpegFrameSourceOptions options, TimeProvider timeProvider, ILogger logger)
    {
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _activeAcceleration = (int)options.HardwareAcceleration;
        _free = Channel.CreateBounded<VideoFrame>(new BoundedChannelOptions(options.BufferCount) { SingleReader = true });
        _ready = Channel.CreateBounded<VideoFrame>(new BoundedChannelOptions(options.BufferCount) { SingleReader = true, SingleWriter = true });
        for (var i = 0; i < options.BufferCount; i++)
        {
            _free.Writer.TryWrite(new VideoFrame(options.OutputWidth, options.OutputHeight, _free.Writer));
        }
    }

    /// <summary>Gets the frame width in pixels.</summary>
    public int Width => _options.OutputWidth;

    /// <summary>Gets the frame height in pixels.</summary>
    public int Height => _options.OutputHeight;

    /// <summary>Gets the constant frame rate.</summary>
    public int FramesPerSecond => _options.FramesPerSecond;

    /// <summary>Gets the acceleration in use; <see cref="HardwareAcceleration.None"/> after a fallback to the CPU.</summary>
    public HardwareAcceleration ActiveAcceleration => (HardwareAcceleration)Volatile.Read(ref _activeAcceleration);

    /// <summary>
    /// Gets a task that completes once every frame was read after the end of the video or disposal, and faults with
    /// <see cref="FrameSourceException"/> when FFmpeg could not be started or failed.
    /// </summary>
    public Task Completion => _ready.Reader.Completion;

    /// <summary>Gets the number of frames read from FFmpeg so far.</summary>
    internal long FramesRead => Interlocked.Read(ref _framesRead);

    /// <summary>Gets the id of the running FFmpeg process, if any.</summary>
    internal int? ProcessId
    {
        get
        {
            lock (_processLock)
            {
                return _process?.Id;
            }
        }
    }

    /// <summary>
    /// Starts FFmpeg in the background. Problems starting or decoding surface from <see cref="ReadFrameAsync"/>.
    /// </summary>
    /// <param name="options">What to decode and how.</param>
    /// <param name="timeProvider">Clock for process exit timeouts.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The running source; dispose it to stop FFmpeg.</returns>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    public static FfmpegFrameSource Start(FfmpegFrameSourceOptions options, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        var errors = options.GetValidationErrors();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        }

        var source = new FfmpegFrameSource(options, timeProvider, logger);
        source._run = Task.Run(source.RunAsync);
        return source;
    }

    /// <summary>
    /// Waits for the next frame. Only one consumer may read at a time. Dispose each frame when done with it.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting (FFmpeg keeps running).</param>
    /// <returns>The next frame, or <see langword="null"/> at the end of the video or after disposal.</returns>
    /// <exception cref="FrameSourceException">FFmpeg could not be started or failed.</exception>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<VideoFrame?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var reader = _ready.Reader;
        while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.TryRead(out var frame))
            {
                return frame;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the next frame if one is ready, without waiting. Only one consumer may read at a time. Dispose each
    /// frame when done with it.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <returns>Whether a frame was ready; check <see cref="Completion"/> to tell the end of the video from a wait.</returns>
    public bool TryReadFrame([NotNullWhen(true)] out VideoFrame? frame) => _ready.Reader.TryRead(out frame);

    /// <summary>
    /// Stops FFmpeg (killing it if needed) and waits until it has exited.
    /// </summary>
    /// <returns>A task that completes when the process is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        lock (_processLock)
        {
            // Unblocks a pipe read that does not observe cancellation (synchronous pipes on Windows).
            if (_process is not null)
            {
                Kill(_process);
            }
        }

        await _run.ConfigureAwait(false);
        _stopping.Dispose();
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Exiting right now; WaitForExitAsync below still bounds the wait.
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Top level of the background task: any failure must reach the consumer as a FrameSourceException instead of looking like the end of the video, and DisposeAsync must not rethrow it.")]
    private async Task RunAsync()
    {
        var token = _stopping.Token;
        Exception? error = null;
        try
        {
            var acceleration = _options.HardwareAcceleration;
            while (true)
            {
                var attempt = await RunProcessAsync(acceleration, token).ConfigureAwait(false);
                if (attempt is null)
                {
                    break;
                }

                if (acceleration != HardwareAcceleration.None && FramesRead == 0)
                {
                    Log.HardwareFallback(_logger, acceleration, attempt.Message);
                    acceleration = HardwareAcceleration.None;
                    Volatile.Write(ref _activeAcceleration, (int)acceleration);
                    continue;
                }

                throw attempt;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Disposed.
        }
        catch (FrameSourceException ex)
        {
            Log.Failed(_logger, ex.Message);
            error = ex;
        }
        catch (IOException ex)
        {
            Log.Failed(_logger, ex.Message);
            error = new FrameSourceException("Reading frames from FFmpeg failed: " + ex.Message, ex);
        }
        catch (Exception ex)
        {
            Log.Unexpected(_logger, ex);
            error = new FrameSourceException("Decoding failed unexpectedly: " + ex.Message, ex);
        }
        finally
        {
            _ready.Writer.TryComplete(error);
        }
    }

    /// <returns><see langword="null"/> when FFmpeg reached the end of the video, otherwise the failure.</returns>
    private async Task<FrameSourceException?> RunProcessAsync(HardwareAcceleration acceleration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(_options.FfmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in FfmpegArguments.Build(_options, acceleration, OperatingSystem.IsWindows()))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Log.Command(_logger, _options.FfmpegPath, new CommandLine(startInfo.ArgumentList));

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new FrameSourceException($"FFmpeg could not be started from '{_options.FfmpegPath}': {ex.Message}", ex);
        }

        lock (_processLock)
        {
            _process = process;
        }

        Log.Started(_logger, process.Id, _options.StartPosition, acceleration, Width, Height, FramesPerSecond);
        var stderr = new Queue<string>(StderrLinesKept);
        var stderrDrained = DrainStderrAsync(process.StandardError, stderr);
        var exited = false;
        try
        {
            if (token.IsCancellationRequested)
            {
                // Disposed while starting, after DisposeAsync looked for a process to kill.
                Kill(process);
            }

            await ReadFramesAsync(process.StandardOutput.BaseStream, token).ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            lock (_processLock)
            {
                _process = null;
            }

            exited = await EnsureExitedAsync(process).ConfigureAwait(false);
            if (exited)
            {
                // Completes once the process is gone: its stderr pipe is closed.
                await stderrDrained.ConfigureAwait(false);
            }
        }

        if (!exited)
        {
            return new FrameSourceException("FFmpeg did not exit after being stopped.");
        }

        Log.Exited(_logger, process.Id, process.ExitCode);
        if (process.ExitCode == 0)
        {
            return null;
        }

        string details;
        lock (stderr)
        {
            details = stderr.Count == 0 ? "no error output" : string.Join(" | ", stderr);
        }

        return new FrameSourceException($"FFmpeg exited with code {process.ExitCode} ({details}).");
    }

    private async Task ReadFramesAsync(Stream stdout, CancellationToken token)
    {
        var length = _options.FrameLength;
        while (true)
        {
            // Waiting here while all buffers are in use is what throttles FFmpeg.
            var frame = await _free.Reader.ReadAsync(token).ConfigureAwait(false);

            // Read inline rather than with Stream.ReadAtLeastAsync, whose own state machine would allocate per frame.
            var buffer = frame.Buffer;
            var read = 0;
            while (read < length)
            {
                var count = await stdout.ReadAsync(buffer[read..], token).ConfigureAwait(false);
                if (count == 0)
                {
                    // End of stream; a partial frame means FFmpeg was cut off.
                    _free.Writer.TryWrite(frame);
                    return;
                }

                read += count;
            }

            var index = Interlocked.Increment(ref _framesRead) - 1;
            frame.Lease(index, _options.StartPosition + TimeSpan.FromTicks(index * TimeSpan.TicksPerSecond / _options.FramesPerSecond));
            await _ready.Writer.WriteAsync(frame, token).ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureExitedAsync(Process process)
    {
        if (!process.HasExited)
        {
            Kill(process);
        }

        using var timeout = new CancellationTokenSource(ExitTimeout, _timeProvider);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.ExitTimeout(_logger, process.Id);
            return false;
        }
    }

    private async Task DrainStderrAsync(StreamReader stderr, Queue<string> lastLines)
    {
        // FFmpeg blocks when stderr is not read; keep only the last lines for error messages.
        while (await stderr.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.Length > MaxStderrLineLength)
            {
                line = line[..MaxStderrLineLength];
            }

            Log.Stderr(_logger, line);
            lock (lastLines)
            {
                if (lastLines.Count == StderrLinesKept)
                {
                    lastLines.Dequeue();
                }

                lastLines.Enqueue(line);
            }
        }
    }

    /// <summary>Formats the argument list only when the log message is actually written.</summary>
    private readonly record struct CommandLine(IList<string> Arguments)
    {
        public override string ToString() => string.Join(' ', Arguments);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Started FFmpeg (pid {ProcessId}) at {Position} with hardware acceleration {Acceleration}, {Width}x{Height} at {Fps} fps")]
        public static partial void Started(ILogger logger, int processId, TimeSpan position, HardwareAcceleration acceleration, int width, int height, int fps);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Hardware decoding with {Acceleration} failed, decoding on the CPU instead: {Reason}")]
        public static partial void HardwareFallback(ILogger logger, HardwareAcceleration acceleration, string reason);

        [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Decoding failed: {Reason}")]
        public static partial void Failed(ILogger logger, string reason);

        [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "FFmpeg command: {Path} {Arguments}")]
        public static partial void Command(ILogger logger, string path, CommandLine arguments);

        [LoggerMessage(EventId = 5, Level = LogLevel.Debug, Message = "FFmpeg (pid {ProcessId}) exited with code {ExitCode}")]
        public static partial void Exited(ILogger logger, int processId, int exitCode);

        [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "FFmpeg (pid {ProcessId}) did not exit after being killed")]
        public static partial void ExitTimeout(ILogger logger, int processId);

        [LoggerMessage(EventId = 7, Level = LogLevel.Trace, Message = "FFmpeg: {Line}")]
        public static partial void Stderr(ILogger logger, string line);

        [LoggerMessage(EventId = 8, Level = LogLevel.Error, Message = "Decoding failed unexpectedly")]
        public static partial void Unexpected(ILogger logger, Exception exception);
    }
}
