using System;
using System.Threading;
using System.Threading.Channels;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// One decoded RGB24 frame from a fixed pool. Dispose it as soon as it has been sent: the buffer goes back to the
/// pool, and while every buffer is in use the frame source stops reading from FFmpeg.
/// </summary>
public sealed class VideoFrame : IDisposable
{
    private readonly ChannelWriter<VideoFrame> _pool;
    private readonly byte[] _buffer;
    private int _leased;

    internal VideoFrame(int width, int height, ChannelWriter<VideoFrame> pool)
    {
        Width = width;
        Height = height;
        _buffer = new byte[width * height * 3];
        _pool = pool;
    }

    /// <summary>Gets the frame width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the frame height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the frame number since the source started (0-based).</summary>
    public long Index { get; private set; }

    /// <summary>Gets the media position this frame shows.</summary>
    public TimeSpan Position { get; private set; }

    /// <summary>Gets the pixels: 3 bytes per pixel, row-major, no padding. Invalid after <see cref="Dispose"/>.</summary>
    public ReadOnlyMemory<byte> Rgb24 => _buffer;

    internal Memory<byte> Buffer => _buffer;

    /// <summary>
    /// Returns the buffer to the pool. Calling it more than once has no effect.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _leased, 0) == 1)
        {
            // Fails only after the source has been disposed; the frame is then simply dropped.
            _pool.TryWrite(this);
        }
    }

    internal void Lease(long index, TimeSpan position)
    {
        Index = index;
        Position = position;
        Volatile.Write(ref _leased, 1);
    }
}
