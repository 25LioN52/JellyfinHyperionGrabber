namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// The video stream of the media source that is playing.
/// </summary>
/// <param name="Path">What FFmpeg opens: a local file with the <c>file:</c> protocol prefix (as Jellyfin's transcoder
/// passes it), so a path is never mistaken for another protocol.</param>
/// <param name="Width">Display width of the video stream.</param>
/// <param name="Height">Display height of the video stream.</param>
/// <param name="Codec">Video codec (for example <c>hevc</c>), or <see langword="null"/> when unknown.</param>
public sealed record VideoInput(string Path, int Width, int Height, string? Codec);
