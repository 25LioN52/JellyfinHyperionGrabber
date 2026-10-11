using System;
using System.Globalization;
using System.Net;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Kodi;

/// <summary>
/// Which Kodi to read the playback position from, and for which item.
/// </summary>
/// <param name="Host">Kodi's address, as the media server sees the client.</param>
/// <param name="Port">Kodi's JSON-RPC TCP port.</param>
/// <param name="ItemId">The item the playback is for.</param>
internal sealed record KodiClockOptions(string Host, int Port, Guid ItemId)
{
    /// <summary>Gets the address and port, for messages.</summary>
    public string Endpoint => string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}");

    /// <summary>Checks whether a client name is a Kodi add-on (Jellyfin for Kodi reports "Kodi", JellyCon "Kodi ...").</summary>
    /// <param name="client">The client application.</param>
    /// <returns>Whether the client runs in Kodi.</returns>
    public static bool IsKodi(string? client) => client?.Contains("Kodi", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Turns the media server's remote end point of a client into an address to connect to.</summary>
    /// <param name="remoteEndPoint">An IP address, optionally with a port.</param>
    /// <returns>The address, or <see langword="null"/> when it is not one.</returns>
    public static string? ParseAddress(string? remoteEndPoint)
    {
        if (string.IsNullOrWhiteSpace(remoteEndPoint))
        {
            return null;
        }

        IPAddress? address = null;
        if (IPAddress.TryParse(remoteEndPoint, out var parsed))
        {
            address = parsed;
        }
        else if (IPEndPoint.TryParse(remoteEndPoint, out var endPoint))
        {
            address = endPoint.Address;
        }

        if (address is null)
        {
            return null;
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }
}
