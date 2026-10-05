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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Streaming;

/// <summary>
/// Pacing, dropping, pause, seek and failure handling of a session, with fake frames, a recording connection and
/// fake time. The stream runs at 10 fps, so one tick is 100 ms and frame <c>n</c> shows <c>n × 100 ms</c>.
/// </summary>
public sealed class StreamingGrabSessionTests : IAsyncDisposable
{
    private static readonly Guid Movie = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 20, 0, 0, TimeSpan.Zero));
    private readonly ConcurrentQueue<FakeFrameSource> _sources = new();
    private readonly FakeLoggerProvider _logs = new();
    private readonly LoggerFactory _loggerFactory;
    private RecordingConnection _connection = new();
    private StreamingSettings _settings = new() { Hyperion = new HyperionClientOptions { Host = "hyperion.local" }, FramesPerSecond = 10 };
    private VideoInputResult _video = VideoInputResult.Supported(new VideoInput("file:/media/movie.mkv", 1920, 1080, "h264"));
    private Exception? _connectFailure;
    private int _connects;
    private StreamingGrabSession? _session;

    public StreamingGrabSessionTests() => _loggerFactory = new LoggerFactory([_logs]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeFrameSource Source => _sources.Last();

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _loggerFactory.Dispose();
        _logs.Dispose();
    }

    [Fact]
    public async Task Start_DecodesFromTheEstimatedPosition()
    {
        var state = State(TimeSpan.FromMinutes(1)) with { ReportedAt = _time.GetUtcNow() - TimeSpan.FromSeconds(2) };

        var session = Start(state);

        Assert.True(await session.StreamingStarted.WaitAsync(Ct));
        var source = Assert.Single(_sources);
        Assert.Equal(TimeSpan.FromSeconds(62), source.Options.StartPosition);
        Assert.Equal(10, source.Options.FramesPerSecond);
        Assert.Equal("file:/media/movie.mkv", source.Options.InputPath);
        Assert.Equal((160, 90), (source.Options.OutputWidth, source.Options.OutputHeight));
        AssertLogged(LogLevel.Information, "Streaming 160x90 at 10 fps");
    }

    [Fact]
    public async Task Hyperion_IsConnectedWhenTheFirstFrameIsDue()
    {
        var session = await StartStreamingAsync();
        await TickAsync(session, 3); // FFmpeg is still opening the file: nothing to send yet.
        Assert.Equal(0, _connects);

        Source.Push(5);
        await TickAsync(session);

        Assert.Equal(1, _connects);
        Assert.Equal("hyperion.local", _connection.Options?.Host);
        Assert.Equal([4], _connection.Images);
    }

    [Fact]
    public async Task Tick_SendsTheFrameThatIsDue_OnePerTick()
    {
        var session = await StartStreamingAsync();
        Source.Push(5);

        await TickAsync(session, 3);

        // At 100 ms frames 0 and 1 are both due; frame 1 is the one to show.
        Assert.Equal([1, 2, 3], _connection.Images);
        Assert.Equal(3, session.FramesSent);
        Assert.Equal(1, session.FramesDropped);
    }

    [Fact]
    public async Task SlowHyperion_FramesThatBecameLateAreDroppedNotQueued()
    {
        var session = await StartStreamingAsync();
        Source.Push(10);
        var hyperion = new TaskCompletionSource();
        _connection.SendGate = hyperion.Task;
        _time.Advance(Interval);
        await TestHelpers.WaitUntilAsync(() => _connection.Images.Count == 1);

        // Hyperion takes 400 ms for frame 1; the frames due meanwhile are not sent one after another afterwards.
        for (var i = 0; i < 4; i++)
        {
            _time.Advance(Interval);
        }

        _connection.SendGate = Task.CompletedTask;
        hyperion.SetResult();
        await TestHelpers.WaitUntilAsync(() => session.Ticks == 2);

        Assert.Equal([1, 5], _connection.Images);
        Assert.Equal(2, session.FramesSent);
        Assert.Equal(4, session.FramesDropped);
        Assert.Equal(4, Source.Outstanding); // Every dropped frame went back to the pool: only 6-9 are out.
    }

    [Fact]
    public async Task Pause_HoldsTheLastFrameAndResendsItToKeepTheConnectionAlive()
    {
        var session = await StartStreamingAsync();
        Source.Push(10);
        await TickAsync(session);

        session.Update(State(TimeSpan.FromMilliseconds(100), paused: true));
        await TickAsync(session, 4);
        var whilePaused = _connection.Images;
        await TickAsync(session);

        Assert.Equal([1], whilePaused);
        Assert.Equal([1, 1], _connection.Images); // Resent 500 ms after the last send.
        Assert.Equal(1, session.FramesSent);
        Assert.Single(_sources);
    }

    [Fact]
    public async Task Resume_ContinuesWithTheNextFrame()
    {
        var session = await StartStreamingAsync();
        Source.Push(10);
        await TickAsync(session);
        session.Update(State(TimeSpan.FromMilliseconds(100), paused: true));
        await TickAsync(session, 3);

        session.Update(State(TimeSpan.FromMilliseconds(100)));
        await TickAsync(session);

        Assert.Equal([1, 2], _connection.Images);
        Assert.Single(_sources);
        Assert.Equal(0, session.Restarts);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(30)]
    public async Task Seek_RestartsDecodingAtTheNewPosition(int minutes)
    {
        var session = await StartStreamingAsync(TimeSpan.FromMinutes(10));
        Source.Push(3);
        await TickAsync(session);
        var first = Source;

        session.Update(State(TimeSpan.FromMinutes(minutes)));
        await TickAsync(session);

        Assert.True(first.IsDisposed);
        Assert.Equal(0, first.Outstanding); // The frame that was waiting to be shown went back to the pool.
        Assert.Equal(2, _sources.Count);
        Assert.Equal(TimeSpan.FromMinutes(minutes) + Interval, Source.Options.StartPosition);
        Assert.Equal(1, session.Restarts);
    }

    [Fact]
    public async Task PositionReport_CloseToTheEstimate_DoesNotRestart()
    {
        var session = await StartStreamingAsync(TimeSpan.FromMinutes(10));
        Source.Push(3);
        await TickAsync(session);

        session.Update(State(TimeSpan.FromMinutes(10) + TimeSpan.FromMilliseconds(600)));
        await TickAsync(session);

        Assert.Single(_sources);
    }

    [Fact]
    public async Task SlowDecoder_IsRestartedAtThePlaybackPositionAfterTheGracePeriod()
    {
        var session = await StartStreamingAsync();
        Source.Push(1);
        await TickAsync(session, 48);
        Source.Push(1); // Frame 1 (100 ms) arrives at 4.9 s: far behind, but the decoder may still catch up.
        await TickAsync(session);
        Assert.Equal(0, session.Restarts);

        Source.Push(1); // Frame 2 (200 ms) at 5 s: still far behind after the grace period.
        await TickAsync(session);

        Assert.Equal(1, session.Restarts);
        Assert.Equal(TimeSpan.FromSeconds(5), Source.Options.StartPosition);
        AssertLogged(LogLevel.Debug, "Decoding lags");
    }

    [Fact]
    public async Task EndOfVideo_KeepsResendingTheLastFrame()
    {
        var session = await StartStreamingAsync();
        Source.Push(2);
        Source.End();

        await TickAsync(session, 6);

        Assert.Equal([1, 1], _connection.Images);
        Assert.Equal(0, session.Restarts);
    }

    [Fact]
    public async Task LatencyOffset_DecodesAndSendsFramesEarlier()
    {
        _settings = _settings with { LatencyOffset = TimeSpan.FromMilliseconds(300) };
        var session = await StartStreamingAsync();
        Source.Push(5); // Frames at 300, 400, 500, ... ms.

        await TickAsync(session); // 100 ms of playback + 300 ms offset: frame 1 (400 ms) is due.

        Assert.Equal(TimeSpan.FromMilliseconds(300), Source.Options.StartPosition);
        Assert.Equal([1], _connection.Images);
    }

    [Fact]
    public async Task NegativeLatencyOffset_NeverDecodesBeforeTheStart()
    {
        _settings = _settings with { LatencyOffset = TimeSpan.FromMilliseconds(-500) };

        await StartStreamingAsync();

        Assert.Equal(TimeSpan.Zero, Source.Options.StartPosition);
    }

    [Fact]
    public async Task LatencyOffset_IsNotMistakenForASeek()
    {
        _settings = _settings with { LatencyOffset = TimeSpan.FromSeconds(1.5) };
        var session = await StartStreamingAsync();
        Source.Push(3);
        await TickAsync(session);

        session.Update(State(TimeSpan.FromMilliseconds(100))); // The report matches the estimate (without the offset).
        await TickAsync(session);

        Assert.Single(_sources);
        Assert.Equal(0, session.Restarts);
    }

    [Fact]
    public async Task LongPause_ReleasesHyperionAndDecoding_ResumeStreamsAgain()
    {
        _settings = _settings with { PauseRelease = TimeSpan.FromSeconds(1) };
        var session = await StartStreamingAsync();
        Source.Push(10);
        await TickAsync(session);
        var first = Source;
        var firstConnection = _connection;

        session.Update(State(TimeSpan.FromMilliseconds(100), paused: true));
        await TickAsync(session, 12);
        var sentBeforeRelease = firstConnection.Images.Count;
        await TickAsync(session, 10);

        Assert.True(first.IsDisposed);
        Assert.True(firstConnection.IsDisposed); // Closing the connection clears the priority: Hyperion shows its default.
        Assert.Equal(sentBeforeRelease, firstConnection.Images.Count); // No keep-alive while released.
        AssertLogged(LogLevel.Information, "released Hyperion until playback resumes");

        session.Update(State(TimeSpan.FromMilliseconds(100)));
        await TickAsync(session);

        Assert.Equal(2, _sources.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(200), Source.Options.StartPosition);
        AssertLogged(LogLevel.Information, "streaming to Hyperion again");

        Source.Push(3); // Frames at 200, 300, 400 ms.
        await TickAsync(session);

        Assert.Equal(2, _connects);
        Assert.NotSame(firstConnection, _connection);
        Assert.Equal([1], _connection.Images);
    }

    [Fact]
    public async Task PauseRelease_Zero_KeepsHoldingTheFrame()
    {
        _settings = _settings with { PauseRelease = TimeSpan.Zero };
        var session = await StartStreamingAsync();
        Source.Push(10);
        await TickAsync(session);

        session.Update(State(TimeSpan.FromMilliseconds(100), paused: true));
        await TickAsync(session, 30);

        Assert.False(Source.IsDisposed);
        Assert.False(_connection.IsDisposed);
        Assert.True(_connection.Images.Count > 3); // Keep-alive resends of frame 1.
        Assert.All(_connection.Images, image => Assert.Equal(1, image));
    }

    [Fact]
    public async Task Start_WithInvalidStreamingSettings_LogsAndDoesNotStream()
    {
        _settings = _settings with { LatencyOffset = TimeSpan.FromSeconds(5) };

        var session = Start(State(TimeSpan.Zero));

        Assert.False(await session.StreamingStarted.WaitAsync(Ct));
        Assert.Empty(_sources);
        AssertLogged(LogLevel.Warning, "Light timing offset must be between -2000 and 2000 ms.");
    }

    [Fact]
    public async Task DecodingFailure_StopsAndReleasesHyperion()
    {
        var session = await StartStreamingAsync();
        Source.Push(2);
        await TickAsync(session);
        Source.Fail("FFmpeg exited with code 1 (Invalid data found).");

        _time.Advance(Interval);
        await session.Completion;

        Assert.True(_connection.IsDisposed);
        Assert.True(Source.IsDisposed);
        AssertLogged(LogLevel.Warning, "Invalid data found");
        AssertLogged(LogLevel.Information, "Stopped streaming to Hyperion");
    }

    [Fact]
    public async Task DecodingFailure_WithADueFrame_ReturnsTheFrameToThePool()
    {
        var session = await StartStreamingAsync();
        Source.Push(2);
        Source.Fail("FFmpeg exited with code 1 (Invalid data found).");

        // The tick takes frame 1 as due, then learns that decoding failed.
        _time.Advance(Interval);
        await session.Completion;

        Assert.Equal(0, Source.Outstanding);
        Assert.Empty(_connection.Images);
    }

    [Fact]
    public async Task ConnectionLost_StopsDecoding()
    {
        var session = await StartStreamingAsync();
        Source.Push(5);
        _connection.FailSends = true;

        _time.Advance(Interval);
        await session.Completion;

        Assert.True(Source.IsDisposed);
        Assert.True(_connection.IsDisposed);
        AssertLogged(LogLevel.Warning, "Simulated connection loss");
    }

    [Fact]
    public async Task DisposeAsync_ReleasesDecoderAndHyperionAndReturnsHeldFrames()
    {
        var session = await StartStreamingAsync();
        Source.Push(5);
        await TickAsync(session);

        await session.DisposeAsync();

        Assert.True(Source.IsDisposed);
        Assert.True(_connection.IsDisposed);
        Assert.Equal(2, Source.Outstanding); // Frames 3 and 4 never left the source; the waiting frame 2 was returned.
        Assert.True(session.Completion.IsCompletedSuccessfully);
        AssertLogged(LogLevel.Information, "Stopped streaming to Hyperion: 1 frames sent, 1 dropped");
    }

    [Fact]
    public async Task DisposeAsync_WhenFfmpegDoesNotExit_ReturnsAfterTheStopTimeout()
    {
        var session = await StartStreamingAsync();
        Source.Push(2);
        await TickAsync(session);
        var ffmpeg = new TaskCompletionSource();
        Source.DisposeGate = ffmpeg.Task;
        try
        {
            var dispose = session.DisposeAsync().AsTask();

            // The timeout runs on the fake clock: advance it until DisposeAsync gives up.
            await TestHelpers.WaitUntilAsync(() =>
            {
                _time.Advance(TimeSpan.FromSeconds(1));
                return dispose.IsCompleted;
            });

            await dispose;
            Assert.False(session.Completion.IsCompleted);
            AssertLogged(LogLevel.Warning, "did not stop within 5 s");
        }
        finally
        {
            ffmpeg.SetResult();
        }

        await session.Completion;
        Assert.True(_connection.IsDisposed);
    }

    [Fact]
    public async Task Start_WithoutHyperionServer_DoesNotStream()
    {
        _settings = new StreamingSettings();

        var session = Start(State(TimeSpan.Zero));

        Assert.False(await session.StreamingStarted.WaitAsync(Ct));
        Assert.Equal(0, _connects);
        AssertLogged(LogLevel.Warning, "no Hyperion server is configured");
    }

    [Fact]
    public async Task Start_WithInvalidFrameRate_DoesNotStream()
    {
        _settings = _settings with { FramesPerSecond = 0 };

        var session = Start(State(TimeSpan.Zero));

        Assert.False(await session.StreamingStarted.WaitAsync(Ct));
        Assert.Equal(0, _connects);
        AssertLogged(LogLevel.Warning, "Frame rate must be between 1 and 60.");
    }

    [Fact]
    public async Task Start_ForUnsupportedMedia_LogsWhyAndDoesNotConnect()
    {
        _video = VideoInputResult.Unsupported("it is a live stream.");

        var session = Start(State(TimeSpan.Zero));

        Assert.False(await session.StreamingStarted.WaitAsync(Ct));
        Assert.Equal(0, _connects);
        AssertLogged(LogLevel.Information, $"The lights do not follow item {Movie}: it is a live stream.");
    }

    [Fact]
    public async Task HyperionUnreachable_StopsDecoding()
    {
        _connectFailure = new HyperionConnectionException("Could not connect to hyperion.local:19400: refused.");
        var session = await StartStreamingAsync();
        Source.Push(2);

        _time.Advance(Interval);
        await session.Completion;

        Assert.True(Source.IsDisposed);
        AssertLogged(LogLevel.Warning, "Could not connect to hyperion.local:19400");
    }

    /// <summary>Advances the clock by one frame interval per tick and waits until the session handled each tick.</summary>
    private async Task TickAsync(StreamingGrabSession session, int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            var before = session.Ticks;
            _time.Advance(Interval);
            await TestHelpers.WaitUntilAsync(() => session.Ticks > before);
        }
    }

    private PlaybackState State(TimeSpan position, bool paused = false) => new()
    {
        SessionId = "session",
        DeviceId = "kodi",
        DeviceName = "Living room",
        Client = "Kodi",
        ItemId = Movie,
        MediaSourceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        Position = position,
        IsPaused = paused,
        ReportedAt = _time.GetUtcNow(),
    };

    private StreamingGrabSession Start(PlaybackState state)
    {
        var host = new Host(this);
        var factory = new StreamingGrabSessionFactory(
            host,
            host,
            host,
            _time,
            _loggerFactory,
            options =>
            {
                var source = new FakeFrameSource(options);
                _sources.Enqueue(source);
                return source;
            },
            (options, cancellationToken) =>
            {
                Interlocked.Increment(ref _connects);
                if (_connection.IsDisposed)
                {
                    _connection = new RecordingConnection();
                }

                _connection.Options = options;
                return _connectFailure is null
                    ? Task.FromResult<IHyperionConnection>(_connection)
                    : Task.FromException<IHyperionConnection>(_connectFailure);
            });
        _session = Assert.IsType<StreamingGrabSession>(factory.Start(state));
        return _session;
    }

    private async Task<StreamingGrabSession> StartStreamingAsync(TimeSpan? position = null)
    {
        var session = Start(State(position ?? TimeSpan.Zero));
        Assert.True(await session.StreamingStarted.WaitAsync(Ct));
        return session;
    }

    private void AssertLogged(LogLevel level, string text)
        => Assert.Contains(_logs.Collector.GetSnapshot(), r => r.Level == level && r.Message.Contains(text, StringComparison.Ordinal));

    private sealed class Host(StreamingGrabSessionTests test) : IVideoInputResolver, IFfmpegSettingsProvider, IStreamingSettingsProvider
    {
        public VideoInputResult Resolve(Guid itemId, string? mediaSourceId) => test._video;

        public FfmpegSettings GetSettings() => new() { FfmpegPath = "ffmpeg" };

        StreamingSettings IStreamingSettingsProvider.GetSettings() => test._settings;
    }
}
