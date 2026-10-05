using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Streaming;

/// <summary>
/// <see cref="IHyperionConnection"/> that records the first byte (the <see cref="FakeFrameSource"/> frame number) of
/// every image, can hold sends (a slow Hyperion) and fail them (a lost connection).
/// </summary>
internal sealed class RecordingConnection : IHyperionConnection
{
    private readonly ConcurrentQueue<int> _images = new();
    private int _disposed;

    public HyperionClientOptions? Options { get; set; }

    /// <summary>Gets the frame numbers of the images sent, oldest first.</summary>
    public IReadOnlyList<int> Images => _images.ToArray();

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Gets or sets a task every send waits for after recording its image.</summary>
    public Task SendGate { get; set; } = Task.CompletedTask;

    public bool FailSends { get; set; }

    public async ValueTask SendImageAsync(ReadOnlyMemory<byte> rgb24, int width, int height, CancellationToken cancellationToken)
    {
        if (FailSends || IsDisposed)
        {
            throw new HyperionConnectionException("Simulated connection loss.");
        }

        _images.Enqueue(rgb24.Span[0]);
        await SendGate.WaitAsync(cancellationToken);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => string.Join(", ", Images.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
