using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Api;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Api;

public class HyperionGrabberControllerTests
{
    private readonly HyperionGrabberController _controller = new(new HyperionConnectionTester(NullLogger<HyperionClient>.Instance, TimeProvider.System));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Controller_RequiresAdministrator()
    {
        var authorize = typeof(HyperionGrabberController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal("RequiresElevation", authorize?.Policy);
    }

    [Fact]
    public async Task TestConnection_ReachableServer_ReturnsSuccess()
    {
        await using var server = FakeHyperionServer.Start();

        var response = Unwrap<OkObjectResult>(await _controller.TestConnection(Target(server), Ct));

        Assert.True(response.Success, response.Message);
        Assert.IsType<RegisterRequest>(await server.NextRequestAsync());
    }

    [Fact]
    public async Task TestConnection_UnreachableServer_ReturnsFailureWithMessage()
    {
        var request = new HyperionTargetRequest { Host = "127.0.0.1", Port = GetUnusedPort() };

        var response = Unwrap<OkObjectResult>(await _controller.TestConnection(request, Ct));

        Assert.False(response.Success);
        Assert.NotEmpty(response.Message);
    }

    [Theory]
    [InlineData(null, 19400, 150, "host")]
    [InlineData("hyperion", 0, 150, "Port")]
    [InlineData("hyperion", 19400, 50, "Priority")]
    public async Task TestConnection_InvalidInput_ReturnsBadRequest(string? host, int port, int priority, string expected)
    {
        var request = new HyperionTargetRequest { Host = host, Port = port, Priority = priority };

        var response = Unwrap<BadRequestObjectResult>(await _controller.TestConnection(request, Ct));

        Assert.False(response.Success);
        Assert.Contains(expected, response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestPattern_PlaysThenClears()
    {
        await using var server = FakeHyperionServer.Start();
        var request = Target(server);
        request.DurationSeconds = 1;

        var response = Unwrap<OkObjectResult>(await _controller.TestPattern(request, Ct));

        Assert.True(response.Success, response.Message);
        Assert.Equal(new TestPatternOptions().FramesPerSecond, response.FramesSent);
        Assert.IsType<RegisterRequest>(await server.NextRequestAsync());
        for (var frame = 0; frame < response.FramesSent; frame++)
        {
            Assert.IsType<ImageRequest>(await server.NextRequestAsync());
        }

        Assert.IsType<ClearRequest>(await server.NextRequestAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(600)]
    public async Task TestPattern_InvalidDuration_ReturnsBadRequest(int seconds)
    {
        var request = new HyperionTargetRequest { Host = "hyperion", DurationSeconds = seconds };

        var response = Unwrap<BadRequestObjectResult>(await _controller.TestPattern(request, Ct));

        Assert.Contains("duration", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HyperionTargetRequest Target(FakeHyperionServer server) => new() { Host = server.Host, Port = server.Port };

    private static HyperionTestResponse Unwrap<TResult>(ActionResult<HyperionTestResponse> result)
        where TResult : ObjectResult
        => Assert.IsType<HyperionTestResponse>(Assert.IsType<TResult>(result.Result).Value);

    private static int GetUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
