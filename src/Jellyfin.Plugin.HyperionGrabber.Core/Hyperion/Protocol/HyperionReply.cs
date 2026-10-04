namespace Jellyfin.Plugin.HyperionGrabber.Core.Hyperion.Protocol;

/// <summary>
/// A decoded <c>Reply</c> table sent by Hyperion.ng or HyperHDR.
/// </summary>
/// <param name="Error">Error text, or <see langword="null"/> on success.</param>
/// <param name="Video">Video mode notification (-1 when absent). Not used by this plugin.</param>
/// <param name="Registered">Priority confirmed by a successful Register (-1 when absent).</param>
internal readonly record struct HyperionReply(string? Error, int Video, int Registered)
{
    /// <summary>Gets a value indicating whether the server reported an error.</summary>
    public bool IsError => !string.IsNullOrEmpty(Error);

    /// <summary>Gets a value indicating whether this reply confirms a registration.</summary>
    public bool IsRegistration => Registered != -1;
}
