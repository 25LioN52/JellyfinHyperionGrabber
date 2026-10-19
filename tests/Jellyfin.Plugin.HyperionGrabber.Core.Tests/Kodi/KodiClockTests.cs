using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Kodi;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Kodi;

/// <summary>
/// The clock against a fake Kodi on loopback, with fake time for polling and retries.
/// </summary>
/// <remarks>Request timeouts also run on the fake time, so time only moves once the clock waits with its timer armed
/// (<see cref="KodiClock.Waits"/>, <see cref="KodiClock.Retries"/>), never while a request is on the way.</remarks>
public sealed class KodiClockTests : IAsyncDisposable
{
    private static readonly Guid ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 11, 20, 0, 0, TimeSpan.Zero));
    private readonly FakeKodiServer _server = FakeKodiServer.Start();
    private readonly FakeLogger<KodiClock> _logger = new();
    private KodiClock? _clock;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_clock is not null)
        {
            await _clock.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    [Fact]
    public async Task ReadsThePositionAndThePauseFromKodi()
    {
        _server.Time = new TimeSpan(0, 0, 12, 34, 567);

        var clock = Start();

        await TestHelpers.WaitUntilAsync(() => clock.Latest is not null);
        var reading = clock.Latest!;
        Assert.Equal(new TimeSpan(0, 0, 12, 34, 567), reading.Position);
        Assert.False(reading.IsPaused);
        Assert.Equal(_time.GetUtcNow(), reading.At);
        Assert.Equal(KodiClock.ReadingTolerance, reading.Uncertainty); // The fake clock does not move during the call.
        Assert.Equal($"Kodi's clock at 127.0.0.1:{_server.Port}.", clock.Description);
    }

    [Fact]
    public async Task ReadsAgainRightAfterKodiAnnouncesAPause()
    {
        var clock = Start();
        await TestHelpers.WaitUntilAsync(() => clock.Latest is not null);
        _server.Time = TimeSpan.FromMinutes(3);
        _server.Speed = 0;

        await _server.NotifyAsync("Player.OnPause");

        await TestHelpers.WaitUntilAsync(() => clock.Latest is { IsPaused: true } reading && reading.Position == TimeSpan.FromMinutes(3));
    }

    [Fact]
    public async Task ReadsAgainEveryTwoSeconds()
    {
        var clock = Start();
        await TestHelpers.WaitUntilAsync(() => clock.Latest is not null && clock.Waits >= 1);
        _server.Time = TimeSpan.FromMinutes(7);

        _time.Advance(KodiClock.PollInterval);

        await TestHelpers.WaitUntilAsync(() => clock.Latest?.Position == TimeSpan.FromMinutes(7));
    }

    [Fact]
    public async Task KodiPlaysAnotherItem_StopsWithoutReadings()
    {
        _server.File = "http://jellyfin:8096/Videos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/stream?static=true";

        var clock = Start();

        await clock.Completion.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(clock.Latest);
        Assert.Contains("plays another item", clock.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KodiPlaysThisItem_IsRead()
    {
        _server.File = "http://jellyfin:8096/Videos/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/stream?static=true";

        var clock = Start();

        await TestHelpers.WaitUntilAsync(() => clock.Latest is not null);
    }

    [Fact]
    public async Task NoVideoPlayer_WaitsUntilKodiStartsOne()
    {
        _server.HasVideoPlayer = false;
        var clock = Start();
        await TestHelpers.WaitUntilAsync(() => clock.Description.Contains("waiting for its video player", StringComparison.Ordinal));

        _server.HasVideoPlayer = true;
        await _server.NotifyAsync("Player.OnAVStart");

        await TestHelpers.WaitUntilAsync(() => clock.Latest is not null);
    }

    [Fact]
    public async Task KodiNotReachable_SaysHowToTurnOnRemoteControl()
    {
        var clock = KodiClock.Start(new KodiClockOptions("127.0.0.1", TestHelpers.GetUnusedPort(), ItemId), _time, _logger);
        _clock = clock;

        // The clock sets its description, then logs, then counts the retry: wait for the count, not the description.
        await TestHelpers.WaitUntilAsync(() => clock.Retries >= 1);

        Assert.Null(clock.Latest);
        Assert.Contains("not reachable", clock.Description, StringComparison.Ordinal);
        Assert.Contains("Allow remote control from applications on other systems", clock.Description, StringComparison.Ordinal);
        var logged = Assert.Single(_logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Information);
        Assert.Contains("does not accept remote control", logged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionLost_ReconnectsAfterTheRetryInterval()
    {
        var clock = Start();
        await TestHelpers.WaitUntilAsync(() => clock.Latest is not null && clock.Waits >= 1);
        _server.DropConnections();

        _time.Advance(KodiClock.PollInterval); // The next reading finds the connection closed.
        await TestHelpers.WaitUntilAsync(() => clock.Retries >= 1);
        Assert.Null(clock.Latest);
        Assert.Contains("Lost the connection", clock.Description, StringComparison.Ordinal);
        Assert.Contains(_logger.Collector.GetSnapshot(), r => r.Message.Contains("Lost the connection to Kodi", StringComparison.Ordinal));

        _time.Advance(KodiClock.RetryInterval); // The clock connects again.

        await TestHelpers.WaitUntilAsync(() => clock.Latest is not null);
        Assert.Equal(2, _server.ConnectionCount);
    }

    [Fact]
    public async Task DisposeAsync_EndsTheClock()
    {
        var clock = Start();
        await TestHelpers.WaitUntilAsync(() => clock.Latest is not null);

        await clock.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.True(clock.Completion.IsCompleted);
    }

    private KodiClock Start()
    {
        _clock = KodiClock.Start(new KodiClockOptions(_server.Host, _server.Port, ItemId), _time, _logger);
        return _clock;
    }
}
