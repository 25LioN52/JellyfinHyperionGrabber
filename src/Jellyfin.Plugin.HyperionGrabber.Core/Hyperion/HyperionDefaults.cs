namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

/// <summary>
/// Protocol constants shared by Hyperion.ng and HyperHDR FlatBuffers servers.
/// </summary>
public static class HyperionDefaults
{
    /// <summary>
    /// Default TCP port of the FlatBuffers server in Hyperion.ng and HyperHDR.
    /// </summary>
    public const int FlatBuffersPort = 19400;

    /// <summary>
    /// Lowest priority Hyperion.ng accepts from a FlatBuffers client (lower number means higher priority).
    /// </summary>
    public const int MinPriority = 100;

    /// <summary>
    /// Highest priority Hyperion.ng accepts from a FlatBuffers client.
    /// </summary>
    public const int MaxPriority = 199;

    /// <summary>
    /// Default priority: above Hyperion's own grabbers (240-250) and background effects, below manual colors.
    /// </summary>
    public const int Priority = 150;

    /// <summary>
    /// Origin name shown in the Hyperion/HyperHDR priority list.
    /// </summary>
    public const string Origin = "Jellyfin";

    /// <summary>
    /// Image duration meaning "until replaced or cleared".
    /// </summary>
    public const int InfiniteDuration = -1;

    /// <summary>
    /// Largest image edge the client sends. Hyperion only needs a few dozen pixels per edge.
    /// </summary>
    public const int MaxImageDimension = 1920;
}
