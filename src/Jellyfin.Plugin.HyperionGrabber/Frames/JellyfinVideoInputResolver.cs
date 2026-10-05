using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.HyperionGrabber.Frames;

/// <summary>
/// Resolves the playing media source of a library video to its file, the way Jellyfin's transcoder opens it.
/// </summary>
/// <remarks>
/// Only local video files are decoded. Live TV, remote and <c>.strm</c> sources, DVD/Blu-ray folders and disc images
/// are not: decoding them a second time would open a second tuner or network stream, or needs Jellyfin's special input
/// handling.
/// </remarks>
public class JellyfinVideoInputResolver : IVideoInputResolver
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinVideoInputResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">Finds the playing item.</param>
    /// <param name="mediaSourceManager">Lists the item's media sources (versions) with their streams.</param>
    public JellyfinVideoInputResolver(ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
    }

    /// <inheritdoc />
    public VideoInputResult Resolve(Guid itemId, string? mediaSourceId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return VideoInputResult.Unsupported("it is not in the library.");
        }

        if (item is not Video)
        {
            return VideoInputResult.Unsupported("it is not a video from the library (Live TV channels and music are not supported).");
        }

        var sources = _mediaSourceManager.GetStaticMediaSources(item, false);
        return Map(Select(sources, mediaSourceId));
    }

    /// <summary>Picks the media source the client reported, or the item's default (first) one.</summary>
    /// <param name="sources">The item's media sources.</param>
    /// <param name="mediaSourceId">The reported media source id, if any.</param>
    /// <returns>The source, or <see langword="null"/> when the item has none.</returns>
    internal static MediaSourceInfo? Select(IReadOnlyList<MediaSourceInfo>? sources, string? mediaSourceId)
    {
        if (sources is null || sources.Count == 0)
        {
            return null;
        }

        return sources.FirstOrDefault(s => IsSameId(s.Id, mediaSourceId)) ?? sources[0];
    }

    /// <summary>Maps a media source to the video FFmpeg should decode.</summary>
    /// <param name="source">The playing media source.</param>
    /// <returns>The video, or why it cannot be decoded directly.</returns>
    internal static VideoInputResult Map(MediaSourceInfo? source)
    {
        if (source is null)
        {
            return VideoInputResult.Unsupported("it has no media source.");
        }

        if (source.Protocol != MediaProtocol.File || source.IsRemote)
        {
            return VideoInputResult.Unsupported($"its media is not a local file but a {source.Protocol} stream (for example a .strm file or Live TV).");
        }

        if (source.IsInfiniteStream)
        {
            return VideoInputResult.Unsupported("it is a live stream.");
        }

        if (source.VideoType is { } videoType && videoType != VideoType.VideoFile)
        {
            return VideoInputResult.Unsupported($"{videoType} folders and disc images are not supported yet.");
        }

        if (string.IsNullOrEmpty(source.Path))
        {
            return VideoInputResult.Unsupported("its file path is unknown.");
        }

        var video = source.MediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        if (video is not { Width: > 0, Height: > 0 })
        {
            return VideoInputResult.Unsupported("it has no video stream with a known size (scan the library to refresh its media info).");
        }

        // Like Jellyfin's transcoder: the file protocol prefix keeps FFmpeg from reading a path as another protocol.
        return VideoInputResult.Supported(new VideoInput("file:" + source.Path, video.Width.Value, video.Height.Value, video.Codec));
    }

    // Jellyfin formats media source ids as GUIDs without dashes; clients may send either format.
    private static bool IsSameId(string? sourceId, string? reportedId)
        => !string.IsNullOrEmpty(reportedId)
            && (string.Equals(sourceId, reportedId, StringComparison.OrdinalIgnoreCase)
                || (Guid.TryParse(sourceId, out var source) && Guid.TryParse(reportedId, out var reported) && source == reported));
}
