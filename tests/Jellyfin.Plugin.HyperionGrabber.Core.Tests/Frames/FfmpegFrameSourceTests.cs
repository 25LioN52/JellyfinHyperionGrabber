using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Frames;

/// <summary>
/// Runs the real FFmpeg against a generated clip; skipped when FFmpeg is not installed.
/// </summary>
public class FfmpegFrameSourceTests(FfmpegFixture ffmpeg) : IClassFixture<FfmpegFixture>
{
    private const int Tolerance = 16;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.5, 1)]
    [InlineData(2.5, 2)]
    [InlineData(3.5, 3)]
    public async Task ReadFrameAsync_ShowsTheColorAtTheStartPosition(double startSeconds, int colorIndex)
    {
        ffmpeg.SkipIfUnavailable();
        await using var source = Start(Options() with { StartPosition = TimeSpan.FromSeconds(startSeconds) });

        using var frame = await source.ReadFrameAsync(Ct);

        Assert.NotNull(frame);
        Assert.Equal(TimeSpan.FromSeconds(startSeconds), frame.Position);
        AssertColor(frame, FfmpegFixture.Colors[colorIndex]);
    }

    [Fact]
    public async Task ReadFrameAsync_ScalesToTheOutputWidthKeepingTheAspectRatio()
    {
        ffmpeg.SkipIfUnavailable();
        await using var source = Start(Options());

        using var frame = await source.ReadFrameAsync(Ct);

        Assert.NotNull(frame);
        Assert.Equal((160, 90), (frame.Width, frame.Height));
        Assert.Equal((160, 90), (source.Width, source.Height));
        Assert.Equal(160 * 90 * 3, frame.Rgb24.Length);
    }

    [Fact]
    public async Task ReadFrameAsync_DeliversConstantRateFramesUntilTheEndOfTheClip()
    {
        ffmpeg.SkipIfUnavailable();
        var start = TimeSpan.FromSeconds(3);
        await using var source = Start(Options() with { StartPosition = start, FramesPerSecond = 10 });

        var positions = new List<TimeSpan>();
        while (await source.ReadFrameAsync(Ct) is { } frame)
        {
            using (frame)
            {
                Assert.Equal(positions.Count, frame.Index);
                AssertColor(frame, FfmpegFixture.Colors[3]);
                positions.Add(frame.Position);
            }
        }

        Assert.InRange(positions.Count, 9, 11);
        Assert.Equal(positions.Select((_, i) => start + TimeSpan.FromMilliseconds(100 * i)), positions);
        Assert.Null(await source.ReadFrameAsync(Ct));
    }

    [Fact]
    public async Task ReadFrameAsync_WhenEveryBufferIsHeld_StopsReadingAndFfmpegWaits()
    {
        ffmpeg.SkipIfUnavailable();
        await using var source = Start(Options() with { BufferCount = 2 });
        using var first = await source.ReadFrameAsync(Ct);
        using var second = await source.ReadFrameAsync(Ct);
        var pid = source.ProcessId;

        // Proving that something does not happen: an unthrottled FFmpeg finishes this clip in well under a second.
        await Task.Delay(500, Ct);

        Assert.Equal(2, source.FramesRead);
        Assert.NotNull(pid);
        using var process = Process.GetProcessById(pid.Value);
        Assert.False(process.HasExited);
    }

    [Fact]
    public async Task ReadFrameAsync_ReusesTheBufferOfADisposedFrame()
    {
        ffmpeg.SkipIfUnavailable();
        await using var source = Start(Options() with { BufferCount = 2 });
        var first = await source.ReadFrameAsync(Ct);
        using var second = await source.ReadFrameAsync(Ct);
        Assert.NotNull(first);
        var firstBuffer = ArrayOf(first.Rgb24);

        first.Dispose();
        first.Dispose(); // Idempotent: must not put the buffer into the pool twice.
        using var third = await source.ReadFrameAsync(Ct);

        Assert.NotNull(third);
        Assert.Same(firstBuffer, ArrayOf(third.Rgb24));
        Assert.Equal(2, third.Index);
    }

    [Fact]
    public async Task ReadFrameAsync_WhenCancelledWhileWaiting_ThrowsAndTheSourceKeepsWorking()
    {
        ffmpeg.SkipIfUnavailable();
        await using var source = Start(Options() with { BufferCount = 2 });
        var first = await source.ReadFrameAsync(Ct);
        using var second = await source.ReadFrameAsync(Ct);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        waiting.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.ReadFrameAsync(waiting.Token));

        first!.Dispose();
        using var third = await source.ReadFrameAsync(Ct);
        Assert.NotNull(third);
    }

    [Fact]
    public async Task DisposeAsync_KillsAndAwaitsABlockedFfmpeg()
    {
        ffmpeg.SkipIfUnavailable();
        var source = Start(Options() with { BufferCount = 2 });
        using var first = await source.ReadFrameAsync(Ct);
        using var second = await source.ReadFrameAsync(Ct);
        using var process = Process.GetProcessById(source.ProcessId!.Value);

        await source.DisposeAsync();

        Assert.True(process.HasExited);
        Assert.Null(source.ProcessId);
        Assert.Null(await source.ReadFrameAsync(Ct));
    }

    [Fact]
    public async Task ReadFrameAsync_WhenTheInputIsMissing_ThrowsWithFfmpegsError()
    {
        ffmpeg.SkipIfUnavailable();
        var missing = Path.Combine(Path.GetDirectoryName(ffmpeg.ClipPath)!, "missing-clip.mkv");
        await using var source = Start(Options() with { InputPath = missing });

        var exception = await Assert.ThrowsAsync<FrameSourceException>(async () => await source.ReadFrameAsync(Ct));

        Assert.Contains("exited with code", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing-clip", exception.Message, StringComparison.Ordinal);
        Assert.Null(source.ProcessId);
    }

    [Fact]
    public async Task ReadFrameAsync_WhenHardwareDecodingFails_FallsBackToTheCpu()
    {
        ffmpeg.SkipIfUnavailable();
        var logger = new FakeLogger<FfmpegFrameSource>();
        var options = Options() with { HardwareAcceleration = HardwareAcceleration.Vaapi, HardwareDevice = "/nonexistent/renderD128" };
        await using var source = FfmpegFrameSource.Start(options, TimeProvider.System, logger);

        using var frame = await source.ReadFrameAsync(Ct);

        Assert.NotNull(frame);
        AssertColor(frame, FfmpegFixture.Colors[0]);
        Assert.Equal(HardwareAcceleration.None, source.ActiveAcceleration);
        Assert.Contains(logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning && r.Message.Contains("Vaapi", StringComparison.Ordinal));
    }

    /// <summary>
    /// Manual check on real hardware: set <c>HYPERION_GRABBER_HWACCEL</c> to a <see cref="HardwareAcceleration"/>
    /// name (and <c>HYPERION_GRABBER_HWDEVICE</c> for VA-API/QSV on Linux).
    /// </summary>
    [Fact]
    public async Task ReadFrameAsync_WithConfiguredHardware_DecodesWithoutFallingBack()
    {
        ffmpeg.SkipIfUnavailable();
        var configured = Environment.GetEnvironmentVariable("HYPERION_GRABBER_HWACCEL");
        Assert.SkipWhen(string.IsNullOrEmpty(configured), "Set HYPERION_GRABBER_HWACCEL to test hardware decoding.");
        var acceleration = Enum.Parse<HardwareAcceleration>(configured, ignoreCase: true);
        var options = Options() with
        {
            StartPosition = TimeSpan.FromSeconds(2.5),
            HardwareAcceleration = acceleration,
            HardwareDevice = Environment.GetEnvironmentVariable("HYPERION_GRABBER_HWDEVICE"),
        };
        await using var source = Start(options);

        using var frame = await source.ReadFrameAsync(Ct);

        Assert.NotNull(frame);
        AssertColor(frame, FfmpegFixture.Colors[2]);
        Assert.Equal(acceleration, source.ActiveAcceleration);
    }

    private static FfmpegFrameSource Start(FfmpegFrameSourceOptions options)
        => FfmpegFrameSource.Start(options, TimeProvider.System, NullLogger.Instance);

    private static byte[] ArrayOf(ReadOnlyMemory<byte> memory)
        => MemoryMarshal.TryGetArray(memory, out var segment) ? segment.Array! : throw new InvalidOperationException("Not array-backed.");

    private static void AssertColor(VideoFrame frame, (byte R, byte G, byte B) expected)
    {
        var pixels = frame.Rgb24.Span;
        int[] offsets = [0, (((frame.Height / 2) * frame.Width) + (frame.Width / 2)) * 3, pixels.Length - 3];
        foreach (var offset in offsets)
        {
            var actual = (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
            Assert.True(
                Math.Abs(actual.Item1 - expected.R) <= Tolerance && Math.Abs(actual.Item2 - expected.G) <= Tolerance && Math.Abs(actual.Item3 - expected.B) <= Tolerance,
                $"Frame {frame.Index} at byte {offset}: expected {expected}, got {actual}.");
        }
    }

    private FfmpegFrameSourceOptions Options() => new()
    {
        FfmpegPath = ffmpeg.FfmpegPath!,
        InputPath = ffmpeg.ClipPath,
        SourceWidth = FfmpegFixture.ClipWidth,
        SourceHeight = FfmpegFixture.ClipHeight,
    };
}

/// <summary>
/// Frame source behaviour that does not need FFmpeg.
/// </summary>
public class FfmpegFrameSourceStartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Start_WithInvalidOptions_ThrowsArgumentException()
    {
        var options = new FfmpegFrameSourceOptions { FfmpegPath = "ffmpeg", InputPath = "in.mkv", SourceWidth = 0, SourceHeight = 0 };

        var exception = Assert.Throws<ArgumentException>(() => FfmpegFrameSource.Start(options, TimeProvider.System, NullLogger.Instance));

        Assert.Contains("Source width and height", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadFrameAsync_WhenFfmpegCannotBeStarted_ThrowsFrameSourceException()
    {
        var path = Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N"), "ffmpeg");
        var options = new FfmpegFrameSourceOptions { FfmpegPath = path, InputPath = "in.mkv", SourceWidth = 1920, SourceHeight = 1080 };
        await using var source = FfmpegFrameSource.Start(options, TimeProvider.System, NullLogger.Instance);

        var exception = await Assert.ThrowsAsync<FrameSourceException>(async () => await source.ReadFrameAsync(Ct));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }
}
