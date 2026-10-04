using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

/// <summary>
/// Thrown when Hyperion rejects a request or sends data that does not follow the FlatBuffers protocol.
/// </summary>
public class HyperionProtocolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HyperionProtocolException"/> class.
    /// </summary>
    public HyperionProtocolException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperionProtocolException"/> class.
    /// </summary>
    /// <param name="message">The server's error text or a description of the malformed data.</param>
    public HyperionProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperionProtocolException"/> class.
    /// </summary>
    /// <param name="message">The server's error text or a description of the malformed data.</param>
    /// <param name="innerException">The underlying error.</param>
    public HyperionProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
