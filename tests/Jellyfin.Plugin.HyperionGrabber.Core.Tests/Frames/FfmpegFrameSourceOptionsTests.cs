using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Frames;

public class FfmpegFrameSourceOptionsTests
{
    private static readonly FfmpegFrameSourceOptions Valid = new()
    {
        FfmpegPath = "ffmpeg",
        InputPath = "movie.mkv",
        SourceWidth = 1920,
        SourceHeight = 1080,
    };

    [Fact]
    public void Defaults_Are160PixelsWide25FpsThreeBuffersOnTheCpu()
    {
        Assert.Equal(160, Valid.OutputWidth);
        Assert.Equal(25, Valid.FramesPerSecond);
        Assert.Equal(3, Valid.BufferCount);
        Assert.Equal(HardwareAcceleration.None, Valid.HardwareAcceleration);
        Assert.Empty(Valid.GetValidationErrors());
    }

    [Theory]
    [InlineData(1920, 1080, 160, 90)]
    [InlineData(3840, 2160, 160, 90)]
    [InlineData(3840, 1600, 160, 66)]
    [InlineData(1440, 1080, 160, 120)]
    [InlineData(1998, 1080, 160, 86)]
    [InlineData(1080, 1920, 160, 284)]
    [InlineData(4000, 10, 160, 2)]
    public void OutputHeight_KeepsTheAspectRatioAndIsEven(int sourceWidth, int sourceHeight, int width, int expectedHeight)
    {
        var options = Valid with { SourceWidth = sourceWidth, SourceHeight = sourceHeight, OutputWidth = width };

        Assert.Equal(expectedHeight, options.OutputHeight);
        Assert.Equal(width * expectedHeight * 3, options.FrameLength);
    }

    [Theory]
    [InlineData("FfmpegPath")]
    [InlineData("InputPath")]
    [InlineData("InputLooksLikeAnOption")]
    [InlineData("SourceSize")]
    [InlineData("StartPosition")]
    [InlineData("OddWidth")]
    [InlineData("TinyWidth")]
    [InlineData("HugeWidth")]
    [InlineData("ZeroFps")]
    [InlineData("HighFps")]
    [InlineData("OneBuffer")]
    [InlineData("ManyBuffers")]
    [InlineData("UnknownAcceleration")]
    [InlineData("ExtremeAspect")]
    public void GetValidationErrors_ReportsEachInvalidValue(string problem)
    {
        var options = problem switch
        {
            "FfmpegPath" => Valid with { FfmpegPath = " " },
            "InputPath" => Valid with { InputPath = string.Empty },
            "InputLooksLikeAnOption" => Valid with { InputPath = "-filter_complex" },
            "SourceSize" => Valid with { SourceHeight = 0 },
            "StartPosition" => Valid with { StartPosition = TimeSpan.FromSeconds(-1) },
            "OddWidth" => Valid with { OutputWidth = 161 },
            "TinyWidth" => Valid with { OutputWidth = 8 },
            "HugeWidth" => Valid with { OutputWidth = 1920 },
            "ZeroFps" => Valid with { FramesPerSecond = 0 },
            "HighFps" => Valid with { FramesPerSecond = 61 },
            "OneBuffer" => Valid with { BufferCount = 1 },
            "ManyBuffers" => Valid with { BufferCount = 17 },
            "UnknownAcceleration" => Valid with { HardwareAcceleration = (HardwareAcceleration)99 },
            "ExtremeAspect" => Valid with { SourceWidth = 10, SourceHeight = 10000 },
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        Assert.Single(options.GetValidationErrors());
    }

    [Fact]
    public void Create_TakesPathAndHardwareFromTheSettingsAndTheVideo()
    {
        var settings = new FfmpegSettings
        {
            FfmpegPath = "/usr/lib/jellyfin-ffmpeg/ffmpeg",
            HardwareAcceleration = HardwareAcceleration.Qsv,
            HardwareDevice = "/dev/dri/renderD128",
            HardwareDecodingCodecs = ["h264", "hevc"],
        };

        var options = FfmpegFrameSourceOptions.Create(settings, new VideoInput("/media/a.mkv", 3840, 2160, "hevc"), TimeSpan.FromMinutes(12));

        Assert.Equal("/usr/lib/jellyfin-ffmpeg/ffmpeg", options.FfmpegPath);
        Assert.Equal("/media/a.mkv", options.InputPath);
        Assert.Equal((3840, 2160), (options.SourceWidth, options.SourceHeight));
        Assert.Equal(TimeSpan.FromMinutes(12), options.StartPosition);
        Assert.Equal(HardwareAcceleration.Qsv, options.HardwareAcceleration);
        Assert.Equal("/dev/dri/renderD128", options.HardwareDevice);
        Assert.Empty(options.GetValidationErrors());
    }

    [Fact]
    public void Create_ForACodecWithoutHardwareDecoding_UsesTheCpu()
    {
        var settings = new FfmpegSettings
        {
            FfmpegPath = "ffmpeg",
            HardwareAcceleration = HardwareAcceleration.Vaapi,
            HardwareDevice = "/dev/dri/renderD128",
            HardwareDecodingCodecs = ["h264"],
        };

        var options = FfmpegFrameSourceOptions.Create(settings, new VideoInput("/media/a.mkv", 1920, 1080, "av1"), TimeSpan.Zero);

        Assert.Equal(HardwareAcceleration.None, options.HardwareAcceleration);
        Assert.Null(options.HardwareDevice);
    }
}
