using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

namespace Jellyfin.Plugin.HyperionGrabber.TestSupport;

/// <summary>
/// <see cref="IHyperionSink"/> that records frames in memory and can simulate a lost connection.
/// </summary>
public sealed class RecordingSink : IHyperionSink
{
    private int _clearCount;

    /// <summary>Gets the recorded frames (copies).</summary>
    public ConcurrentQueue<(byte[] Rgb24, int Width, int Height)> Frames { get; } = new();

    /// <summary>Gets the number of Clear calls.</summary>
    public int ClearCount => Volatile.Read(ref _clearCount);

    /// <summary>Gets or sets the frame number (1-based) after which sends fail with a connection error; 0 disables.</summary>
    public int FailAfterFrames { get; set; }

    /// <summary>Gets or sets a value indicating whether Clear fails with a connection error.</summary>
    public bool FailClear { get; set; }

    /// <inheritdoc />
    public ValueTask SendImageAsync(ReadOnlyMemory<byte> rgb24, int width, int height, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailAfterFrames > 0 && Frames.Count >= FailAfterFrames)
        {
            throw new HyperionConnectionException("Simulated connection loss.");
        }

        Frames.Enqueue((rgb24.ToArray(), width, height));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _clearCount);
        return FailClear ? throw new HyperionConnectionException("Simulated connection loss.") : ValueTask.CompletedTask;
    }
}
