using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Streaming;

/// <summary>
/// <see cref="IFrameSource"/> whose frames the test pushes. Frame <c>n</c> shows <c>start + n / fps</c> and its first
/// byte is <c>n</c>, so tests can tell which frame was sent.
/// </summary>
internal sealed class FakeFrameSource : IFrameSource
{
    private readonly Channel<VideoFrame> _pool = Channel.CreateUnbounded<VideoFrame>();
    private readonly ConcurrentQueue<VideoFrame> _ready = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _created;
    private int _pushed;
    private int _disposed;

    public FakeFrameSource(FfmpegFrameSourceOptions options) => Options = options;

    public FfmpegFrameSourceOptions Options { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Gets or sets a task that <see cref="DisposeAsync"/> waits for (simulates FFmpeg that will not exit).</summary>
    public Task DisposeGate { get; set; } = Task.CompletedTask;

    public Task Completion => _completion.Task;

    /// <summary>Gets the number of frames pushed and not yet returned to the pool.</summary>
    public int Outstanding => Volatile.Read(ref _created) - _pool.Reader.Count;

    /// <summary>Makes the next <paramref name="count"/> frames available.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The consumer disposes the frames, which returns them to the pool.")]
    public void Push(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!_pool.Reader.TryRead(out var frame))
            {
                frame = new VideoFrame(Options.OutputWidth, Options.OutputHeight, _pool.Writer);
                Interlocked.Increment(ref _created);
            }

            var index = _pushed++;
            frame.Buffer.Span.Clear();
            frame.Buffer.Span[0] = (byte)index;
            frame.Lease(index, Options.StartPosition + TimeSpan.FromTicks(index * TimeSpan.TicksPerSecond / Options.FramesPerSecond));
            _ready.Enqueue(frame);
        }
    }

    /// <summary>Ends the video after the pushed frames.</summary>
    public void End() => _completion.TrySetResult();

    /// <summary>Fails decoding.</summary>
    public void Fail(string message) => _completion.TrySetException(new FrameSourceException(message));

    public bool TryReadFrame([NotNullWhen(true)] out VideoFrame? frame) => _ready.TryDequeue(out frame);

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await DisposeGate;
        _completion.TrySetResult();
    }
}
