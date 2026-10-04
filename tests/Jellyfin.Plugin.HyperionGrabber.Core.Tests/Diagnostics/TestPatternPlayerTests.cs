using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Diagnostics;

public class TestPatternPlayerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PlayAsync_SendsFramesForTheDurationThenClears()
    {
        var sink = new RecordingSink();
        var options = new TestPatternOptions { Width = 16, Height = 9, FramesPerSecond = 50, Duration = TimeSpan.FromMilliseconds(200) };

        var sent = await TestPatternPlayer.PlayAsync(sink, options, TimeProvider.System, Ct);

        Assert.Equal(10, sent);
        Assert.Equal(10, sink.Frames.Count);
        Assert.All(sink.Frames, frame =>
        {
            Assert.Equal(16, frame.Width);
            Assert.Equal(9, frame.Height);
            Assert.Equal(16 * 9 * 3, frame.Rgb24.Length);
        });
        Assert.Equal(1, sink.ClearCount);
    }

    [Fact]
    public async Task PlayAsync_PacesFramesWithTheTimeProvider()
    {
        var time = new FakeTimeProvider();
        var sink = new RecordingSink();
        var options = new TestPatternOptions { FramesPerSecond = 10, Duration = TimeSpan.FromSeconds(1) };

        var play = TestPatternPlayer.PlayAsync(sink, options, time, Ct);
        await TestHelpers.WaitUntilAsync(() => sink.Frames.Count == 1);
        await Task.Delay(50, Ct);
        Assert.Single(sink.Frames); // waits for the clock, not for wall time

        for (var expected = 2; expected <= 10; expected++)
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            await TestHelpers.WaitUntilAsync(() => sink.Frames.Count == expected);
        }

        Assert.Equal(10, await play);
    }

    [Fact]
    public async Task PlayAsync_WhenCancelled_StillClears()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var sink = new RecordingSink();
        var options = new TestPatternOptions { FramesPerSecond = 50, Duration = TimeSpan.FromSeconds(30) };

        var play = TestPatternPlayer.PlayAsync(sink, options, TimeProvider.System, cancellation.Token);
        await TestHelpers.WaitUntilAsync(() => sink.Frames.Count >= 3);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => play);
        Assert.Equal(1, sink.ClearCount);
    }

    [Fact]
    public async Task PlayAsync_WhenConnectionIsLost_PropagatesAndStillTriesToClear()
    {
        var sink = new RecordingSink { FailAfterFrames = 2, FailClear = true };
        var options = new TestPatternOptions { FramesPerSecond = 50, Duration = TimeSpan.FromSeconds(1) };

        await Assert.ThrowsAsync<HyperionConnectionException>(() => TestPatternPlayer.PlayAsync(sink, options, TimeProvider.System, Ct));

        Assert.Equal(2, sink.Frames.Count);
        Assert.Equal(1, sink.ClearCount);
    }

    [Fact]
    public async Task PlayAsync_RejectsInvalidOptions()
    {
        var options = new TestPatternOptions { Duration = TimeSpan.FromMinutes(5) };

        await Assert.ThrowsAsync<ArgumentException>(() => TestPatternPlayer.PlayAsync(new RecordingSink(), options, TimeProvider.System, Ct));
    }
}
