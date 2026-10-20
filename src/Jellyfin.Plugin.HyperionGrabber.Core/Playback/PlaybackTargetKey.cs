using System;
using System.Globalization;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Identifies a <see cref="PlaybackTarget"/>: the Hyperion server and the priority it registers with. Two targets with
/// the same key would fight over one priority; a target whose key changes is a different target.
/// </summary>
/// <param name="Host">The server's host as configured (compared ignoring case); empty when no server is configured.</param>
/// <param name="Port">The FlatBuffers port; 0 when no server is configured.</param>
/// <param name="Priority">The priority; 0 when no server is configured.</param>
public readonly record struct PlaybackTargetKey(string Host, int Port, int Priority)
{
    /// <summary>Gets the key of a target without a configured server.</summary>
    public static PlaybackTargetKey None { get; } = new(string.Empty, 0, 0);

    /// <summary>Returns the key of a server and priority.</summary>
    /// <param name="hyperion">The server, or <see langword="null"/>.</param>
    /// <returns>The key; <see cref="None"/> without a server.</returns>
    public static PlaybackTargetKey Of(HyperionClientOptions? hyperion)
        => hyperion is null ? None : new(hyperion.Host.Trim(), hyperion.Port, hyperion.Priority);

    /// <inheritdoc />
    public bool Equals(PlaybackTargetKey other)
        => string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase) && Port == other.Port && Priority == other.Priority;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Host ?? string.Empty), Port, Priority);

    /// <inheritdoc />
    public override string ToString()
        => this == None ? "no Hyperion server" : string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port} (priority {Priority})");
}
