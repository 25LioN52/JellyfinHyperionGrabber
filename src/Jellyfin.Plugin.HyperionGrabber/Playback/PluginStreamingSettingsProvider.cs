using Jellyfin.Plugin.HyperionGrabber.Core.Streaming;

namespace Jellyfin.Plugin.HyperionGrabber.Playback;

/// <summary>
/// <see cref="IStreamingSettingsProvider"/> backed by the plugin's saved configuration.
/// </summary>
internal sealed class PluginStreamingSettingsProvider : IStreamingSettingsProvider
{
    /// <inheritdoc />
    public StreamingSettings GetSettings() => Plugin.Instance?.Configuration?.ToStreamingSettings() ?? new StreamingSettings();
}
