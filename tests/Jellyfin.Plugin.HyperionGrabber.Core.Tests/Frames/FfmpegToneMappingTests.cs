using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Frames;

/// <summary>
/// HDR tone mapping with the real FFmpeg: the system's, and Jellyfin's (<c>tonemapx</c>) when
/// <c>HYPERION_GRABBER_JELLYFIN_FFMPEG</c> is set. Decodes <see cref="FfmpegFixture.HdrCodes"/>: black, gray, orange.
/// </summary>
public class FfmpegToneMappingTests(FfmpegFixture ffmpeg) : IClassFixture<FfmpegFixture>
{
    private const string Pq = "smpte2084";
    private const string Hlg = "arib-std-b67";
    private const int Tolerance = 16;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hdr10_WithoutToneMapping_LooksPale()
    {
        ffmpeg.SkipIfUnavailable();

        var (black, gray, orange) = await ReadEachSecondAsync(ffmpeg.FfmpegPath!, Pq, VideoDynamicRange.Hdr10, HdrToneMapper.None);

        // What 0.7 sent: PQ code values shown as if they were SDR gamma values in BT.709 colors.
        AssertNear((0, 0, 0), black);
        AssertNear((130, 130, 130), gray);
        AssertNear((175, 71, 36), orange);
    }

    [Theory]
    [InlineData(HdrToneMapper.Tonemapx)]
    [InlineData(HdrToneMapper.Zscale)]
    public async Task Hdr10_IsMappedToSdr(HdrToneMapper toneMapper)
    {
        var path = await FfmpegWithAsync(toneMapper);

        var (black, gray, orange) = await ReadEachSecondAsync(path, Pq, VideoDynamicRange.Hdr10, toneMapper);

        // Black stays black (letterbox bars), a 100-nit gray is near SDR white instead of mid gray, and BT.2020 colors
        // become saturated BT.709 colors instead of pale ones.
        Assert.True(black.R <= 8 && black.G <= 8 && black.B <= 8, $"Black came out as {black}.");
        AssertNeutral(gray);
        Assert.True(gray.R >= 180, $"Gray came out as {gray}; untreated it is 130.");
        Assert.True(orange.R >= 200 && orange.G <= 60 && orange.B <= 40, $"Orange came out as {orange}; untreated it is (175, 71, 36).");
    }

    [Theory]
    [InlineData(HdrToneMapper.Tonemapx)]
    [InlineData(HdrToneMapper.Zscale)]
    public async Task Hlg_IsMappedToSdr(HdrToneMapper toneMapper)
    {
        var path = await FfmpegWithAsync(toneMapper);

        var (black, gray, orange) = await ReadEachSecondAsync(path, Hlg, VideoDynamicRange.Hlg, toneMapper);

        Assert.True(black.R <= 8 && black.G <= 8 && black.B <= 8, $"Black came out as {black}.");
        AssertNeutral(gray);
        Assert.InRange(gray.R, 80, 230);
        Assert.True(orange.R >= 150 && orange.G <= orange.R / 2 && orange.B <= orange.R / 3, $"Orange came out as {orange}.");
    }

