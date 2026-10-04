using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Hyperion;

public class HyperionClientOptionsTests
{
    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("hyperion.local")]
    [InlineData("localhost")]
    [InlineData("fe80::1")]
    public void ValidHosts_HaveNoErrors(string host)
    {
        Assert.Empty(new HyperionClientOptions { Host = host }.GetValidationErrors());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" 192.168.1.20")]
    [InlineData("my hyperion")]
    public void InvalidHosts_AreRejected(string host)
    {
        Assert.Contains(new HyperionClientOptions { Host = host }.GetValidationErrors(), e => e.Contains("host", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void InvalidPorts_AreRejected(int port)
    {
        Assert.Contains(new HyperionClientOptions { Host = "h", Port = port }.GetValidationErrors(), e => e.StartsWith("Port", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(200)]
    [InlineData(-1)]
    public void PrioritiesOutsideHyperionRange_AreRejected(int priority)
    {
        Assert.Contains(new HyperionClientOptions { Host = "h", Priority = priority }.GetValidationErrors(), e => e.StartsWith("Priority", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HyperionDefaults.MinPriority)]
    [InlineData(HyperionDefaults.MaxPriority)]
    public void PriorityRangeIsInclusive(int priority)
    {
        Assert.Empty(new HyperionClientOptions { Host = "h", Priority = priority }.GetValidationErrors());
    }

    [Fact]
    public void Validate_ThrowsWithAllErrors()
    {
        var exception = Assert.Throws<ArgumentException>(() => new HyperionClientOptions { Host = string.Empty, Port = 0 }.Validate());

        Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Port", exception.Message, StringComparison.Ordinal);
    }
}
