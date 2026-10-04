using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Diagnostics;

public class HyperionConnectionTesterTests
{
    private static readonly HyperionConnectionTester Tester = new(NullLogger<HyperionClient>.Instance, TimeProvider.System);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TestConnectionAsync_ReportsSuccessAndReleasesThePriority()
    {
        await using var server = FakeHyperionServer.Start();

        var result = await Tester.TestConnectionAsync(OptionsFor(server), Ct);

        Assert.True(result.Success, result.Message);
        Assert.Contains("registered priority 150", result.Message, StringComparison.Ordinal);
        Assert.IsType<RegisterRequest>(await server.NextRequestAsync());
        Assert.IsType<ClearRequest>(await server.NextRequestAsync());
    }

    [Fact]
    public async Task TestConnectionAsync_ReportsUnreachableServer()
    {
        var port = TestHelpers.GetUnusedPort();

        var result = await Tester.TestConnectionAsync(new HyperionClientOptions { Host = "127.0.0.1", Port = port }, Ct);

        Assert.False(result.Success);
        Assert.Contains(port.ToString(CultureInfo.InvariantCulture), result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectionAsync_ReportsRejection()
    {
        await using var server = FakeHyperionServer.Start(RegistrationMode.Reject, "not allowed");

        var result = await Tester.TestConnectionAsync(OptionsFor(server), Ct);

        Assert.False(result.Success);
        Assert.Contains("not allowed", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendTestPatternAsync_StreamsFramesThenClears()
    {
        await using var server = FakeHyperionServer.Start();
        var pattern = new TestPatternOptions { FramesPerSecond = 25, Duration = TimeSpan.FromMilliseconds(200) };

        var result = await Tester.SendTestPatternAsync(OptionsFor(server), pattern, Ct);

        Assert.True(result.Success, result.Message);
        Assert.Equal(5, result.FramesSent);
        Assert.IsType<RegisterRequest>(await server.NextRequestAsync());
        for (var frame = 0; frame < 5; frame++)
        {
            var image = await server.NextRequestAsync<ImageRequest>();
            Assert.Equal((pattern.Width, pattern.Height), (image.Width, image.Height));
        }

        Assert.IsType<ClearRequest>(await server.NextRequestAsync());
    }

    [Fact]
    public async Task SendTestPatternAsync_WhenCancelled_ReturnsResultAndClears()
    {
        await using var server = FakeHyperionServer.Start();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var pattern = new TestPatternOptions { Duration = TimeSpan.FromSeconds(30) };

        var run = Tester.SendTestPatternAsync(OptionsFor(server), pattern, cancellation.Token);
        await server.NextRequestAsync<RegisterRequest>();
        await server.NextRequestAsync<ImageRequest>();
        await cancellation.CancelAsync();
        var result = await run;

        Assert.False(result.Success);
        Assert.Equal("Cancelled.", result.Message);
        ReceivedRequest request;
        do
        {
            request = await server.NextRequestAsync();
        }
        while (request is ImageRequest);
        Assert.IsType<ClearRequest>(request);
    }

    private static HyperionClientOptions OptionsFor(FakeHyperionServer server) => new() { Host = server.Host, Port = server.Port };
}
