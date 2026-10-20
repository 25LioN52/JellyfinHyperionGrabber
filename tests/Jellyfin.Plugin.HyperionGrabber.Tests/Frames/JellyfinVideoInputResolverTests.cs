using System;
using System.Collections.Generic;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Frames;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Frames;

public class JellyfinVideoInputResolverTests
{
    private static readonly Guid ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ILibraryManager _libraryManager = Substitute.For<ILibraryManager>();
    private readonly IMediaSourceManager _mediaSourceManager = Substitute.For<IMediaSourceManager>();

    [Fact]
    public void Resolve_LocalVideoFile_PrefixesThePathWithTheFileProtocol()
    {
        var movie = new Movie { Id = ItemId };
        _libraryManager.GetItemById(ItemId).Returns(movie);
        _mediaSourceManager.GetStaticMediaSources(movie, false).Returns([Source("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "/media/Movie (2020)/movie.mkv")]);

        var result = Resolver().Resolve(ItemId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.True(result.IsSupported);
        Assert.Equal("file:/media/Movie (2020)/movie.mkv", result.Input.Path);
        Assert.Equal((3840, 1606, "hevc"), (result.Input.Width, result.Input.Height, result.Input.Codec));
    }

    [Fact]
    public void Resolve_PicksTheMediaSourceThatIsPlaying()
    {
        var movie = new Movie { Id = ItemId };
        _libraryManager.GetItemById(ItemId).Returns(movie);
        _mediaSourceManager.GetStaticMediaSources(movie, false).Returns(
        [
            Source("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "/media/movie - 4K.mkv"),
            Source("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "/media/movie - 1080p.mkv"),
        ]);

        // Clients may report the id with dashes.
        var result = Resolver().Resolve(ItemId, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        Assert.Equal("file:/media/movie - 1080p.mkv", result.Input?.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cccccccccccccccccccccccccccccccc")]
    public void Select_WithoutAMatchingId_UsesTheFirstSource(string? mediaSourceId)
    {
        var sources = new List<MediaSourceInfo> { Source("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "/a.mkv"), Source("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "/b.mkv") };

        Assert.Same(sources[0], JellyfinVideoInputResolver.Select(sources, mediaSourceId));
    }

    [Fact]
    public void Resolve_MissingItem_IsUnsupported()
    {
        var result = Resolver().Resolve(ItemId, null);

        Assert.False(result.IsSupported);
        Assert.Contains("not in the library", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_LiveTvChannel_IsUnsupportedWithoutOpeningTheStream()
    {
        _libraryManager.GetItemById(ItemId).Returns(new LiveTvChannel { Id = ItemId });

        var result = Resolver().Resolve(ItemId, null);

        Assert.False(result.IsSupported);
        Assert.Contains("Live TV", result.Reason, StringComparison.Ordinal);
        _mediaSourceManager.DidNotReceiveWithAnyArgs().GetStaticMediaSources(default!, default);
    }

    [Theory]
    [InlineData(MediaProtocol.Http)]
    [InlineData(MediaProtocol.Rtsp)]
    [InlineData(MediaProtocol.Udp)]
    public void Map_RemoteSource_IsUnsupported(MediaProtocol protocol)
    {
        var source = Source("a", "https://example.com/stream.m3u8");
        source.Protocol = protocol;

        var result = JellyfinVideoInputResolver.Map(source);

        Assert.False(result.IsSupported);
        Assert.Contains("not a local file", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_StrmFile_IsUnsupported()
    {
        // Jellyfin resolves .strm files to their target URL and marks the source as remote.
        var source = Source("a", "/media/movie.strm");
        source.IsRemote = true;

        Assert.False(JellyfinVideoInputResolver.Map(source).IsSupported);
    }

    [Fact]
    public void Map_InfiniteStream_IsUnsupported()
    {
        var source = Source("a", "/recordings/live.ts");
        source.IsInfiniteStream = true;

        var result = JellyfinVideoInputResolver.Map(source);

        Assert.Contains("live stream", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VideoType.BluRay)]
    [InlineData(VideoType.Dvd)]
    [InlineData(VideoType.Iso)]
    public void Map_DiscStructure_IsUnsupported(VideoType videoType)
    {
        var source = Source("a", "/media/disc");
        source.VideoType = videoType;

        var result = JellyfinVideoInputResolver.Map(source);

        Assert.Contains("not supported yet", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_WithoutVideoStreamSize_IsUnsupported()
    {
        var source = Source("a", "/media/movie.mkv");
        source.MediaStreams = [new MediaStream { Type = MediaStreamType.Audio, Codec = "ac3" }];

        var result = JellyfinVideoInputResolver.Map(source);

        Assert.Contains("no video stream", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_UsesTheFirstVideoStream()
    {
        var source = Source("a", "/media/movie.mkv");
        source.MediaStreams =
        [
            new MediaStream { Type = MediaStreamType.Audio, Codec = "truehd" },
            new MediaStream { Type = MediaStreamType.Video, Codec = "h264", Width = 1920, Height = 1080 },
            new MediaStream { Type = MediaStreamType.Video, Codec = "mjpeg", Width = 640, Height = 360 },
        ];

        var result = JellyfinVideoInputResolver.Map(source);

        Assert.Equal((1920, 1080, "h264"), (result.Input?.Width, result.Input?.Height, result.Input?.Codec));
    }

    [Theory]
    [InlineData(720, 576, "16:9", 1024, 576)] // PAL DVD, 16:9
    [InlineData(720, 480, "4:3", 640, 480)] // NTSC DVD, 4:3
    [InlineData(1440, 1080, "16:9", 1920, 1080)] // HDV
    [InlineData(1920, 1080, "2.40:1", 2592, 1080)] // Anamorphic scope in a 16:9 frame
    public void Map_AnamorphicVideo_UsesTheDisplaySize(int width, int height, string aspectRatio, int displayWidth, int displayHeight)
    {
        var source = Source("a", "/media/dvd.mkv");
        source.MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Codec = "mpeg2video", Width = width, Height = height, IsAnamorphic = true, AspectRatio = aspectRatio }];

        var input = JellyfinVideoInputResolver.Map(source).Input;

        Assert.Equal((displayWidth, displayHeight), (input?.Width, input?.Height));
    }

    [Theory]
    [InlineData(false, "16:9")] // 1920x1040 is reported as "16:9" (Jellyfin rounds within 3 %), but its pixels are square.
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "wide")]
    [InlineData(true, "16:0")]
    [InlineData(true, "-16:9")]
    [InlineData(true, "1:100")]
    public void Map_WithoutAUsableDisplayAspectRatio_UsesTheStoredSize(bool anamorphic, string? aspectRatio)
    {
        var source = Source("a", "/media/movie.mkv");
        source.MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Codec = "h264", Width = 1920, Height = 1040, IsAnamorphic = anamorphic, AspectRatio = aspectRatio }];

        var input = JellyfinVideoInputResolver.Map(source).Input;

        Assert.Equal((1920, 1040), (input?.Width, input?.Height));
    }

    [Fact]
    public void Map_WhenJellyfinDoesNotSayWhetherItIsAnamorphic_TrustsADifferentDisplayAspectRatio()
    {
        // Libraries scanned before Jellyfin stored IsAnamorphic.
        var source = Source("a", "/media/dvd.mkv");
        source.MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Codec = "mpeg2video", Width = 720, Height = 576, AspectRatio = "16:9" }];

        var input = JellyfinVideoInputResolver.Map(source).Input;

        Assert.Equal((1024, 576), (input?.Width, input?.Height));
    }

    [Fact]
    public void Map_HdrVideo_PassesItsDynamicRange()
    {
        var source = Source("a", "/media/movie.mkv");
        source.MediaStreams = [Hdr("smpte2084", 10)];

        Assert.Equal(VideoDynamicRange.Hdr10, JellyfinVideoInputResolver.Map(source).Input?.DynamicRange);
    }

    [Theory]
    [InlineData("smpte2084", 10, VideoDynamicRange.Hdr10)]
    [InlineData("arib-std-b67", 10, VideoDynamicRange.Hlg)]
    [InlineData("SMPTE2084", 12, VideoDynamicRange.Hdr10)]
    [InlineData("bt709", 10, VideoDynamicRange.Sdr)]
    [InlineData(null, null, VideoDynamicRange.Sdr)]
    [InlineData("smpte2084", null, VideoDynamicRange.Hdr10)]
    [InlineData("smpte2084", 8, VideoDynamicRange.Sdr)]
    public void GetDynamicRange_FollowsTheTransferOf10BitVideo(string? transfer, int? bitDepth, VideoDynamicRange expected)
    {
        Assert.Equal(expected, JellyfinVideoInputResolver.GetDynamicRange(Hdr(transfer, bitDepth)));
    }

    [Theory]
    [InlineData(5, 0, null, VideoDynamicRange.DolbyVision)]
    [InlineData(8, 1, "smpte2084", VideoDynamicRange.Hdr10)]
    [InlineData(8, 4, "arib-std-b67", VideoDynamicRange.Hlg)]
    [InlineData(8, 2, "bt709", VideoDynamicRange.Sdr)]
    [InlineData(7, 6, "smpte2084", VideoDynamicRange.Hdr10)]
    [InlineData(8, 3, "smpte2084", VideoDynamicRange.Hdr10)]
    [InlineData(8, 3, "arib-std-b67", VideoDynamicRange.Hlg)]
    public void GetDynamicRange_DolbyVision_NeedsItsMetadataOnlyWithoutACompatibleBaseLayer(int profile, int compatibility, string? transfer, VideoDynamicRange expected)
    {
        var video = Hdr(transfer, 10);
        video.DvProfile = profile;
        video.DvBlSignalCompatibilityId = compatibility;
        video.RpuPresentFlag = 1;
        video.BlPresentFlag = 1;

        Assert.Equal(expected, JellyfinVideoInputResolver.GetDynamicRange(video));
    }

    private static MediaStream Hdr(string? transfer, int? bitDepth) => new()
    {
        Type = MediaStreamType.Video,
        Codec = "hevc",
        Width = 3840,
        Height = 2160,
        BitDepth = bitDepth,
        ColorTransfer = transfer,
        ColorPrimaries = transfer is null ? null : "bt2020",
        ColorSpace = transfer is null ? null : "bt2020nc",
    };

    private static MediaSourceInfo Source(string id, string path) => new()
    {
        Id = id,
        Path = path,
        Protocol = MediaProtocol.File,
        VideoType = VideoType.VideoFile,
        MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Codec = "hevc", Width = 3840, Height = 1606 }],
    };

    private JellyfinVideoInputResolver Resolver() => new(_libraryManager, _mediaSourceManager);
}
