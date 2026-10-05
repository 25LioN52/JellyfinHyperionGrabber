using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Jellyfin.Plugin.HyperionGrabber.Playback;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Playback;

public sealed class PlaybackMonitorServiceTests : IDisposable
{
    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();
    private readonly RecordingGrabSessionFactory _factory = new();
    private readonly FakeFilterSource _filterSource = new();
    private readonly PlaybackMonitorService _service;

    public PlaybackMonitorServiceTests()
    {
        _service = new PlaybackMonitorService(_sessionManager, _factory, _filterSource, TimeProvider.System, NullLoggerFactory.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task PlaybackStart_OnSelectedDevice_StartsSession()
    {
        _filterSource.Filter = PlaybackFilter.Create(true, ["kodi-device"], []);
        await _service.StartAsync(Ct);

        _sessionManager.PlaybackStart += Raise.EventWith(_sessionManager, PlaybackEventMapperTests.Args("kodi-device"));

        await TestHelpers.WaitUntilAsync(() => _factory.Sessions.Count == 1);
        Assert.Equal(PlaybackEventMapperTests.ItemId, _factory.Sessions[0].Initial.ItemId);
    }

    [Fact]
    public async Task PlaybackProgressAndStopped_ReachTheSession()
    {
        _filterSource.Filter = PlaybackFilter.Create(true, ["kodi-device"], []);
        await _service.StartAsync(Ct);
        _sessionManager.PlaybackStart += Raise.EventWith(_sessionManager, PlaybackEventMapperTests.Args("kodi-device"));
        var progress = PlaybackEventMapperTests.Args("kodi-device");
        progress.PlaybackPositionTicks = TimeSpan.FromSeconds(30).Ticks;

        _sessionManager.PlaybackProgress += Raise.EventWith(_sessionManager, progress);
        _sessionManager.PlaybackStopped += Raise.EventWith(_sessionManager, new PlaybackStopEventArgs { Session = PlaybackEventMapperTests.Session("kodi-device") });

        await TestHelpers.WaitUntilAsync(() => _factory.Sessions.Count == 1 && _factory.Sessions[0].IsDisposed);
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(_factory.Sessions[0].Updates).Position);
    }

    [Fact]
    public async Task ConfigurationChange_AppliesTheNewFilter()
    {
        await _service.StartAsync(Ct);
        _sessionManager.PlaybackStart += Raise.EventWith(_sessionManager, PlaybackEventMapperTests.Args("kodi-device"));

        _filterSource.Filter = PlaybackFilter.Create(true, ["kodi-device"], []);
        _filterSource.RaiseChanged();

        await TestHelpers.WaitUntilAsync(() => _factory.Running.Count == 1);
    }

    [Fact]
    public async Task StopAsync_UnsubscribesAndStopsTheSession()
    {
        _filterSource.Filter = PlaybackFilter.Create(true, ["kodi-device"], []);
        await _service.StartAsync(Ct);
        _sessionManager.PlaybackStart += Raise.EventWith(_sessionManager, PlaybackEventMapperTests.Args("kodi-device"));
        await TestHelpers.WaitUntilAsync(() => _factory.Sessions.Count == 1);

        await _service.StopAsync(Ct);
        _sessionManager.PlaybackStart += Raise.EventWith(_sessionManager, PlaybackEventMapperTests.Args("kodi-device"));

        Assert.True(Assert.Single(_factory.Sessions).IsDisposed);
        Assert.False(_filterSource.HasSubscribers);
    }

    private sealed class FakeFilterSource : IPlaybackFilterSource
    {
        public event EventHandler? Changed;

        public PlaybackFilter Filter { get; set; } = PlaybackFilter.Disabled;

        public bool HasSubscribers => Changed is not null;

        public PlaybackFilter GetCurrent() => Filter;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
