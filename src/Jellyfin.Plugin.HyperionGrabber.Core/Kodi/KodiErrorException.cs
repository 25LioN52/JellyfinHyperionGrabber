using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Kodi;

/// <summary>
/// Kodi answered a request with an error (for example because its player stopped); the connection is still usable.
/// </summary>
public class KodiErrorException : KodiException
{
    /// <summary>Initializes a new instance of the <see cref="KodiErrorException"/> class.</summary>
    public KodiErrorException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KodiErrorException"/> class.</summary>
    /// <param name="message">What Kodi said.</param>
    public KodiErrorException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KodiErrorException"/> class.</summary>
    /// <param name="message">What Kodi said.</param>
    /// <param name="innerException">The cause.</param>
    public KodiErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
