using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// Finds the video file of a playing item (implemented by the Jellyfin adapter).
/// </summary>
public interface IVideoInputResolver
{
    /// <summary>
    /// Resolves the playing media source to a video FFmpeg can decode directly. Called on the streaming session's own
    /// task, once per playback start.
    /// </summary>
    /// <param name="itemId">The playing item.</param>
    /// <param name="mediaSourceId">The playing media source (version), if the client reported it.</param>
    /// <returns>The video, or why this item cannot drive the lights.</returns>
    VideoInputResult Resolve(Guid itemId, string? mediaSourceId);
}
