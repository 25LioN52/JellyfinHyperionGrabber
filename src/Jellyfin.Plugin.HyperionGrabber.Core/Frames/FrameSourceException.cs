using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// FFmpeg could not be started or failed while decoding.
/// </summary>
public class FrameSourceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FrameSourceException"/> class.
    /// </summary>
    public FrameSourceException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameSourceException"/> class.
    /// </summary>
    /// <param name="message">What went wrong, including FFmpeg's last error output when available.</param>
    public FrameSourceException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameSourceException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying error.</param>
    public FrameSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
