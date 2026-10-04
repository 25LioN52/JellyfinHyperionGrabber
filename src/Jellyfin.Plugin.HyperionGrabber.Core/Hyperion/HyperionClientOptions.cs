using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

/// <summary>
/// Where and how to connect to a Hyperion.ng or HyperHDR FlatBuffers server.
/// </summary>
public sealed record HyperionClientOptions
{
    /// <summary>Maximum length of a DNS host name.</summary>
    public const int MaxHostLength = 253;

    /// <summary>Maximum length of the origin string.</summary>
    public const int MaxOriginLength = 64;

    /// <summary>Gets the host name or IP address of the Hyperion server.</summary>
    public required string Host { get; init; }

    /// <summary>Gets the FlatBuffers server port.</summary>
    public int Port { get; init; } = HyperionDefaults.FlatBuffersPort;

    /// <summary>Gets the priority to register (100-199; lower wins).</summary>
    public int Priority { get; init; } = HyperionDefaults.Priority;

    /// <summary>Gets the origin shown in Hyperion's priority list.</summary>
    public string Origin { get; init; } = HyperionDefaults.Origin;

    /// <summary>Gets the time allowed for the TCP connection to be established.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets the time allowed for Hyperion to confirm the registration.</summary>
    public TimeSpan ReplyTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets the time allowed for one message to be written before the connection is considered dead.</summary>
    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Returns human-readable problems with these options; empty when they are valid.
    /// </summary>
    /// <returns>Validation errors suitable for showing to an administrator.</returns>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add("Hyperion host is required.");
        }
        else if (Host.Length > MaxHostLength || Host.Trim() != Host || Host.Contains(' ', StringComparison.Ordinal))
        {
            errors.Add("Hyperion host must be a host name or IP address without spaces.");
        }

        if (Port is < 1 or > 65535)
        {
            errors.Add("Port must be between 1 and 65535.");
        }

        if (Priority is < HyperionDefaults.MinPriority or > HyperionDefaults.MaxPriority)
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Priority must be between {HyperionDefaults.MinPriority} and {HyperionDefaults.MaxPriority} (Hyperion.ng rejects other values for FlatBuffers clients)."));
        }

        if (string.IsNullOrWhiteSpace(Origin) || Origin.Length > MaxOriginLength)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"Origin must be 1-{MaxOriginLength} characters."));
        }

        if (ConnectTimeout <= TimeSpan.Zero || ReplyTimeout <= TimeSpan.Zero || WriteTimeout <= TimeSpan.Zero)
        {
            errors.Add("Timeouts must be positive.");
        }

        return errors;
    }

    /// <summary>
    /// Throws when the options are invalid.
    /// </summary>
    /// <exception cref="ArgumentException">One or more options are invalid.</exception>
    public void Validate()
    {
        var errors = GetValidationErrors();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }
    }
}
