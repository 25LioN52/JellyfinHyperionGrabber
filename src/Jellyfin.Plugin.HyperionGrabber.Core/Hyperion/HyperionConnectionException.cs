using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

/// <summary>
/// Thrown when the TCP connection to Hyperion cannot be established, times out or is lost.
/// </summary>
public class HyperionConnectionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HyperionConnectionException"/> class.
    /// </summary>
    public HyperionConnectionException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperionConnectionException"/> class.
    /// </summary>
    /// <param name="message">Human-readable description, safe to show to an administrator.</param>
    public HyperionConnectionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperionConnectionException"/> class.
    /// </summary>
    /// <param name="message">Human-readable description, safe to show to an administrator.</param>
    /// <param name="innerException">The underlying socket or I/O error.</param>
    public HyperionConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
