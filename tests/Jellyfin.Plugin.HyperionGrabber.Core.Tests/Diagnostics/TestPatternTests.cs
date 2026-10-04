using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Diagnostics;

/// <summary>
/// The pattern is a user-facing contract (documented in docs/user-guide/hyperion-setup.md).
/// </summary>
public class TestPatternTests
{
    private const int Width = 64;
    private const int Height = 36;

    [Fact]
    public void Render_PaintsEachEdgeInItsColor()
    {
        var frame = Render(phase: 0.0); // marker sits in the top-left corner

        Assert.Equal(TestPattern.TopColor, PixelAt(frame, Width / 2, 1));
        Assert.Equal(TestPattern.RightColor, PixelAt(frame, Width - 2, Height / 2));
        Assert.Equal(TestPattern.BottomColor, PixelAt(frame, Width / 2, Height - 2));
        Assert.Equal(TestPattern.LeftColor, PixelAt(frame, 1, Height / 2));
    }

    [Theory]
    [InlineData(0.0, 0, 0)] // top-left corner
    [InlineData(0.5, Width - 1, Height - 1)] // bottom-right corner
    [InlineData(1.0, 0, 0)] // a full lap is back at the start
    public void Render_PlacesMarkerOnTheBorder(double phase, int x, int y)
    {
        Assert.Equal(TestPattern.MarkerColor, PixelAt(Render(phase), x, y));
    }

    [Fact]
    public void Marker_TravelsClockwise()
    {
        var size = TestPattern.GetMarker(Width, Height, 0).Size;

        Assert.Equal(0, TestPattern.GetMarker(Width, Height, 0.10).Y); // along the top
        Assert.Equal(Width - size, TestPattern.GetMarker(Width, Height, 0.35).X); // down the right side
        Assert.Equal(Height - size, TestPattern.GetMarker(Width, Height, 0.60).Y); // along the bottom, leftwards
        Assert.Equal(0, TestPattern.GetMarker(Width, Height, 0.90).X); // up the left side
        Assert.True(TestPattern.GetMarker(Width, Height, 0.60).X > TestPattern.GetMarker(Width, Height, 0.70).X);
        Assert.True(TestPattern.GetMarker(Width, Height, 0.85).Y > TestPattern.GetMarker(Width, Height, 0.95).Y);
    }

    [Fact]
    public void Render_OnlyUsesFractionalPhase()
    {
        Assert.Equal(Render(0.25), Render(3.25));
    }

    [Fact]
    public void Render_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => TestPattern.Render(new byte[10], Width, Height, 0));
        Assert.Throws<ArgumentException>(() => TestPattern.Render(new byte[3], 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TestPattern.Render(new byte[Width * Height * 3], Width, Height, double.NaN));
    }

    private static byte[] Render(double phase)
    {
        var frame = new byte[Width * Height * 3];
        TestPattern.Render(frame, Width, Height, phase);
        return frame;
    }

    private static Rgb PixelAt(byte[] frame, int x, int y)
    {
        var offset = ((y * Width) + x) * 3;
        return new Rgb(frame[offset], frame[offset + 1], frame[offset + 2]);
    }
}
