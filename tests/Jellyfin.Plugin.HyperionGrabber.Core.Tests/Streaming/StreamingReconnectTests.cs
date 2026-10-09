using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Jellyfin.Plugin.HyperionGrabber.Core.Streaming;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Streaming;

/// <summary>
/// A session with the real <see cref="HyperionClient"/> against <see cref="FakeHyperionServer"/>, fake frames and fake
/// time: a dropped connection is noticed, reconnected and streamed to again. Frame <c>n</c>'s first byte is <c>n</c>.
/// </summary>
public sealed class StreamingReconnectTests : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 9, 20, 0, 0, TimeSpan.Zero));
    private readonly FakeHyperionServer _server = FakeHyperionServer.Start();
    private readonly ConcurrentQueue<FakeFrameSource> _sources = new();
    private readonly CancellationTokenSource _stopTicking = new();
    private StreamingGrabSession? _session;
    private Task _ticker = Task.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _stopTicking.CancelAsync();
        await _ticker;
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        await _server.DisposeAsync();
        _stopTicking.Dispose();
    }

    [Fact]
    public async Task ServerDropsTheConnection_StreamingResumesOnANewConnection()
    {
        var session = await StartAsync();
        await _server.NextRequestAsync<RegisterRequest>();
        var lastBeforeDrop = FrameNumber(await _server.NextRequestAsync<ImageRequest>());

        _server.DropConnections();

        // Images already on the way may still arrive; then the session registers again.
        ReceivedRequest request;
        while ((request = await _server.NextRequestAsync()) is ImageRequest image)
        {
            lastBeforeDrop = FrameNumber(image);
        }

        Assert.IsType<RegisterRequest>(request);
        var resumed = FrameNumber(await _server.NextRequestAsync<ImageRequest>());

        Assert.Equal(2, _server.ConnectionCount);
        Assert.False(session.Completion.IsCompleted);
        Assert.Single(_sources); // Decoding went on: no restart.

        // Reconnected one second (ten frames) after the loss, with the frame due then, not the one at the loss.
        Assert.True(resumed >= lastBeforeDrop + 10, $"Resumed with frame {resumed}; the last one before the drop was {lastBeforeDrop}.");
    }

    private static int FrameNumber(ImageRequest image) => image.Data[0];

    private async Task<StreamingGrabSession> StartAsync()
    {
        var host = new Host(_server);
        var factory = new StreamingGrabSessionFactory(
            host,
            host,
            host,
            _time,
            NullLoggerFactory.Instance,
            options =>
            {
                var source = new FakeFrameSource(options);
                _sources.Enqueue(source);
                return source;
            },
            async (options, cancellationToken) => await HyperionClient.ConnectAsync(options, NullLogger<HyperionClient>.Instance, cancellationToken));
        var state = new PlaybackState
        {
            SessionId = "session",
            DeviceId = "kodi",
            DeviceName = "Living room",
            Client = "Kodi",
            ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Position = TimeSpan.Zero,
            IsStart = true,
            ReportedAt = _time.GetUtcNow(),
        };
        _session = Assert.IsType<StreamingGrabSession>(factory.Start(state));
        Assert.True(await _session.StreamingStarted.WaitAsync(Ct));
        var source = _sources.Single();
        source.Push(5);
        _ticker = TickAsync(_session, source, _stopTicking.Token);
        return _session;
    }

    /// <summary>
    /// Plays the video on the fake clock: one frame interval per handled tick, one new frame per tick (frame numbers stay
    /// below 256 for the 25 s of video this test may use).
    /// </summary>
    /// <remarks>Connecting and sending take real time, which the fake clock must leave room for: it runs at about ten
    /// times real time. The delay paces the clock; it does not synchronize the test, which only counts frames.</remarks>
    private async Task TickAsync(StreamingGrabSession session, FakeFrameSource source, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && session.Ticks < 250)
        {
            var before = session.Ticks;
            source.Push(1);
            _time.Advance(Interval);
            await TestHelpers.WaitUntilAsync(() => session.Ticks > before || cancellationToken.IsCancellationRequested);
            await Task.Delay(Interval / 10, CancellationToken.None);
        }
    }

    private sealed class Host(FakeHyperionServer server) : IVideoInputResolver, IFfmpegSettingsProvider, IStreamingSettingsProvider
    {
        public VideoInputResult Resolve(Guid itemId, string? mediaSourceId)
            => VideoInputResult.Supported(new VideoInput("file:/media/movie.mkv", 1920, 1080, "h264"));

        public FfmpegSettings GetSettings() => new() { FfmpegPath = "ffmpeg" };

        StreamingSettings IStreamingSettingsProvider.GetSettings() => new()
        {
            Hyperion = new HyperionClientOptions { Host = server.Host, Port = server.Port },
            FramesPerSecond = 10,
        };
    }
}
