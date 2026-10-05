using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Jellyfin.Plugin.HyperionGrabber.Playback;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Playback;

public class PlaybackEventMapperTests
{
    internal static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Map_CopiesSessionItemAndPosition()
    {
        var args = Args("kodi-device");
        args.MediaSourceId = "source";
        args.PlaybackPositionTicks = TimeSpan.FromMinutes(12).Ticks;
        args.IsPaused = true;

        var playbackEvent = PlaybackEventMapper.Map(PlaybackEventKind.Progress, args);

        Assert.NotNull(playbackEvent);
        Assert.Equal(PlaybackEventKind.Progress, playbackEvent.Kind);
        Assert.Equal("session-kodi-device", playbackEvent.SessionId);
        Assert.Equal("kodi-device", playbackEvent.DeviceId);
        Assert.Equal("Living room", playbackEvent.DeviceName);
        Assert.Equal("Kodi", playbackEvent.Client);
        Assert.Equal(UserId, playbackEvent.UserId);
        Assert.Equal(ItemId, playbackEvent.ItemId);
        Assert.Equal("source", playbackEvent.MediaSourceId);
        Assert.Equal(TimeSpan.FromMinutes(12), playbackEvent.Position);
        Assert.True(playbackEvent.IsPaused);
    }

    [Fact]
    public void Map_WithoutPosition_LeavesPositionEmpty()
    {
        var playbackEvent = PlaybackEventMapper.Map(PlaybackEventKind.Started, Args("kodi-device"));

        Assert.Null(playbackEvent?.Position);
    }

    [Fact]
    public void Map_WithoutSession_ReturnsNull()
    {
        var args = new PlaybackProgressEventArgs { Item = new Movie { Id = ItemId }, DeviceId = "kodi-device" };

        Assert.Null(PlaybackEventMapper.Map(PlaybackEventKind.Started, args));
    }

    [Fact]
    public void Map_StartWithoutItem_ReturnsNull()
    {
        var args = Args("kodi-device");
        args.Item = null;

        Assert.Null(PlaybackEventMapper.Map(PlaybackEventKind.Started, args));
    }

    [Fact]
    public void Map_StopWithoutItem_StillMaps()
    {
        var args = new PlaybackStopEventArgs { Session = Session("kodi-device") };

        var playbackEvent = PlaybackEventMapper.Map(PlaybackEventKind.Stopped, args);

        Assert.Equal("session-kodi-device", playbackEvent?.SessionId);
    }

    internal static PlaybackProgressEventArgs Args(string deviceId) => new()
    {
        Session = Session(deviceId),
        Item = new Movie { Id = ItemId },
    };

    internal static SessionInfo Session(string deviceId) => new(Substitute.For<ISessionManager>(), NullLogger.Instance)
    {
        Id = "session-" + deviceId,
        DeviceId = deviceId,
        DeviceName = "Living room",
        Client = "Kodi",
        UserId = UserId,
    };
}
