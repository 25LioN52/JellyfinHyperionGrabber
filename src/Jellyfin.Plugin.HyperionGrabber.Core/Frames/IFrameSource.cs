using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// The part of <see cref="FfmpegFrameSource"/> the streaming session uses; lets tests supply frames without FFmpeg.
/// </summary>
internal interface IFrameSource : IAsyncDisposable
{
    /// <summary>Gets a task that completes after the last frame was read, or faults with <see cref="FrameSourceException"/>.</summary>
    Task Completion { get; }

    /// <summary>Returns the next frame if one is ready, without waiting.</summary>
    /// <param name="frame">The frame; dispose it when done.</param>
    /// <returns>Whether a frame was ready.</returns>
    bool TryReadFrame([NotNullWhen(true)] out VideoFrame? frame);
}
