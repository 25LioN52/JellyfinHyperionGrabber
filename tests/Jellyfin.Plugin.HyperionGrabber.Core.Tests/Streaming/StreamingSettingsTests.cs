using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Streaming;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Streaming;

public class StreamingSettingsTests
{
    [Fact]
    public void Defaults_AreValid_AndReleaseAfter15Seconds()
    {
        var settings = new StreamingSettings();

        Assert.Empty(settings.GetValidationErrors());
        Assert.Equal(TimeSpan.Zero, settings.LatencyOffset);
        Assert.Equal(TimeSpan.FromSeconds(15), settings.PauseRelease);
    }

    [Theory]
    [InlineData(-2000)]
    [InlineData(0)]
    [InlineData(2000)]
    public void LatencyOffset_WithinTwoSeconds_IsValid(int milliseconds)
    {
        Assert.Empty(new StreamingSettings { LatencyOffset = TimeSpan.FromMilliseconds(milliseconds) }.GetValidationErrors());
    }

    [Theory]
    [InlineData(-2001)]
    [InlineData(2001)]
    public void LatencyOffset_BeyondTwoSeconds_IsRejected(int milliseconds)
    {
        var errors = new StreamingSettings { LatencyOffset = TimeSpan.FromMilliseconds(milliseconds) }.GetValidationErrors();

        Assert.Contains(errors, e => e.StartsWith("Light timing offset", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    public void PauseRelease_Range(int seconds, bool valid)
    {
        var errors = new StreamingSettings { PauseRelease = TimeSpan.FromSeconds(seconds) }.GetValidationErrors();

        Assert.Equal(valid, errors.Count == 0);
    }
}
