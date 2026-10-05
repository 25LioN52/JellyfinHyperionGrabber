using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Frames;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Frames;

public class JellyfinFfmpegSettingsProviderTests
{
    private readonly IMediaEncoder _mediaEncoder = Substitute.For<IMediaEncoder>();
    private readonly IServerConfigurationManager _configuration = Substitute.For<IServerConfigurationManager>();
    private readonly EncodingOptions _encoding = new();

    public JellyfinFfmpegSettingsProviderTests()
    {
        _mediaEncoder.EncoderPath.Returns("/usr/lib/jellyfin-ffmpeg/ffmpeg");
        _configuration.GetConfiguration("encoding").Returns(_encoding);
    }

    [Fact]
    public void GetSettings_UsesJellyfinsFfmpegAndHardwareSettings()
    {
        _encoding.HardwareAccelerationType = HardwareAccelerationType.qsv;
        _encoding.VaapiDevice = "/dev/dri/renderD129";
        _encoding.HardwareDecodingCodecs = ["h264", "hevc", "av1"];

        var settings = Provider().GetSettings();

        Assert.Equal("/usr/lib/jellyfin-ffmpeg/ffmpeg", settings.FfmpegPath);
        Assert.Equal(HardwareAcceleration.Qsv, settings.HardwareAcceleration);
        Assert.Equal("/dev/dri/renderD129", settings.HardwareDevice);
        Assert.Equal(["h264", "hevc", "av1"], settings.HardwareDecodingCodecs);
    }

    [Fact]
    public void GetSettings_ReadsTheCurrentConfigurationEachTime()
    {
        var provider = Provider();
        _encoding.HardwareAccelerationType = HardwareAccelerationType.none;
        Assert.Equal(HardwareAcceleration.None, provider.GetSettings().HardwareAcceleration);

        _encoding.HardwareAccelerationType = HardwareAccelerationType.nvenc;

        Assert.Equal(HardwareAcceleration.Nvenc, provider.GetSettings().HardwareAcceleration);
    }

    [Fact]
    public void GetSettings_ForNvidia_HasNoDevice()
    {
        _encoding.HardwareAccelerationType = HardwareAccelerationType.nvenc;
        _encoding.VaapiDevice = "/dev/dri/renderD128";

        Assert.Null(Provider().GetSettings().HardwareDevice);
    }

    [Fact]
    public void GetSettings_WithoutFfmpeg_ReturnsAnEmptyPathThatFailsValidation()
    {
        _mediaEncoder.EncoderPath.Returns((string?)null);

        var settings = Provider().GetSettings();
        var options = FfmpegFrameSourceOptions.Create(settings, new VideoInput("/media/a.mkv", 1920, 1080, "h264"), TimeSpan.Zero);

        Assert.Equal(string.Empty, settings.FfmpegPath);
        Assert.Contains("FFmpeg path is not configured.", options.GetValidationErrors());
    }

    [Theory]
    [InlineData(HardwareAccelerationType.none, HardwareAcceleration.None)]
    [InlineData(HardwareAccelerationType.amf, HardwareAcceleration.Amf)]
    [InlineData(HardwareAccelerationType.qsv, HardwareAcceleration.Qsv)]
    [InlineData(HardwareAccelerationType.nvenc, HardwareAcceleration.Nvenc)]
    [InlineData(HardwareAccelerationType.vaapi, HardwareAcceleration.Vaapi)]
    [InlineData(HardwareAccelerationType.videotoolbox, HardwareAcceleration.VideoToolbox)]
    [InlineData(HardwareAccelerationType.rkmpp, HardwareAcceleration.Rkmpp)]
    [InlineData(HardwareAccelerationType.v4l2m2m, HardwareAcceleration.None)]
    public void Map_TranslatesEveryJellyfinType(HardwareAccelerationType type, HardwareAcceleration expected)
    {
        Assert.Equal(expected, JellyfinFfmpegSettingsProvider.Map(type));
    }

    [Fact]
    public void Map_CoversEveryValueJellyfinDefines()
    {
        foreach (var type in Enum.GetValues<HardwareAccelerationType>())
        {
            var mapped = JellyfinFfmpegSettingsProvider.Map(type);

            Assert.True(mapped != HardwareAcceleration.None || type is HardwareAccelerationType.none or HardwareAccelerationType.v4l2m2m, $"{type} is not mapped.");
        }
    }

    [Fact]
    public void RegisterServices_RegistersTheProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_mediaEncoder);
        services.AddSingleton(_configuration);

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<JellyfinFfmpegSettingsProvider>(provider.GetRequiredService<IFfmpegSettingsProvider>());
    }

    private JellyfinFfmpegSettingsProvider Provider() => new(_mediaEncoder, _configuration);
}
