using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.HyperionGrabber.Configuration;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Configuration;

public class PluginConfigurationTests
{
    private static readonly XmlReaderSettings SafeSettings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    [Fact]
    public void Defaults_MatchHyperion()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(string.Empty, configuration.HyperionHost);
        Assert.Equal(19400, configuration.HyperionPort);
        Assert.Equal(150, configuration.HyperionPriority);
    }

    [Fact]
    public void RoundTripsThroughXmlSerializer()
    {
        // Jellyfin persists plugin configuration with System.Xml.Serialization.
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        var original = new PluginConfiguration { HyperionHost = "192.168.1.20", HyperionPort = 19401, HyperionPriority = 120 };
        using var stream = new MemoryStream();

        serializer.Serialize(stream, original);
        stream.Position = 0;
        using var reader = XmlReader.Create(stream, SafeSettings);
        var copy = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));

        Assert.Equal(original.HyperionHost, copy.HyperionHost);
        Assert.Equal(original.HyperionPort, copy.HyperionPort);
        Assert.Equal(original.HyperionPriority, copy.HyperionPriority);
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
    }
}