    /// <summary>
    /// Manual check on real hardware: set <c>HYPERION_GRABBER_HWACCEL</c> to a <see cref="HardwareAcceleration"/>
    /// name (and <c>HYPERION_GRABBER_HWDEVICE</c> for VA-API/QSV on Linux). Needs libx265 for the HEVC Main 10 clip.
    /// </summary>
    [Fact]
    public async Task Hdr10_OnConfiguredHardware_KeepsTenBitsAndIsMappedToSdr()
    {
        ffmpeg.SkipIfUnavailable();
        var configured = Environment.GetEnvironmentVariable("HYPERION_GRABBER_HWACCEL");
        Assert.SkipWhen(string.IsNullOrEmpty(configured), "Set HYPERION_GRABBER_HWACCEL to test hardware decoding.");
        var acceleration = Enum.Parse<HardwareAcceleration>(configured, ignoreCase: true);
        var toneMapper = await ffmpeg.FindFfmpegWithFilterAsync("tonemapx") is not null ? HdrToneMapper.Tonemapx : HdrToneMapper.Zscale;
        var path = await FfmpegWithAsync(toneMapper);
        var clip = await ffmpeg.GetHevcHdrClipAsync();
        Assert.SkipWhen(clip is null, "This FFmpeg has no libx265 to encode the HEVC Main 10 clip.");

        var options = new FfmpegFrameSourceOptions
        {
            FfmpegPath = path,
            InputPath = clip!,
            SourceWidth = FfmpegFixture.ClipWidth,
            SourceHeight = FfmpegFixture.ClipHeight,
            StartPosition = TimeSpan.FromSeconds(1.5),
            HardwareAcceleration = acceleration,
            HardwareDevice = Environment.GetEnvironmentVariable("HYPERION_GRABBER_HWDEVICE"),
            DynamicRange = VideoDynamicRange.Hdr10,
            ToneMapper = toneMapper,
        };
        await using var source = FfmpegFrameSource.Start(options, TimeProvider.System, NullLogger.Instance);
        using var frame = await source.ReadFrameAsync(Ct);

        Assert.NotNull(frame);
        Assert.Equal(acceleration, source.ActiveAcceleration);
        var pixels = frame.Rgb24.Span;
        var middle = (((frame.Height / 2) * frame.Width) + (frame.Width / 2)) * 3;
        var gray = (pixels[middle], pixels[middle + 1], pixels[middle + 2]);
        AssertNeutral(gray);
        Assert.True(gray.Item1 >= 180, $"Gray came out as {gray}; untreated it is 130.");
    }

    private static void AssertNear((int R, int G, int B) expected, (byte R, byte G, byte B) actual)
        => Assert.True(
            Math.Abs(actual.R - expected.R) <= Tolerance && Math.Abs(actual.G - expected.G) <= Tolerance && Math.Abs(actual.B - expected.B) <= Tolerance,
            $"Expected about {expected}, got {actual}.");

    private static void AssertNeutral((byte R, byte G, byte B) color)
        => Assert.True(Math.Abs(color.R - color.G) <= 8 && Math.Abs(color.G - color.B) <= 8, $"Gray came out tinted: {color}.");

    /// <summary>Returns the middle pixel of the frames at 0.5, 1.5 and 2.5 s.</summary>
    private async Task<((byte R, byte G, byte B) Black, (byte R, byte G, byte B) Gray, (byte R, byte G, byte B) Orange)> ReadEachSecondAsync(
        string ffmpegPath, string transfer, VideoDynamicRange range, HdrToneMapper toneMapper)
    {
        var options = new FfmpegFrameSourceOptions
        {
            FfmpegPath = ffmpegPath,
            InputPath = ffmpeg.HdrClipPath(transfer),
            SourceWidth = FfmpegFixture.ClipWidth,
            SourceHeight = FfmpegFixture.ClipHeight,
            DynamicRange = range,
            ToneMapper = toneMapper,
        };
        await using var source = FfmpegFrameSource.Start(options, TimeProvider.System, NullLogger.Instance);
        var colors = new (byte R, byte G, byte B)[FfmpegFixture.HdrCodes.Length];
        while (await source.ReadFrameAsync(Ct) is { } frame)
        {
            using (frame)
            {
                if (frame.Index % options.FramesPerSecond == options.FramesPerSecond / 2)
                {
                    var pixels = frame.Rgb24.Span;
                    var middle = (((frame.Height / 2) * frame.Width) + (frame.Width / 2)) * 3;
                    colors[frame.Index / options.FramesPerSecond] = (pixels[middle], pixels[middle + 1], pixels[middle + 2]);
                }
            }
        }

        return (colors[0], colors[1], colors[2]);
    }

    private async Task<string> FfmpegWithAsync(HdrToneMapper toneMapper)
    {
        ffmpeg.SkipIfUnavailable();
        var filter = toneMapper == HdrToneMapper.Tonemapx ? "tonemapx" : "zscale";
        var path = await ffmpeg.FindFfmpegWithFilterAsync(filter);

        // Only Jellyfin's FFmpeg has tonemapx; CI installs it and requires every tone mapper to be tested.
        var required = toneMapper == HdrToneMapper.Tonemapx ? ffmpeg.JellyfinFfmpegPath is not null : FfmpegFixture.IsRequired;
        Assert.False(path is null && required, $"No FFmpeg with the {filter} filter was found, but tone mapping tests are required.");
        Assert.SkipWhen(path is null, $"No FFmpeg with the {filter} filter (set HYPERION_GRABBER_JELLYFIN_FFMPEG to Jellyfin's FFmpeg for tonemapx).");
        return path!;
    }
}
