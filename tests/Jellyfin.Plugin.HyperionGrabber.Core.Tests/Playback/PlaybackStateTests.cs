using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Playback;

public class PlaybackStateTests
{
    private static readonly DateTimeOffset ReportedAt = new(2026, 10, 5, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EstimatePosition_AddsTheTimeSinceTheReport()
    {
        var state = State(paused: false);

        Assert.Equal(TimeSpan.FromSeconds(61.5), state.EstimatePosition(ReportedAt + TimeSpan.FromSeconds(1.5)));
    }

    [Fact]
    public void EstimatePosition_WhilePaused_IsTheReportedPosition()
    {
        var state = State(paused: true);

        Assert.Equal(TimeSpan.FromMinutes(1), state.EstimatePosition(ReportedAt + TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void EstimatePosition_BeforeTheReport_IsTheReportedPosition()
    {
        var state = State(paused: false);

        Assert.Equal(TimeSpan.FromMinutes(1), state.EstimatePosition(ReportedAt - TimeSpan.FromSeconds(1)));
    }

    private static PlaybackState State(bool paused) => new()
    {
        SessionId = "session",
        DeviceId = "kodi",
        ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Position = TimeSpan.FromMinutes(1),
        IsPaused = paused,
        ReportedAt = ReportedAt,
    };
}
