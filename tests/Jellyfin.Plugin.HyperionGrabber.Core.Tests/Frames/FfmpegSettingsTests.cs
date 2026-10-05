using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Frames;

public class FfmpegSettingsTests
{
    private static readonly FfmpegSettings Nvidia = new()
    {
        FfmpegPath = "ffmpeg",
        HardwareAcceleration = HardwareAcceleration.Nvenc,
        HardwareDecodingCodecs = ["h264", "HEVC"],
    };

    [Theory]
    [InlineData("h264")]
    [InlineData("hevc")]
    [InlineData("H264")]
    public void GetAcceleration_ForAnEnabledCodec_UsesTheHardware(string codec)
    {
        Assert.Equal(HardwareAcceleration.Nvenc, Nvidia.GetAcceleration(codec));
    }

    [Fact]
    public void GetAcceleration_ForACodecTheAdministratorDidNotEnable_UsesTheCpu()
    {
        Assert.Equal(HardwareAcceleration.None, Nvidia.GetAcceleration("av1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetAcceleration_ForAnUnknownCodec_TriesTheHardware(string? codec)
    {
        Assert.Equal(HardwareAcceleration.Nvenc, Nvidia.GetAcceleration(codec));
    }

    [Fact]
    public void GetAcceleration_WithoutHardwareAcceleration_UsesTheCpu()
    {
        var settings = Nvidia with { HardwareAcceleration = HardwareAcceleration.None };

        Assert.Equal(HardwareAcceleration.None, settings.GetAcceleration("h264"));
    }
}
