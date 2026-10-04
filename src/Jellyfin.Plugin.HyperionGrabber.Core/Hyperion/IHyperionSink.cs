using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

/// <summary>
/// Destination for frames produced by the grabber (a Hyperion connection, or a fake in tests).
/// </summary>
public interface IHyperionSink
{
    /// <summary>
    /// Sends one RGB24 image (3 bytes per pixel, row-major, no padding).
    /// </summary>
    /// <param name="rgb24">Pixel data; length must be <paramref name="width"/> * <paramref name="height"/> * 3.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the image has been written.</returns>
    ValueTask SendImageAsync(ReadOnlyMemory<byte> rgb24, int width, int height, CancellationToken cancellationToken);

    /// <summary>
    /// Releases this source's priority so Hyperion falls back to the next source (effect, other grabber, off).
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the request has been written.</returns>
    ValueTask ClearAsync(CancellationToken cancellationToken);
}
