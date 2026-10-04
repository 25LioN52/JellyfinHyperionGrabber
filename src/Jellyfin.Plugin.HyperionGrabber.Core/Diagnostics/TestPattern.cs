using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

/// <summary>
/// Renders the LED layout test pattern.
/// </summary>
/// <remarks>
/// <para>Each screen edge has its own color (top red, right green, bottom blue, left yellow; corners are split along the
/// diagonals) and a white block travels clockwise around the border, starting at the top-left corner.</para>
/// <para>On a correctly configured Hyperion LED layout you see the four colors on the matching sides of the TV and a white
/// light chasing clockwise. Wrong colors mean the layout is mirrored or rotated; a counter-clockwise chase means the LED
/// direction is reversed. This contract is documented for users in docs/user-guide/hyperion-setup.md.</para>
/// </remarks>
public static class TestPattern
{
    /// <summary>Color of the top edge.</summary>
    public static readonly Rgb TopColor = new(255, 0, 0);

    /// <summary>Color of the right edge.</summary>
    public static readonly Rgb RightColor = new(0, 255, 0);

    /// <summary>Color of the bottom edge.</summary>
    public static readonly Rgb BottomColor = new(0, 0, 255);

    /// <summary>Color of the left edge.</summary>
    public static readonly Rgb LeftColor = new(255, 255, 0);

    /// <summary>Color of the moving marker.</summary>
    public static readonly Rgb MarkerColor = new(255, 255, 255);

    /// <summary>
    /// Renders one frame.
    /// </summary>
    /// <param name="rgb24">Destination of exactly <paramref name="width"/> * <paramref name="height"/> * 3 bytes.</param>
    /// <param name="width">Frame width, at least 2 pixels.</param>
    /// <param name="height">Frame height, at least 2 pixels.</param>
    /// <param name="phase">Marker position along the border: 0 is the top-left corner, 0.5 the bottom-right corner;
    /// only the fractional part is used, so a growing value loops.</param>
    public static void Render(Span<byte> rgb24, int width, int height, double phase)
    {
        if (width < 2 || height < 2 || (long)width * height * 3 != rgb24.Length)
        {
            throw new ArgumentException($"Expected a buffer for a {width}x{height} RGB24 frame of at least 2x2 pixels.", nameof(rgb24));
        }

        if (!double.IsFinite(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Phase must be a finite number.");
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Write(rgb24, width, x, y, EdgeColor(x, y, width, height));
            }
        }

        var (markerX, markerY, markerSize) = GetMarker(width, height, phase - Math.Floor(phase));
        for (var y = markerY; y < markerY + markerSize; y++)
        {
            for (var x = markerX; x < markerX + markerSize; x++)
            {
                Write(rgb24, width, x, y, MarkerColor);
            }
        }
    }

    /// <summary>
    /// Returns the marker square for a phase in [0, 1).
    /// </summary>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="phase">Normalized position along the border, clockwise from the top-left corner.</param>
    /// <returns>Top-left corner and edge length of the marker, clamped inside the frame.</returns>
    internal static (int X, int Y, int Size) GetMarker(int width, int height, double phase)
    {
        var size = Math.Max(1, Math.Min(width, height) / 4);
        var perimeter = 2.0 * (width + height);
        var distance = phase * perimeter;

        double pointX, pointY;
        if (distance < width)
        {
            (pointX, pointY) = (distance, 0); // top edge, left to right
        }
        else if (distance < width + height)
        {
            (pointX, pointY) = (width, distance - width); // right edge, top to bottom
        }
        else if (distance < (2.0 * width) + height)
        {
            (pointX, pointY) = (width - (distance - width - height), height); // bottom edge, right to left
        }
        else
        {
            (pointX, pointY) = (0, height - (distance - (2.0 * width) - height)); // left edge, bottom to top
        }

        var x = Math.Clamp((int)Math.Round(pointX - (size / 2.0)), 0, width - size);
        var y = Math.Clamp((int)Math.Round(pointY - (size / 2.0)), 0, height - size);
        return (x, y, size);
    }

    private static Rgb EdgeColor(int x, int y, int width, int height)
    {
        // Normalized distances to each edge; the nearest edge wins, which splits the frame along its diagonals.
        var u = (x + 0.5) / width;
        var v = (y + 0.5) / height;
        var top = v;
        var right = 1 - u;
        var bottom = 1 - v;
        var left = u;

        var nearest = Math.Min(Math.Min(top, right), Math.Min(bottom, left));
        if (nearest == top)
        {
            return TopColor;
        }

        if (nearest == right)
        {
            return RightColor;
        }

        return nearest == bottom ? BottomColor : LeftColor;
    }

    private static void Write(Span<byte> rgb24, int width, int x, int y, Rgb color)
    {
        var offset = ((y * width) + x) * 3;
        rgb24[offset] = color.R;
        rgb24[offset + 1] = color.G;
        rgb24[offset + 2] = color.B;
    }
}
