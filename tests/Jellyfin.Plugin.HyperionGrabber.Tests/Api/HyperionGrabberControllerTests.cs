using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Api;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Api;

public class HyperionGrabberControllerTests
{
    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();
    private readonly HyperionGrabberController _controller;

    public HyperionGrabberControllerTests()
    {
        _controller = new(new HyperionConnectionTester(NullLogger<HyperionClient>.Instance, TimeProvider.System), _sessionManager);
    }

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

    [Fact]
    public void GetClients_ListsEachDeviceOnceMostRecentFirstAndUsersByName()
    {
        var alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var now = new DateTime(2026, 10, 5, 20, 0, 0, DateTimeKind.Utc);
        _sessionManager.Sessions.Returns(
        [
            Session("kodi", "Living room", "Kodi", bob, "bob", now.AddMinutes(-10)),
            Session("phone", "Pixel", "Jellyfin Android", alice, "alice", now.AddMinutes(-5)),
            Session("kodi", "Living room", "Kodi", alice, "alice", now),
            Session("anonymous", null, null, Guid.Empty, null, now.AddHours(-1)),
        ]);

        var response = Assert.IsType<PlaybackClientsResponse>(Assert.IsType<OkObjectResult>(_controller.GetClients().Result).Value);

        Assert.Equal(["kodi", "phone", "anonymous"], response.Devices.Select(d => d.Id));
        Assert.Equal(new PlaybackClientDevice("kodi", "Living room", "Kodi", "alice", now), response.Devices[0]);
        Assert.Equal("anonymous", response.Devices[2].Name);
        Assert.Equal([new PlaybackClientUser(alice, "alice"), new PlaybackClientUser(bob, "bob")], response.Users);
    }

    private static SessionInfo Session(string deviceId, string? deviceName, string? client, Guid userId, string? userName, DateTime lastActivity)
        => new(Substitute.For<ISessionManager>(), NullLogger.Instance)
        {
            Id = Guid.NewGuid().ToString("N"),
            DeviceId = deviceId,
            DeviceName = deviceName,
            Client = client,
            UserId = userId,
            UserName = userName,
            LastActivityDate = lastActivity,
        };

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
