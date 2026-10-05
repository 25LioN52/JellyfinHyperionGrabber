namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// Supplies the host's current FFmpeg settings (implemented by the Jellyfin adapter).
/// </summary>
public interface IFfmpegSettingsProvider
{
    /// <summary>
    /// Reads the current settings; they can change while the server runs, so call this per playback.
    /// </summary>
    /// <returns>The FFmpeg path and hardware acceleration settings.</returns>
    FfmpegSettings GetSettings();
}
