using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.HyperionGrabber.Configuration;
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
        Assert.False(configuration.PlaybackEnabled);
        Assert.Empty(configuration.PlaybackDevices);
        Assert.Empty(configuration.PlaybackUsers);
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
        Assert.False(configuration.PlaybackEnabled);
        Assert.Empty(configuration.PlaybackDevices);
        Assert.Empty(configuration.PlaybackUsers);
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
