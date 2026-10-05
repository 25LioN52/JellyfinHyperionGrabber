namespace Jellyfin.Plugin.HyperionGrabber.Core.Streaming;

/// <summary>
/// Supplies the current <see cref="StreamingSettings"/> (implemented by the Jellyfin adapter).
/// </summary>
public interface IStreamingSettingsProvider
{
    /// <summary>Reads the current settings; called once per playback start, so changes apply to the next playback.</summary>
    /// <returns>The settings.</returns>
    StreamingSettings GetSettings();
}
