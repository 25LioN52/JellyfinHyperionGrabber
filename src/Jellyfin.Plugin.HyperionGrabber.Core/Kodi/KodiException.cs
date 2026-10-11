using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Kodi;

/// <summary>
/// Kodi could not be reached, the connection was lost, or Kodi sent something unusable.
/// </summary>
public class KodiException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="KodiException"/> class.</summary>
    public KodiException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KodiException"/> class.</summary>
    /// <param name="message">What happened.</param>
    public KodiException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KodiException"/> class.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">The cause.</param>
    public KodiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
