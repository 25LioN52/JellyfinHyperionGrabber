namespace Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

/// <summary>
/// A 24-bit color.
/// </summary>
/// <param name="R">Red component.</param>
/// <param name="G">Green component.</param>
/// <param name="B">Blue component.</param>
public readonly record struct Rgb(byte R, byte G, byte B);
