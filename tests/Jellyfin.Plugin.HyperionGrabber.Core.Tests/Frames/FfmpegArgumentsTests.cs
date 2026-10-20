using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Frames;

public class FfmpegArgumentsTests
{
    private const string PqTags = "setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc";
    private const string HlgTags = "setparams=color_primaries=bt2020:color_trc=arib-std-b67:colorspace=bt2020nc";
    private const string Tonemapx = "tonemapx=tonemap=bt2390:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=yuv420p";
    private const string Zscale = "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=mobius:desat=0:peak=10,zscale=t=bt709:m=bt709:r=tv,format=yuv420p";

    private static readonly FfmpegFrameSourceOptions Options = new()
    {
        FfmpegPath = "/usr/lib/jellyfin-ffmpeg/ffmpeg",
        InputPath = "/media/movies/A Film (2024)/A Film.mkv",
        SourceWidth = 3840,
        SourceHeight = 2160,
        StartPosition = TimeSpan.FromSeconds(723.25),
    };

    [Fact]
    public void Build_Cpu_SeeksDecodesScalesAndWritesRawRgbToStdout()
    {
        var args = FfmpegArguments.Build(Options, HardwareAcceleration.None, isWindows: false);

        Assert.Equal(
            [
                "-nostdin", "-hide_banner", "-nostats", "-loglevel", "error",
                "-ss", "723.25",
                "-i", "/media/movies/A Film (2024)/A Film.mkv",
                "-map", "0:v:0", "-an", "-sn", "-dn",
                "-vf", "fps=25,scale=160:90:flags=area,format=rgb24",
                "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1",
            ],
            args);
    }

    [Fact]
    public void Build_AtTheStart_OmitsTheSeek()
    {
        var args = FfmpegArguments.Build(Options with { StartPosition = TimeSpan.Zero }, HardwareAcceleration.None, isWindows: false);

        Assert.DoesNotContain("-ss", args);
    }

    [Fact]
    public void Build_FormatsTheSeekPositionIndependentOfTheCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var args = FfmpegArguments.Build(Options, HardwareAcceleration.None, isWindows: false);

