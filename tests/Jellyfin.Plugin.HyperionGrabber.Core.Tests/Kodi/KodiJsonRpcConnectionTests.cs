using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Kodi;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Kodi;

public sealed class KodiJsonRpcConnectionTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly FakeKodiServer _server = FakeKodiServer.Start();
    private readonly ConcurrentQueue<string> _notifications = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task CallAsync_ReturnsTheResult_ThroughSplitAnswersAndGluedNotifications()
    {
        _server.Time = new TimeSpan(0, 1, 2, 3, 456);
        await using var connection = await ConnectAsync();

        var result = await connection.CallAsync("Player.GetProperties", null, Timeout, TimeProvider.System, Ct);

        Assert.Equal(1, result.GetProperty("speed").GetInt32());
        Assert.Equal(456, result.GetProperty("time").GetProperty("milliseconds").GetInt32());
        await TestHelpers.WaitUntilAsync(() => _notifications.Contains("System.OnIdle"));
    }

    [Fact]
    public async Task CallAsync_KodiAnswersWithAnError_ThrowsKodiErrorException()
    {
        _server.HasVideoPlayer = false;
        await using var connection = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<KodiErrorException>(() => connection.CallAsync("Player.GetProperties", null, Timeout, TimeProvider.System, Ct));

        Assert.Contains("Failed to execute method.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notifications_ReachTheCallback()
    {
        await using var connection = await ConnectAsync();
        await connection.CallAsync("Player.GetActivePlayers", null, Timeout, TimeProvider.System, Ct); // Connected and served.

        await _server.NotifyAsync("Player.OnPause");

        await TestHelpers.WaitUntilAsync(() => _notifications.Contains("Player.OnPause"));
    }

    [Fact]
    public async Task ConnectionLost_FailsCallsWithKodiException()
    {
        await using var connection = await ConnectAsync();
        await connection.CallAsync("Player.GetActivePlayers", null, Timeout, TimeProvider.System, Ct);

        _server.DropConnections();

        // Whether the call is written before or after the client sees the close, it fails as a lost connection.
        var exception = await Assert.ThrowsAnyAsync<KodiException>(() => connection.CallAsync("Player.GetActivePlayers", null, Timeout, TimeProvider.System, Ct));
        Assert.IsNotType<KodiErrorException>(exception);
    }

    [Fact]
    public async Task ConnectAsync_NothingListens_ThrowsKodiException()
    {
        var port = TestHelpers.GetUnusedPort();

        var exception = await Assert.ThrowsAsync<KodiException>(
            () => KodiJsonRpcConnection.ConnectAsync("127.0.0.1", port, Timeout, _ => { }, TimeProvider.System, Ct));

        Assert.Contains($"127.0.0.1:{port}", exception.Message, StringComparison.Ordinal);
    }

    private Task<KodiJsonRpcConnection> ConnectAsync()
        => KodiJsonRpcConnection.ConnectAsync(_server.Host, _server.Port, Timeout, _notifications.Enqueue, TimeProvider.System, Ct);
}
