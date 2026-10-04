using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Hyperion;

public class HyperionClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConnectAsync_RegistersOriginAndPriority()
    {
        await using var server = FakeHyperionServer.Start();

        await using var client = await ConnectAsync(server, priority: 120);

        var register = await server.NextRequestAsync<RegisterRequest>();
        Assert.Equal(HyperionDefaults.Origin, register.Origin);
        Assert.Equal(120, register.Priority);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WhenServerRejectsRegistration_ThrowsWithServerMessage()
    {
        await using var server = FakeHyperionServer.Start(RegistrationMode.Reject, "priority out of range");

        var exception = await Assert.ThrowsAsync<HyperionProtocolException>(() => ConnectAsync(server));

        Assert.Contains("priority out of range", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_WhenServerNeverConfirms_TimesOutWithPortHint()
    {
        await using var server = FakeHyperionServer.Start(RegistrationMode.Silent);
        var options = OptionsFor(server) with { ReplyTimeout = TimeSpan.FromMilliseconds(200) };

        var exception = await Assert.ThrowsAsync<HyperionConnectionException>(
            () => HyperionClient.ConnectAsync(options, NullLogger.Instance, Ct));

        Assert.Contains("FlatBuffers port", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_WhenNothingListens_ThrowsConnectionException()
    {
        var port = TestHelpers.GetUnusedPort();

        var exception = await Assert.ThrowsAsync<HyperionConnectionException>(
            () => HyperionClient.ConnectAsync(new HyperionClientOptions { Host = "127.0.0.1", Port = port }, NullLogger.Instance, Ct));

        Assert.Contains(port.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_WithInvalidOptions_ThrowsBeforeConnecting()
    {
        var options = new HyperionClientOptions { Host = "127.0.0.1", Priority = 99 };

        await Assert.ThrowsAsync<ArgumentException>(() => HyperionClient.ConnectAsync(options, NullLogger.Instance, Ct));
    }

    [Fact]
    public async Task SendImageAsync_DeliversPixelsUnchanged()
    {
        await using var server = FakeHyperionServer.Start();
        await using var client = await ConnectAsync(server);
        await server.NextRequestAsync<RegisterRequest>();
        var pixels = Enumerable.Range(0, 4 * 3 * 3).Select(i => (byte)i).ToArray();

        await client.SendImageAsync(pixels, 4, 3, Ct);

        var image = await server.NextRequestAsync<ImageRequest>();
        Assert.Equal(4, image.Width);
        Assert.Equal(3, image.Height);
        Assert.Equal(pixels, image.Data);
        Assert.Equal(HyperionDefaults.InfiniteDuration, image.Duration);
    }

    [Fact]
    public async Task SendImageAsync_StreamOfFrames_ArrivesInOrder()
    {
        await using var server = FakeHyperionServer.Start();
        await using var client = await ConnectAsync(server);
        await server.NextRequestAsync<RegisterRequest>();
        var frame = new byte[64 * 36 * 3];

        for (var index = 0; index < 100; index++)
        {
            Array.Fill(frame, (byte)index);
            await client.SendImageAsync(frame, 64, 36, Ct);
        }

        for (var index = 0; index < 100; index++)
        {
            var image = await server.NextRequestAsync<ImageRequest>();
            Assert.All(image.Data, value => Assert.Equal((byte)index, value));
        }

        Assert.True(client.IsConnected);
        Assert.Null(client.LastServerError);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(2, 2, 11)]
    [InlineData(1921, 1, 1921 * 3)]
    public async Task SendImageAsync_RejectsInvalidImages(int width, int height, int length)
    {
        await using var server = FakeHyperionServer.Start();
        await using var client = await ConnectAsync(server);

        await Assert.ThrowsAsync<ArgumentException>(async () => await client.SendImageAsync(new byte[length], width, height, Ct));
    }

    [Fact]
    public async Task SendColorAsync_SendsPackedRgb()
    {
        await using var server = FakeHyperionServer.Start();
        await using var client = await ConnectAsync(server);
        await server.NextRequestAsync<RegisterRequest>();

        await client.SendColorAsync(0x11, 0x22, 0x33, Ct);

        Assert.Equal(new ColorRequest(0x112233, HyperionDefaults.InfiniteDuration), await server.NextRequestAsync());
    }

    [Fact]
    public async Task DisposeAsync_ClearsOwnPriority()
    {
        await using var server = FakeHyperionServer.Start();
        var client = await ConnectAsync(server, priority: 130);
        await server.NextRequestAsync<RegisterRequest>();

        await client.DisposeAsync();

        Assert.Equal(new ClearRequest(130), await server.NextRequestAsync());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_WhileOtherTasksSend_FailsThemOnlyWithConnectionException()
    {
        await using var server = FakeHyperionServer.Start();
        var client = await ConnectAsync(server);
        await server.NextRequestAsync<RegisterRequest>();
        var frame = new byte[64 * 36 * 3];
        var senders = Enumerable.Range(0, 8).Select(_ => Task.Run(
            async () =>
            {
                for (var index = 0; index < 500; index++)
                {
                    await client.SendImageAsync(frame, 64, 36, Ct);
                }
            },
            Ct)).ToArray();
        await server.NextRequestAsync<ImageRequest>(); // sending is under way

        await client.DisposeAsync();

        foreach (var sender in senders)
        {
            var exception = await Record.ExceptionAsync(() => sender);
            Assert.True(exception is null or HyperionConnectionException, exception?.ToString());
        }

        await Assert.ThrowsAsync<HyperionConnectionException>(async () => await client.SendImageAsync(frame, 64, 36, Ct));
    }

    [Fact]
    public async Task SendImageAsync_WithCancelledToken_KeepsTheConnectionUsable()
    {
        await using var server = FakeHyperionServer.Start();
        await using var client = await ConnectAsync(server);
        await server.NextRequestAsync<RegisterRequest>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.SendImageAsync(new byte[3], 1, 1, cancelled.Token));

        Assert.True(client.IsConnected);
        await client.SendImageAsync(new byte[3], 1, 1, Ct);
        Assert.IsType<ImageRequest>(await server.NextRequestAsync());
    }

    [Fact]
    public async Task WhenServerDropsConnection_ClientReportsDisconnectedAndSendsFail()
    {
        await using var server = FakeHyperionServer.Start();
        await using var client = await ConnectAsync(server);
        await server.NextRequestAsync<RegisterRequest>();

        server.DropConnections();
        await TestHelpers.WaitUntilAsync(() => !client.IsConnected);

        await Assert.ThrowsAsync<HyperionConnectionException>(async () => await client.SendImageAsync(new byte[3], 1, 1, Ct));
    }

    private static HyperionClientOptions OptionsFor(FakeHyperionServer server, int priority = HyperionDefaults.Priority) => new()
    {
        Host = server.Host,
        Port = server.Port,
        Priority = priority,
        ReplyTimeout = TimeSpan.FromSeconds(2),
    };

    private static Task<HyperionClient> ConnectAsync(FakeHyperionServer server, int priority = HyperionDefaults.Priority)
        => HyperionClient.ConnectAsync(OptionsFor(server, priority), NullLogger.Instance, Ct);
}
