using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Playback;

public sealed class PlaybackMonitorTests : IAsyncDisposable
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Movie = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Episode = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly PlaybackTarget LivingRoom = Target("Living room", "192.168.1.20", "kodi");
    private static readonly PlaybackTarget Bedroom = Target("Bedroom", "192.168.1.21", "shield");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 20, 0, 0, TimeSpan.Zero));
    private readonly RecordingGrabSessionFactory _factory = new();
    private readonly PlaybackMonitor _monitor;
    private long _expectedReports;

    public PlaybackMonitorTests()
    {
        _monitor = new PlaybackMonitor(_factory, _time, NullLogger<PlaybackMonitor>.Instance);
        _monitor.Start();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync() => await _monitor.DisposeAsync();

    [Fact]
    public async Task Started_OnSelectedDevice_StartsSessionWithItsState()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));

        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi", position: TimeSpan.FromMinutes(5)) with { MediaSourceId = "source", UserId = Alice });

        var session = Assert.Single(_factory.Sessions);
        Assert.Equal("s1", session.Initial.SessionId);
        Assert.Equal("kodi", session.Initial.DeviceId);
        Assert.Equal(Alice, session.Initial.UserId);
        Assert.Equal(Movie, session.Initial.ItemId);
        Assert.Equal("source", session.Initial.MediaSourceId);
        Assert.Equal(TimeSpan.FromMinutes(5), session.Initial.Position);
        Assert.True(session.Initial.IsStart);
        Assert.Equal(_time.GetUtcNow(), session.Initial.ReportedAt);
    }

    [Fact]
    public async Task Started_OnOtherDevice_IsIgnored()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));

        await PostAsync(Event(PlaybackEventKind.Started, "s1", "phone"));

        Assert.Empty(_factory.Sessions);
    }

    [Fact]
    public async Task Started_WhenFilterDisabled_IsIgnored()
    {
        await SetFilterAsync(TestFilters.OneTarget(false, ["kodi"], []));

        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        Assert.Empty(_factory.Sessions);
    }

    [Fact]
    public async Task Started_BeforeAnyFilter_IsIgnored()
    {
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        Assert.Empty(_factory.Sessions);
    }

    [Fact]
    public async Task Started_ByUnselectedUser_IsIgnored()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], [Alice]));

        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi") with { UserId = Bob });
        await PostAsync(Event(PlaybackEventKind.Started, "s2", "kodi") with { UserId = Alice });

        Assert.Equal("s2", Assert.Single(_factory.Sessions).Initial.SessionId);
    }

    [Fact]
    public async Task Progress_ForwardsPositionAndPauseToTheSession()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        _time.Advance(TimeSpan.FromSeconds(30));
        await PostAsync(Event(PlaybackEventKind.Progress, "s1", "kodi", position: TimeSpan.FromSeconds(30)) with { IsPaused = true });

        var session = Assert.Single(_factory.Sessions);
        var update = Assert.Single(session.Updates);
        Assert.Equal(TimeSpan.FromSeconds(30), update.Position);
        Assert.True(update.IsPaused);
        Assert.Equal(_time.GetUtcNow(), update.ReportedAt);
    }

    [Fact]
    public async Task Progress_WithoutPosition_KeepsTheEstimatedPosition()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi", position: TimeSpan.FromMinutes(5)));

        // A report without a position must not look like a seek to the start.
        _time.Advance(TimeSpan.FromSeconds(10));
        await PostAsync(Event(PlaybackEventKind.Progress, "s1", "kodi") with { IsPaused = true });

        var update = Assert.Single(Assert.Single(_factory.Sessions).Updates);
        Assert.Equal(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(10), update.Position);
        Assert.False(update.IsPositionReported);
        Assert.True(update.IsPaused);
        Assert.Equal(_time.GetUtcNow(), update.ReportedAt);
    }

    [Fact]
    public async Task GetStatus_WithoutPlayback_ReportsTheFilter()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi", "shield"], [Alice]));

        var status = _monitor.GetStatus();

        Assert.True(status.FollowsPlayback);
        Assert.Equal((1, 2), (status.UserCount, status.DeviceCount));
        Assert.Equal(new TargetStatus("TV", PlaybackTargetKey.None, 2, null, null), Assert.Single(status.Targets));
    }

    [Fact]
    public async Task GetStatus_WhilePlaying_ReportsThePlaybackAndItsSession()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));
        var session = Assert.Single(_factory.Sessions);
        session.Status = new GrabSessionStatus { Phase = GrabSessionPhase.Paused, FramesSent = 42 };

        var playback = Assert.Single(_monitor.GetStatus().Targets).Playback;

        Assert.NotNull(playback);
        Assert.Equal(("KODI", "Kodi", Movie, _time.GetUtcNow()), (playback.DeviceName, playback.Client, playback.ItemId, playback.Since));
        Assert.Same(session.Status, playback.Session);
    }

    [Fact]
    public async Task GetStatus_AfterTheSessionStopped_KeepsItsLastProblem()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));
        var problem = new ProblemReport(_time.GetUtcNow(), "Hyperion is unreachable: refused.");
        _factory.Sessions[0].Status = new GrabSessionStatus { Phase = GrabSessionPhase.Streaming, LastProblem = problem };

        await PostAsync(Event(PlaybackEventKind.Stopped, "s1", "kodi"));

        var status = Assert.Single(_monitor.GetStatus().Targets);
        Assert.Null(status.Playback);
        Assert.Same(problem, status.LastProblem);
    }

    [Fact]
    public async Task GetStatus_WhenASessionCannotStart_SaysWhy()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        _factory.FailStart = true;

        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        var problem = Assert.Single(_monitor.GetStatus().Targets).LastProblem;
        Assert.Equal("Could not start the lights for playback on KODI: Simulated session failure.", problem?.Message);
        Assert.Equal(_time.GetUtcNow(), problem?.Time);
    }

    [Fact]
    public async Task Stopped_DisposesTheSession()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        await PostAsync(Event(PlaybackEventKind.Stopped, "s1", "kodi"));

        Assert.True(Assert.Single(_factory.Sessions).IsDisposed);
    }

    [Fact]
    public async Task Progress_ForUnknownSession_StartsSession()
    {
        // Playback that was already running when Jellyfin or the plugin started.
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));

        await PostAsync(Event(PlaybackEventKind.Progress, "s1", "kodi", position: TimeSpan.FromMinutes(42)));

        var initial = Assert.Single(_factory.Sessions).Initial;
        Assert.Equal(TimeSpan.FromMinutes(42), initial.Position);
        Assert.False(initial.IsStart); // A clock reading with the client's usual precision, not a requested start.
    }

    [Fact]
    public async Task Progress_WithDifferentItem_RestartsSession()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        await PostAsync(Event(PlaybackEventKind.Progress, "s1", "kodi") with { ItemId = Episode });

        Assert.Equal(2, _factory.Sessions.Count);
        Assert.True(_factory.Sessions[0].IsDisposed);
        Assert.Equal(Episode, Assert.Single(_factory.Running).Initial.ItemId);
    }

    [Fact]
    public async Task Started_OnSecondSelectedDevice_TakesOverAndHandsBackWhenStopped()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi", "shield"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));
        await PostAsync(Event(PlaybackEventKind.Progress, "s1", "kodi", position: TimeSpan.FromMinutes(1)));

        await PostAsync(Event(PlaybackEventKind.Started, "s2", "shield"));
        Assert.Equal("s2", Assert.Single(_factory.Running).Initial.SessionId);

        await PostAsync(Event(PlaybackEventKind.Progress, "s1", "kodi", position: TimeSpan.FromMinutes(2)));
        Assert.Equal("s2", Assert.Single(_factory.Running).Initial.SessionId);

        await PostAsync(Event(PlaybackEventKind.Stopped, "s2", "shield"));
        var resumed = Assert.Single(_factory.Running);
        Assert.Equal("s1", resumed.Initial.SessionId);
        Assert.Equal(TimeSpan.FromMinutes(2), resumed.Initial.Position);
        Assert.Equal(3, _factory.Sessions.Count);
    }

    [Fact]
    public async Task UpdateFilter_RemovingThePlayingDevice_StopsSession()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        await SetFilterAsync(TestFilters.OneTarget(true, ["shield"], []));

        Assert.True(Assert.Single(_factory.Sessions).IsDisposed);
    }

    [Fact]
    public async Task UpdateFilter_Disabling_StopsSession()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        await SetFilterAsync(PlaybackFilter.Disabled);

        Assert.Empty(_factory.Running);
    }

    [Fact]
    public async Task UpdateFilter_SelectingADeviceThatIsPlaying_StartsSession()
    {
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi", position: TimeSpan.FromMinutes(3)));

        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));

        Assert.Equal(TimeSpan.FromMinutes(3), Assert.Single(_factory.Running).Initial.Position);
    }

    [Fact]
    public async Task Started_WhenFactoryFails_KeepsProcessingAndRetriesOnNextReport()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        _factory.FailStart = true;
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));
        Assert.Empty(_factory.Sessions);

        _factory.FailStart = false;
        await PostAsync(Event(PlaybackEventKind.Progress, "s1", "kodi", position: TimeSpan.FromSeconds(30)));

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(_factory.Running).Initial.Position);
    }

    [Fact]
    public async Task StopAsync_DisposesRunningSessionAndIgnoresLaterReports()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        await _monitor.StopAsync(Ct);
        _monitor.Post(Event(PlaybackEventKind.Started, "s2", "kodi"));

        Assert.True(Assert.Single(_factory.Sessions).IsDisposed);
    }

    [Fact]
    public async Task ManyPlaybacks_TracksAtMostTheLimit()
    {
        await SetFilterAsync(TestFilters.OneTarget(true, ["kodi"], []));
        await PostAsync(Event(PlaybackEventKind.Started, "kodi-session", "kodi"));

        // Lost stop events must not grow the state forever: the least recently reported playback is forgotten.
        for (var i = 0; i < PlaybackMonitor.MaxTrackedPlaybacks; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await PostAsync(Event(PlaybackEventKind.Started, $"other-{i}", "phone"));
        }

        Assert.True(Assert.Single(_factory.Sessions).IsDisposed);
    }

    [Fact]
    public async Task TwoTargets_FollowTheirOwnDevicesIndependently()
    {
        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom, Bedroom], []));

        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));
        await PostAsync(Event(PlaybackEventKind.Started, "s2", "shield"));
        await PostAsync(Event(PlaybackEventKind.Progress, "s1", "kodi", position: TimeSpan.FromMinutes(1)));

        // A movie starting on the bedroom TV does not take the living room's lights.
        Assert.Equal([("s1", LivingRoom), ("s2", Bedroom)], _factory.Running.Select(s => (s.Initial.SessionId, s.Target)));
        Assert.Single(_factory.Sessions[0].Updates);
        Assert.Empty(_factory.Sessions[1].Updates);

        await PostAsync(Event(PlaybackEventKind.Stopped, "s1", "kodi"));

        Assert.Equal(["s2"], _factory.Running.Select(s => s.Initial.SessionId));
    }

    [Fact]
    public async Task DeviceSelectedForTwoTargets_DrivesBoth()
    {
        var hue = Target("Hue room", "192.168.1.22", "kodi");
        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom, hue], []));

        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));
        await PostAsync(Event(PlaybackEventKind.Progress, "s1", "kodi", position: TimeSpan.FromMinutes(1)) with { IsPaused = true });

        Assert.Equal([LivingRoom, hue], _factory.Running.Select(s => s.Target));
        Assert.All(_factory.Running, session => Assert.True(Assert.Single(session.Updates).IsPaused));
    }

    [Fact]
    public async Task Stopped_PlaybackOnTwoTargets_StopsBothSessionsAtTheSameTime()
    {
        var hue = Target("Hue room", "192.168.1.22", "kodi");
        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom, hue], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));
        var slowStop = new TaskCompletionSource();
        foreach (var session in _factory.Sessions)
        {
            session.DisposeGate = slowStop.Task;
        }

        _monitor.Post(Event(PlaybackEventKind.Stopped, "s1", "kodi"));

        // Both stops have begun while neither has finished: one slow Hyperion does not delay the other target.
        await TestHelpers.WaitUntilAsync(() => _factory.Sessions.All(s => s.IsDisposeStarted));
        Assert.DoesNotContain(_factory.Sessions, s => s.IsDisposed);
        slowStop.SetResult();
        await WaitForProcessingAsync();
        Assert.Empty(_factory.Running);
    }

    [Fact]
    public async Task UpdateFilter_ChangingATargetsServer_RestartsOnlyThatTargetsSession()
    {
        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom, Bedroom], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));
        await PostAsync(Event(PlaybackEventKind.Started, "s2", "shield"));
        var moved = Target("Bedroom", "192.168.1.31", "shield");

        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom, moved], []));

        Assert.Equal(3, _factory.Sessions.Count);
        Assert.False(_factory.Sessions[0].IsDisposed);
        Assert.True(_factory.Sessions[1].IsDisposed);
        Assert.Equal(("s2", moved), (_factory.Sessions[2].Initial.SessionId, _factory.Sessions[2].Target));
    }

    [Fact]
    public async Task UpdateFilter_RenamingATarget_KeepsItsSession()
    {
        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));

        await SetFilterAsync(PlaybackFilter.Create(true, [Target("Lounge", "192.168.1.20", "kodi")], []));

        Assert.Single(_factory.Running);
        Assert.Equal("Lounge", Assert.Single(_monitor.GetStatus().Targets).Name);
    }

    [Fact]
    public async Task UpdateFilter_RemovingATarget_StopsItsSessionAndForgetsItsProblem()
    {
        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom, Bedroom], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s1", "kodi"));
        await PostAsync(Event(PlaybackEventKind.Started, "s2", "shield"));
        _factory.Sessions[1].Status = new GrabSessionStatus { Phase = GrabSessionPhase.Streaming, LastProblem = new ProblemReport(_time.GetUtcNow(), "refused") };

        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom], []));
        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom, Bedroom], []));

        Assert.True(_factory.Sessions[1].IsDisposed);
        Assert.False(_factory.Sessions[0].IsDisposed);
        Assert.Null(_monitor.GetStatus().Targets[1].LastProblem);
    }

    [Fact]
    public async Task GetStatus_ReportsEachTargetInOrder()
    {
        await SetFilterAsync(PlaybackFilter.Create(true, [LivingRoom, Bedroom], []));
        await PostAsync(Event(PlaybackEventKind.Started, "s2", "shield"));
        var problem = new ProblemReport(_time.GetUtcNow(), "Hyperion is unreachable: refused.");
        _factory.Sessions[0].Status = new GrabSessionStatus { Phase = GrabSessionPhase.Reconnecting, LastProblem = problem };

        var targets = _monitor.GetStatus().Targets;

        Assert.Equal(["Living room", "Bedroom"], targets.Select(t => t.Name));
        Assert.Equal(new PlaybackTargetKey("192.168.1.21", 19400, 150), targets[1].Key);
        Assert.Null(targets[0].Playback);
        Assert.Null(targets[0].LastProblem);
        Assert.Equal("SHIELD", targets[1].Playback?.DeviceName);
        Assert.Same(problem, targets[1].LastProblem);
    }

    [Fact]
    public void Start_Twice_Throws()
    {
        Assert.Throws<InvalidOperationException>(_monitor.Start);
    }

    [Fact]
    public async Task UnexpectedFailure_IsLoggedAsCritical()
    {
        // Logging is the only code outside the guarded session calls that a test can make throw.
        var logger = new ThrowingLogger(throwOnEventId: 6); // PlaybackStarted
        var monitor = new PlaybackMonitor(_factory, _time, logger);
        monitor.Start();
        monitor.UpdateFilter(TestFilters.OneTarget(true, ["kodi"], []));

        monitor.Post(Event(PlaybackEventKind.Started, "s1", "kodi"));

        await TestHelpers.WaitUntilAsync(() => logger.CriticalLogged);
        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.StopAsync(Ct));
    }

    private static PlaybackEvent Event(PlaybackEventKind kind, string sessionId, string deviceId, TimeSpan? position = null) => new()
    {
        Kind = kind,
        SessionId = sessionId,
        DeviceId = deviceId,
        DeviceName = deviceId.ToUpperInvariant(),
        Client = "Kodi",
        ItemId = Movie,
        Position = position,
    };

    private static PlaybackTarget Target(string name, string host, params string[] deviceIds)
        => PlaybackTarget.Create(name, new HyperionClientOptions { Host = host }, deviceIds);

    private Task PostAsync(PlaybackEvent playbackEvent)
    {
        _monitor.Post(playbackEvent);
        return WaitForProcessingAsync();
    }

    private Task SetFilterAsync(PlaybackFilter filter)
    {
        _monitor.UpdateFilter(filter);
        return WaitForProcessingAsync();
    }

    private Task WaitForProcessingAsync()
    {
        var expected = ++_expectedReports;
        return TestHelpers.WaitUntilAsync(() => _monitor.ProcessedReports >= expected);
    }

    private sealed class ThrowingLogger(int throwOnEventId) : ILogger<PlaybackMonitor>
    {
        private volatile bool _criticalLogged;

        public bool CriticalLogged => _criticalLogged;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Critical)
            {
                _criticalLogged = true;
                return;
            }

            if (eventId.Id == throwOnEventId)
            {
                throw new InvalidOperationException("Simulated failure outside a session call.");
            }
        }
    }
}
