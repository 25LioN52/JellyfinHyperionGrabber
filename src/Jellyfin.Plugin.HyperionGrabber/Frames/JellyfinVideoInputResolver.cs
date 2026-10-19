using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
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
        var (width, height) = GetDisplaySize(video, video.Width.Value, video.Height.Value);
        return VideoInputResult.Supported(new VideoInput("file:" + source.Path, width, height, video.Codec)
        {
            DynamicRange = GetDynamicRange(video),
        });
    }

    /// <summary>
    /// Returns the size the video is shown at. Anamorphic video has non-square pixels (a 16:9 DVD is stored at 720×576),
    /// so it is widened to its display aspect ratio, which Jellyfin keeps as text such as <c>16:9</c> or <c>2.40:1</c>.
    /// </summary>
    /// <param name="video">The video stream.</param>
    /// <param name="width">The stored width.</param>
    /// <param name="height">The stored height.</param>
    /// <returns>The display size; the stored size when the aspect ratio is unknown or the pixels are square.</returns>
    internal static (int Width, int Height) GetDisplaySize(MediaStream video, int width, int height)
    {
        if (video.IsAnamorphic == false || !TryParseAspectRatio(video.AspectRatio, out var ratio))
        {
            return (width, height);
        }

        // Jellyfin rounds the ratio to a common name (up to 3.75 % off for 4:3), so without its anamorphic flag
        // (libraries scanned before it existed) only a clearly different shape counts.
        var tolerance = video.IsAnamorphic == true ? 0 : 0.05;
        var displayWidth = (int)Math.Round(height * ratio, MidpointRounding.AwayFromZero);
        return Math.Abs(displayWidth - width) > width * tolerance ? (displayWidth, height) : (width, height);
    }

    /// <summary>Maps Jellyfin's video range to what tone mapping needs to know.</summary>
    /// <param name="video">The video stream.</param>
    /// <returns>The dynamic range; SDR when Jellyfin does not know better.</returns>
    internal static VideoDynamicRange GetDynamicRange(MediaStream video)
    {
        if (video.BitDepth is < 10)
        {
            // Like Jellyfin, which only tone maps video with 10 bits or more: 8-bit "HDR" is mislabelled SDR.
            return VideoDynamicRange.Sdr;
        }

        return video.VideoRangeType switch
        {
            VideoRangeType.HDR10 or VideoRangeType.HDR10Plus
                or VideoRangeType.DOVIWithHDR10 or VideoRangeType.DOVIWithHDR10Plus
                or VideoRangeType.DOVIWithEL or VideoRangeType.DOVIWithELHDR10Plus => VideoDynamicRange.Hdr10,
            VideoRangeType.HLG or VideoRangeType.DOVIWithHLG => VideoDynamicRange.Hlg,
            VideoRangeType.DOVI => VideoDynamicRange.DolbyVision,

            // Dolby Vision outside the specification: Jellyfin 10.11 treats it as HDR10, Jellyfin 12 as whatever its base
            // layer is tagged.
            VideoRangeType.DOVIInvalid when video.VideoRange == VideoRange.HDR
                => string.Equals(video.ColorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase) ? VideoDynamicRange.Hlg : VideoDynamicRange.Hdr10,
            _ => VideoDynamicRange.Sdr,
        };
    }

    /// <summary>Parses <c>width:height</c> (decimals allowed) into a plausible display shape, between 1:5 and 5:1.</summary>
    private static bool TryParseAspectRatio(string? text, out double ratio)
    {
        ratio = 0;
        var separator = text?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (separator <= 0
            || !double.TryParse(text.AsSpan(0, separator), NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            || !double.TryParse(text.AsSpan(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        ratio = width / height;
        return ratio is >= 0.2 and <= 5;
    }

    // Jellyfin formats media source ids as GUIDs without dashes; clients may send either format.
    private static bool IsSameId(string? sourceId, string? reportedId)
        => !string.IsNullOrEmpty(reportedId)
            && (string.Equals(sourceId, reportedId, StringComparison.OrdinalIgnoreCase)
                || (Guid.TryParse(sourceId, out var source) && Guid.TryParse(reportedId, out var reported) && source == reported));
}
