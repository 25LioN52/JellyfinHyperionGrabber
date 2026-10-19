using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.HyperionGrabber.Configuration;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Configuration;

public class PluginConfigurationTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly XmlReaderSettings SafeSettings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    [Fact]
    public void Defaults_MatchHyperion()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(string.Empty, configuration.HyperionHost);
        Assert.Equal(19400, configuration.HyperionPort);
        Assert.Equal(150, configuration.HyperionPriority);
        Assert.Equal(25, configuration.FramesPerSecond);
        Assert.Equal(0, configuration.LatencyOffsetMilliseconds);
        Assert.Equal(15, configuration.PauseReleaseSeconds);
        Assert.True(configuration.KodiClockEnabled);
        Assert.Equal(9090, configuration.KodiJsonRpcPort);
        Assert.False(configuration.PlaybackEnabled);
        Assert.Empty(configuration.PlaybackDevices);
        Assert.Empty(configuration.PlaybackUsers);
        Assert.Equal(string.Empty, configuration.HyperionName);
        Assert.Empty(configuration.AdditionalTargets);
    }

    [Fact]
    public void RoundTripsThroughXmlSerializer()
    {
        // Jellyfin persists plugin configuration with System.Xml.Serialization.
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        var original = new PluginConfiguration
        {
            HyperionHost = "192.168.1.20",
            HyperionPort = 19401,
            HyperionPriority = 120,
            FramesPerSecond = 30,
            LatencyOffsetMilliseconds = -250,
            PauseReleaseSeconds = 0,
            KodiClockEnabled = false,
            KodiJsonRpcPort = 9091,
            PlaybackEnabled = true,
            PlaybackDevices = [new PlaybackDeviceSelection { Id = "kodi-device", Name = "Living room" }],
            PlaybackUsers = [new PlaybackUserSelection { Id = Alice, Name = "alice" }],
        };
        using var stream = new MemoryStream();

        serializer.Serialize(stream, original);
        stream.Position = 0;
        using var reader = XmlReader.Create(stream, SafeSettings);
        var copy = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));

        Assert.Equal(original.HyperionHost, copy.HyperionHost);
        Assert.Equal(original.HyperionPort, copy.HyperionPort);
        Assert.Equal(original.HyperionPriority, copy.HyperionPriority);
        Assert.Equal(30, copy.FramesPerSecond);
        Assert.Equal(-250, copy.LatencyOffsetMilliseconds);
        Assert.Equal(0, copy.PauseReleaseSeconds);
        Assert.False(copy.KodiClockEnabled);
        Assert.Equal(9091, copy.KodiJsonRpcPort);
        Assert.True(copy.PlaybackEnabled);
        var device = Assert.Single(copy.PlaybackDevices);
        Assert.Equal(("kodi-device", "Living room"), (device.Id, device.Name));
        var user = Assert.Single(copy.PlaybackUsers);
        Assert.Equal((Alice, "alice"), (user.Id, user.Name));
    }

    [Fact]
    public void MissingElements_KeepDefaults()
    {
        // Older configuration files must keep loading when properties are added.
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var text = new StringReader("<PluginConfiguration><HyperionHost>hyperion.local</HyperionHost></PluginConfiguration>");
        using var reader = XmlReader.Create(text, SafeSettings);

        var configuration = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));

        Assert.Equal("hyperion.local", configuration.HyperionHost);
        Assert.Equal(19400, configuration.HyperionPort);
        Assert.Equal(150, configuration.HyperionPriority);
        Assert.Equal(25, configuration.FramesPerSecond);
        Assert.Equal(0, configuration.LatencyOffsetMilliseconds);
        Assert.Equal(15, configuration.PauseReleaseSeconds);
        Assert.True(configuration.KodiClockEnabled); // Saved before 0.7: Kodi's clock is used.
        Assert.Equal(9090, configuration.KodiJsonRpcPort);
        Assert.False(configuration.PlaybackEnabled);
        Assert.Empty(configuration.PlaybackDevices);
        Assert.Empty(configuration.PlaybackUsers);
        Assert.Empty(configuration.AdditionalTargets); // Saved before 0.8: one Hyperion server.
    }

    [Fact]
    public void ConfigurationFrom07_LoadsAsOneTargetWithItsDevices()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var text = new StringReader(
            "<PluginConfiguration><HyperionHost>hyperion.local</HyperionHost><HyperionPriority>140</HyperionPriority><PlaybackEnabled>true</PlaybackEnabled>"
            + "<PlaybackDevices><PlaybackDeviceSelection><Id>kodi-device</Id><Name>Living room</Name></PlaybackDeviceSelection></PlaybackDevices></PluginConfiguration>");
        using var reader = XmlReader.Create(text, SafeSettings);
        var configuration = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));

        var filter = configuration.ToPlaybackFilter();

        var target = Assert.Single(filter.Targets);
        Assert.Equal("hyperion.local:19400", target.Name);
        Assert.Equal(new PlaybackTargetKey("hyperion.local", 19400, 140), target.Key);
        Assert.Equal(["kodi-device"], target.DeviceIds);
        Assert.True(filter.Matches(target, "kodi-device", Alice));
    }

    [Fact]
    public void AdditionalTargets_RoundTripThroughXmlSerializer()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        var original = new PluginConfiguration
        {
            HyperionHost = "192.168.1.20",
            HyperionName = "Living room",
            AdditionalTargets =
            [
                new HyperionTargetConfiguration
                {
                    Name = "Bedroom",
                    Host = "192.168.1.21",
                    Port = 19401,
                    Priority = 160,
                    Devices = [new PlaybackDeviceSelection { Id = "shield", Name = "Bedroom TV" }],
                },
            ],
        };
        using var stream = new MemoryStream();

        serializer.Serialize(stream, original);
        stream.Position = 0;
        using var reader = XmlReader.Create(stream, SafeSettings);
        var copy = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));

        Assert.Equal("Living room", copy.HyperionName);
        var target = Assert.Single(copy.AdditionalTargets);
        Assert.Equal(("Bedroom", "192.168.1.21", 19401, 160), (target.Name, target.Host, target.Port, target.Priority));
        Assert.Equal(("shield", "Bedroom TV"), (Assert.Single(target.Devices).Id, target.Devices[0].Name));
    }

    [Fact]
    public void AdditionalTargets_ReadFromTheConfigurationPagesJson()
    {
        // The page posts the configuration as PascalCase JSON; a new target may omit port and priority.
        var json = """{"HyperionHost":"tv1.local","AdditionalTargets":[{"Name":"Bedroom","Host":"tv2.local","Devices":[{"Id":"shield","Name":"Bedroom TV"}]}]}""";

        var configuration = JsonSerializer.Deserialize<PluginConfiguration>(json)!;

        var target = Assert.Single(configuration.AdditionalTargets);
        Assert.Equal(("Bedroom", "tv2.local", 19400, 150), (target.Name, target.Host, target.Port, target.Priority));
        Assert.Equal("shield", Assert.Single(target.Devices).Id);
    }

    [Fact]
    public void ToPlaybackFilter_HasTheFirstServerThenEachAdditionalOneWithItsDevices()
    {
        var configuration = new PluginConfiguration
        {
            PlaybackEnabled = true,
            HyperionHost = " tv1.local ",
            HyperionName = "Living room",
            PlaybackDevices = [new PlaybackDeviceSelection { Id = "kodi" }],
            AdditionalTargets =
            [
                new HyperionTargetConfiguration { Name = "Bedroom", Host = "tv2.local", Port = 19401, Priority = 120, Devices = [new PlaybackDeviceSelection { Id = "shield" }] },
                new HyperionTargetConfiguration { Host = "tv3.local", Devices = [new PlaybackDeviceSelection { Id = "kodi" }] },
            ],
        };

        var targets = configuration.ToPlaybackFilter().Targets;

        Assert.Equal(["Living room", "Bedroom", "tv3.local:19400"], targets.Select(t => t.Name));
        Assert.Equal(("tv1.local", 19400, 150), (targets[0].Hyperion?.Host, targets[0].Hyperion?.Port, targets[0].Hyperion?.Priority));
        Assert.Equal(("tv2.local", 19401, 120), (targets[1].Hyperion?.Host, targets[1].Hyperion?.Port, targets[1].Hyperion?.Priority));
        Assert.Equal(["kodi"], targets[0].DeviceIds);
        Assert.Equal(["shield"], targets[1].DeviceIds);
        Assert.Equal(["kodi"], targets[2].DeviceIds);
    }

    [Fact]
    public void ToPlaybackFilter_KeepsTheFirstServerWithoutAHost_AndSkipsAdditionalOnesWithout()
    {
        // The first server without a host still appears, so the status can say it is missing; an incomplete
        // additional entry is ignored.
        var configuration = new PluginConfiguration
        {
            AdditionalTargets = [new HyperionTargetConfiguration { Name = "Unfinished", Host = " " }, null!],
        };

        var target = Assert.Single(configuration.ToPlaybackFilter().Targets);

        Assert.Null(target.Hyperion);
        Assert.Equal(PlaybackTargetKey.None, target.Key);
    }

    [Fact]
    public void ToStreamingSettings_UsesTheSavedFrameRate()
    {
        var configuration = new PluginConfiguration { HyperionHost = "hyperion.local", FramesPerSecond = 30 };

        var settings = configuration.ToStreamingSettings();

        Assert.Equal(30, settings.FramesPerSecond);
    }

    [Fact]
    public void ToStreamingSettings_UsesTheTimingSettings()
    {
        var configuration = new PluginConfiguration { HyperionHost = "hyperion.local", LatencyOffsetMilliseconds = 800, PauseReleaseSeconds = 30 };

        var settings = configuration.ToStreamingSettings();

        Assert.Equal(TimeSpan.FromMilliseconds(800), settings.LatencyOffset);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.PauseRelease);
        Assert.Empty(settings.GetValidationErrors());
    }

    [Fact]
    public void ToStreamingSettings_UsesTheKodiSettings()
    {
        var configuration = new PluginConfiguration { HyperionHost = "hyperion.local", KodiClockEnabled = false, KodiJsonRpcPort = 9091 };

        var settings = configuration.ToStreamingSettings();

        Assert.False(settings.KodiClock);
        Assert.Equal(9091, settings.KodiPort);
    }

    [Fact]
    public void ToPlaybackFilter_UsesSelectedDevicesAndUsers()
    {
        var configuration = new PluginConfiguration
        {
            PlaybackEnabled = true,
            PlaybackDevices = [new PlaybackDeviceSelection { Id = "kodi-device" }],
            PlaybackUsers = [new PlaybackUserSelection { Id = Alice }],
        };

        var filter = configuration.ToPlaybackFilter();

        Assert.True(filter.Matches("kodi-device", Alice));
        Assert.False(filter.Matches("kodi-device", Guid.NewGuid()));
        Assert.False(filter.Matches("phone", Alice));
    }

    [Fact]
    public void ToPlaybackFilter_ByDefault_MatchesNothing()
    {
        var filter = new PluginConfiguration().ToPlaybackFilter();

        Assert.False(filter.Enabled);
        Assert.Empty(filter.DeviceIds);
    }
}