            Assert.Equal("723.25", ValueAfter(args, "-ss"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Build_UsesTheConfiguredRateAndSize()
    {
        var options = Options with { FramesPerSecond = 30, OutputWidth = 96, SourceWidth = 1440, SourceHeight = 1080 };

        var args = FfmpegArguments.Build(options, HardwareAcceleration.None, isWindows: false);

        Assert.Equal("fps=30,scale=96:72:flags=area,format=rgb24", ValueAfter(args, "-vf"));
    }

    [Theory]
    [InlineData(HardwareAcceleration.Nvenc, false, "-hwaccel cuda -hwaccel_output_format cuda", "fps=25,scale_cuda=160:90:format=yuv420p,hwdownload,format=yuv420p,format=rgb24")]
    [InlineData(HardwareAcceleration.Vaapi, false, "-vaapi_device /dev/dri/renderD128 -hwaccel vaapi -hwaccel_output_format vaapi", "fps=25,scale_vaapi=w=160:h=90:format=nv12,hwdownload,format=nv12,format=rgb24")]
    [InlineData(HardwareAcceleration.Qsv, false, "-init_hw_device vaapi=va:/dev/dri/renderD128 -init_hw_device qsv=qs@va -hwaccel qsv -hwaccel_output_format qsv", "fps=25,scale_qsv=w=160:h=90:format=nv12,hwdownload,format=nv12,format=rgb24")]
    [InlineData(HardwareAcceleration.Qsv, true, "-hwaccel qsv -hwaccel_output_format qsv", "fps=25,scale_qsv=w=160:h=90:format=nv12,hwdownload,format=nv12,format=rgb24")]
    [InlineData(HardwareAcceleration.Amf, true, "-hwaccel d3d11va", "fps=25,scale=160:90:flags=area,format=rgb24")]
    [InlineData(HardwareAcceleration.Amf, false, "-vaapi_device /dev/dri/renderD128 -hwaccel vaapi -hwaccel_output_format vaapi", "fps=25,scale_vaapi=w=160:h=90:format=nv12,hwdownload,format=nv12,format=rgb24")]
    [InlineData(HardwareAcceleration.VideoToolbox, false, "-hwaccel videotoolbox", "fps=25,scale=160:90:flags=area,format=rgb24")]
    [InlineData(HardwareAcceleration.Rkmpp, false, "-hwaccel rkmpp -hwaccel_output_format drm_prime", "fps=25,scale_rkrga=w=160:h=90:format=nv12:afbc=0,hwdownload,format=nv12,format=rgb24")]
    public void Build_Hardware_DecodesAndScalesOnTheDevice(HardwareAcceleration acceleration, bool isWindows, string inputArguments, string filter)
    {
        var options = Options with { HardwareAcceleration = acceleration, HardwareDevice = "/dev/dri/renderD128" };

        var args = FfmpegArguments.Build(options, acceleration, isWindows);

        var expectedPrefix = "-nostdin -hide_banner -nostats -loglevel error " + inputArguments + " -ss 723.25";
        Assert.StartsWith(expectedPrefix + " ", string.Join(' ', args), StringComparison.Ordinal);
        Assert.Equal(filter, ValueAfter(args, "-vf"));
    }

    [Fact]
    public void Build_HdrOnTheCpu_TonemapsTheScaledPictureWithTonemapx()
    {
        var options = Options with { DynamicRange = VideoDynamicRange.Hdr10, ToneMapper = HdrToneMapper.Tonemapx };

        var args = FfmpegArguments.Build(options, HardwareAcceleration.None, isWindows: false);

        Assert.Equal($"fps=25,scale=160:90:flags=area,format=yuv420p10le,{PqTags},{Tonemapx},format=rgb24", ValueAfter(args, "-vf"));
    }

    [Fact]
    public void Build_HlgWithZscale_TagsTheHlgTransferAndTonemapsWithZscale()
    {
        var options = Options with { DynamicRange = VideoDynamicRange.Hlg, ToneMapper = HdrToneMapper.Zscale };

        var args = FfmpegArguments.Build(options, HardwareAcceleration.None, isWindows: false);

        Assert.Equal($"fps=25,scale=160:90:flags=area,format=yuv420p10le,{HlgTags},{Zscale},format=rgb24", ValueAfter(args, "-vf"));
    }

    [Theory]
    [InlineData(HardwareAcceleration.Nvenc, false, "fps=25,scale_cuda=160:90:format=p010,hwdownload,format=p010")]
    [InlineData(HardwareAcceleration.Vaapi, false, "fps=25,scale_vaapi=w=160:h=90:format=p010,hwdownload,format=p010")]
    [InlineData(HardwareAcceleration.Qsv, false, "fps=25,scale_qsv=w=160:h=90:format=p010,hwdownload,format=p010")]
    [InlineData(HardwareAcceleration.Qsv, true, "fps=25,scale_qsv=w=160:h=90:format=p010,hwdownload,format=p010")]
    [InlineData(HardwareAcceleration.Amf, false, "fps=25,scale_vaapi=w=160:h=90:format=p010,hwdownload,format=p010")]
    [InlineData(HardwareAcceleration.Rkmpp, false, "fps=25,scale_rkrga=w=160:h=90:format=p010:afbc=0,hwdownload,format=p010")]
    [InlineData(HardwareAcceleration.Amf, true, "fps=25,scale=160:90:flags=area,format=yuv420p10le")]
    [InlineData(HardwareAcceleration.VideoToolbox, false, "fps=25,scale=160:90:flags=area,format=yuv420p10le")]
    public void Build_HdrOnHardware_KeepsTenBitsThroughTheScalerAndTonemapsAfterTheDownload(HardwareAcceleration acceleration, bool isWindows, string scaling)
    {
        var options = Options with
        {
            HardwareAcceleration = acceleration,
            HardwareDevice = "/dev/dri/renderD128",
            DynamicRange = VideoDynamicRange.Hdr10,
            ToneMapper = HdrToneMapper.Tonemapx,
        };

        var args = FfmpegArguments.Build(options, acceleration, isWindows);

        Assert.Equal($"{scaling},{PqTags},{Tonemapx},format=rgb24", ValueAfter(args, "-vf"));
    }

    [Theory]
    [InlineData(HardwareAcceleration.None)]
    [InlineData(HardwareAcceleration.Vaapi)]
    [InlineData(HardwareAcceleration.Nvenc)]
    public void Build_HdrWithoutAToneMapper_DecodesAsBefore(HardwareAcceleration acceleration)
    {
        var options = Options with { HardwareAcceleration = acceleration, DynamicRange = VideoDynamicRange.Hdr10 };

        var args = FfmpegArguments.Build(options, acceleration, isWindows: false);

        Assert.Equal(FfmpegArguments.Build(Options with { HardwareAcceleration = acceleration }, acceleration, isWindows: false), args);
    }

    [Fact]
    public void Build_Sdr_IsNotTonemapped()
    {
        var options = Options with { ToneMapper = HdrToneMapper.Tonemapx };

        var args = FfmpegArguments.Build(options, HardwareAcceleration.None, isWindows: false);

        Assert.Equal("fps=25,scale=160:90:flags=area,format=rgb24", ValueAfter(args, "-vf"));
    }

    [Fact]
    public void Build_DolbyVision_TagsPqForTonemapxWhichAppliesTheRpu()
    {
        var options = Options with { DynamicRange = VideoDynamicRange.DolbyVision, ToneMapper = HdrToneMapper.Tonemapx };

        var args = FfmpegArguments.Build(options, HardwareAcceleration.None, isWindows: false);

        // format=yuv420p makes tonemapx take planar 10-bit input, which its Dolby Vision reshaping needs.
        Assert.Equal($"fps=25,scale=160:90:flags=area,format=yuv420p10le,{PqTags},{Tonemapx},format=rgb24", ValueAfter(args, "-vf"));
    }

    [Theory]
    [InlineData(false, "-vaapi_device /dev/dri/renderD128 -hwaccel vaapi -hwaccel_output_format vaapi", "fps=25,scale_vaapi=w=160:h=90:format=p010,hwdownload,format=p010")]
    [InlineData(true, "-hwaccel d3d11va", "fps=25,scale=160:90:flags=area,format=yuv420p10le")]
    public void Build_DolbyVisionOnQuickSync_DecodesWithTheSystemDecoderThatKeepsTheRpu(bool isWindows, string inputArguments, string scaling)
    {
        var options = Options with
        {
            HardwareAcceleration = HardwareAcceleration.Qsv,
            HardwareDevice = "/dev/dri/renderD128",
            DynamicRange = VideoDynamicRange.DolbyVision,
            ToneMapper = HdrToneMapper.Tonemapx,
        };

        var args = FfmpegArguments.Build(options, HardwareAcceleration.Qsv, isWindows);

        Assert.StartsWith("-nostdin -hide_banner -nostats -loglevel error " + inputArguments + " -ss ", string.Join(' ', args), StringComparison.Ordinal);
        Assert.Equal($"{scaling},{PqTags},{Tonemapx},format=rgb24", ValueAfter(args, "-vf"));
    }

    [Theory]
    [InlineData(VideoDynamicRange.Hdr10, HdrToneMapper.Tonemapx)]
    [InlineData(VideoDynamicRange.DolbyVision, HdrToneMapper.Zscale)]
    public void Build_OtherwiseOnQuickSync_StaysOnQuickSync(VideoDynamicRange range, HdrToneMapper toneMapper)
    {
        var options = Options with { HardwareAcceleration = HardwareAcceleration.Qsv, DynamicRange = range, ToneMapper = toneMapper };

        var args = FfmpegArguments.Build(options, HardwareAcceleration.Qsv, isWindows: false);

        Assert.Equal("qsv", ValueAfter(args, "-hwaccel"));
    }

    [Fact]
    public void Build_VaapiWithoutDevice_LetsFfmpegPickTheDevice()
    {
        var args = FfmpegArguments.Build(Options, HardwareAcceleration.Vaapi, isWindows: false);

        Assert.DoesNotContain("-vaapi_device", args);
        Assert.Equal("vaapi", ValueAfter(args, "-hwaccel"));
    }

    [Fact]
    public void Build_UsesTheAttemptsAccelerationNotTheConfiguredOne()
    {
        var options = Options with { HardwareAcceleration = HardwareAcceleration.Nvenc };

        var args = FfmpegArguments.Build(options, HardwareAcceleration.None, isWindows: false);

        Assert.DoesNotContain("-hwaccel", args);
    }

    private static string ValueAfter(List<string> args, string name)
    {
        var index = args.IndexOf(name);
        Assert.InRange(index, 0, args.Count - 2);
        return args[index + 1];
    }
}
