using System;
using System.Net;
using System.Net.Sockets;

namespace Jellyfin.Plugin.HyperionGrabber.TestSupport;

/// <summary>
/// A loopback port on which nothing listens, reserved until disposed: its socket is bound but never listens, so
/// connections to it are refused.
/// </summary>
/// <remarks>
/// A port that is looked up and released at once can be taken before the test connects to it: by another test's server
/// asking for any free port (parallel test modules, other test runs on the machine), or as the source port of the
/// test's own connection, which then connects to itself. While the socket holds the port, the operating system hands it
/// out for neither. (Tests never bind fixed ports, so a socket asking for this exact port is not a concern.)
/// </remarks>
public sealed class ClosedPort : IDisposable
{
    private readonly Socket _socket;

    private ClosedPort(Socket socket)
    {
        _socket = socket;
        Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <summary>Gets the port number.</summary>
    public int Port { get; }

    /// <summary>Reserves a free loopback port on which nothing listens.</summary>
    /// <returns>The reserved port; dispose it after the test.</returns>
    public static ClosedPort Reserve()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return new ClosedPort(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _socket.Dispose();
}
