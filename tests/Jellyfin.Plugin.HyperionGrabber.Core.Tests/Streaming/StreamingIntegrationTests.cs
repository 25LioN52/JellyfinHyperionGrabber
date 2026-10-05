using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Jellyfin.Plugin.HyperionGrabber.Core.Streaming;
using Jellyfin.Plugin.HyperionGrabber.Core.Tests.Frames;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Streaming;

/// <summary>
/// Playback events → <see cref="PlaybackMonitor"/> → <see cref="StreamingGrabSessionFactory"/> → real FFmpeg on the
/// generated clip → <see cref="HyperionClient"/> → <see cref="FakeHyperionServer"/>. Skipped without FFmpeg.
/// </summary>
public sealed class StreamingIntegrationTests(FfmpegFixture ffmpeg) : IClassFixture<FfmpegFixture>, IAsyncDisposable
{
    private const int Tolerance = 16;
    private static readonly Guid Movie = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly FakeHyperionServer _server = FakeHyperionServer.Start();
    private PlaybackMonitor? _monitor;

    public async ValueTask DisposeAsync()
    {
        if (_monitor is not null)
        {
            await _monitor.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    [Fact]
    public async Task PausedPlayback_ShowsTheFrameAtThePositionUntilStopped()
    {
        ffmpeg.SkipIfUnavailable();
        var monitor = StartMonitor();

        monitor.Post(Event(PlaybackEventKind.Started, TimeSpan.FromSeconds(1.5), paused: true));

        var register = await _server.NextRequestAsync<RegisterRequest>();
        Assert.Equal((HyperionDefaults.Origin, HyperionDefaults.Priority), (register.Origin, register.Priority));
        var image = await _server.NextRequestAsync<ImageRequest>();
        AssertImage(image, FfmpegFixture.Colors[1]);

        // Paused: the same frame is resent so Hyperion keeps the connection open.
        var keepAlive = await _server.NextRequestAsync<ImageRequest>();
        Assert.Equal(image.Data, keepAlive.Data);

        monitor.Post(Event(PlaybackEventKind.Stopped, null));
        var clear = await NextClearAsync();

        Assert.Equal(HyperionDefaults.Priority, clear.Priority);
    }

    [Fact]
    public async Task Playback_StreamsTheVideoInOrderThenClearsOnStop()
    {
        ffmpeg.SkipIfUnavailable();
        var monitor = StartMonitor();

        monitor.Post(Event(PlaybackEventKind.Started, TimeSpan.Zero));

        await _server.NextRequestAsync<RegisterRequest>();
        var colors = new List<int> { ColorIndex(await _server.NextRequestAsync<ImageRequest>()) };
        var stopwatch = Stopwatch.StartNew();
        while (colors[^1] == 0)
        {
            colors.Add(ColorIndex(await _server.NextRequestAsync<ImageRequest>()));
        }

        // Red for the first second, then green, sent at the playback pace (25 fps = 40 ms per frame), not as fast
        // as FFmpeg decodes. Real time only makes it slower, so the lower bound on the pace cannot flake.
        Assert.Equal(1, colors[^1]);
        Assert.All(colors.GetRange(0, colors.Count - 1), c => Assert.Equal(0, c));
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(20 * (colors.Count - 1)), $"{colors.Count} frames in {stopwatch.Elapsed}.");

        monitor.Post(Event(PlaybackEventKind.Stopped, null));
        var clear = await NextClearAsync();

        Assert.Equal(HyperionDefaults.Priority, clear.Priority);
    }

    private static PlaybackEvent Event(PlaybackEventKind kind, TimeSpan? position, bool paused = false) => new()
    {
        Kind = kind,
        SessionId = "session",
        DeviceId = "kodi",
        DeviceName = "Living room",
        Client = "Kodi",
        ItemId = Movie,
        Position = position,
        IsPaused = paused,
    };

    private static void AssertImage(ImageRequest image, (byte R, byte G, byte B) expected)
    {
        Assert.Equal((160, 90), (image.Width, image.Height));
        Assert.Equal(HyperionDefaults.InfiniteDuration, image.Duration);
        Assert.Equal(160 * 90 * 3, image.Data.Length);
        Assert.Equal(Array.IndexOf(FfmpegFixture.Colors, expected), ColorIndex(image));
    }

    /// <summary>Returns which clip color the center pixel shows, or -1.</summary>
    private static int ColorIndex(ImageRequest image)
    {
        var offset = (((image.Height / 2) * image.Width) + (image.Width / 2)) * 3;
        for (var i = 0; i < FfmpegFixture.Colors.Length; i++)
        {
            var (r, g, b) = FfmpegFixture.Colors[i];
            if (Math.Abs(image.Data[offset] - r) <= Tolerance && Math.Abs(image.Data[offset + 1] - g) <= Tolerance && Math.Abs(image.Data[offset + 2] - b) <= Tolerance)
            {
                return i;
            }
        }

        return -1;
    }

    private async Task<ClearRequest> NextClearAsync()
    {
        // Frames already on the way may arrive before the Clear; nothing may follow it.
        while (true)
        {
            var request = await _server.NextRequestAsync();
            if (request is ClearRequest clear)
            {
                return clear;
            }

            Assert.IsType<ImageRequest>(request);
        }
    }

    private PlaybackMonitor StartMonitor()
    {
        var host = new Host(new VideoInput("file:" + ffmpeg.ClipPath, FfmpegFixture.ClipWidth, FfmpegFixture.ClipHeight, null), ffmpeg.FfmpegPath!, _server);
        var factory = new StreamingGrabSessionFactory(host, host, host, TimeProvider.System, NullLoggerFactory.Instance);
        _monitor = new PlaybackMonitor(factory, TimeProvider.System, NullLogger<PlaybackMonitor>.Instance);
        _monitor.Start();
        _monitor.UpdateFilter(PlaybackFilter.Create(true, ["kodi"], []));
        return _monitor;
    }

    private sealed class Host(VideoInput video, string ffmpegPath, FakeHyperionServer server) : IVideoInputResolver, IFfmpegSettingsProvider, IStreamingSettingsProvider
    {
        public VideoInputResult Resolve(Guid itemId, string? mediaSourceId) => VideoInputResult.Supported(video);

        public FfmpegSettings GetSettings() => new() { FfmpegPath = ffmpegPath };

        StreamingSettings IStreamingSettingsProvider.GetSettings() => new()
        {
            Hyperion = new HyperionClientOptions { Host = server.Host, Port = server.Port },
        };
    }
}
