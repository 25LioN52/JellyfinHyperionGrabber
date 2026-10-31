using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Helpers;

/// <summary>
/// The tests for unreachable servers rely on <see cref="ClosedPort"/> refusing connections on every OS CI runs on.
/// </summary>
public class ClosedPortTests
{
    [Fact]
    public async Task Connecting_WhileReserved_IsRefused()
    {
        using var closed = ClosedPort.Reserve();
        using var client = new TcpClient();

        var exception = await Assert.ThrowsAnyAsync<SocketException>(
            () => client.ConnectAsync(IPAddress.Loopback, closed.Port, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(SocketError.ConnectionRefused, exception.SocketErrorCode);
    }
}
