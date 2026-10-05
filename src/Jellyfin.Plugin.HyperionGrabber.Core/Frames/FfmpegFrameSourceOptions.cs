using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// What <see cref="FfmpegFrameSource"/> decodes and how.
/// </summary>
public sealed record FfmpegFrameSourceOptions
{
    /// <summary>Default output width in pixels; plenty for any LED layout.</summary>
    public const int DefaultOutputWidth = 160;

    /// <summary>Default constant output frame rate.</summary>
    public const int DefaultFramesPerSecond = 25;

    /// <summary>Default number of frame buffers.</summary>
    public const int DefaultBufferCount = 3;

    /// <summary>Smallest allowed output width.</summary>
    public const int MinOutputWidth = 16;

    /// <summary>Largest allowed output width.</summary>
    public const int MaxOutputWidth = 640;

    /// <summary>Highest allowed output frame rate.</summary>
    public const int MaxFramesPerSecond = 60;

    /// <summary>Smallest allowed number of frame buffers (one being read, one being consumed).</summary>
    public const int MinBufferCount = 2;

    /// <summary>Largest allowed number of frame buffers.</summary>
    public const int MaxBufferCount = 16;

    /// <summary>Gets the path of the FFmpeg executable (Jellyfin's, so its hardware support is available).</summary>
    public required string FfmpegPath { get; init; }

    /// <summary>Gets the path of the media file to decode.</summary>
    public required string InputPath { get; init; }

    /// <summary>Gets the display width of the video stream, used to keep the aspect ratio.</summary>
    public required int SourceWidth { get; init; }

    /// <summary>Gets the display height of the video stream, used to keep the aspect ratio.</summary>
    public required int SourceHeight { get; init; }

    /// <summary>Gets the media position of the first frame.</summary>
    public TimeSpan StartPosition { get; init; }

    /// <summary>Gets the output width in pixels (even); the height follows from the aspect ratio.</summary>
    public int OutputWidth { get; init; } = DefaultOutputWidth;

    /// <summary>Gets the constant output frame rate: frame <c>n</c> shows <see cref="StartPosition"/> + n / fps.</summary>
    public int FramesPerSecond { get; init; } = DefaultFramesPerSecond;

    /// <summary>Gets the preferred hardware decoder; on failure before the first frame the source falls back to the CPU.</summary>
    public HardwareAcceleration HardwareAcceleration { get; init; }

    /// <summary>Gets the DRM render node for VA-API and QuickSync on Linux (for example <c>/dev/dri/renderD128</c>), if any.</summary>
    public string? HardwareDevice { get; init; }

    /// <summary>Gets the number of frame buffers; when all are in use FFmpeg is not read and blocks on the pipe.</summary>
    public int BufferCount { get; init; } = DefaultBufferCount;

    /// <summary>Gets the output height in pixels: even, at least 2, keeping the source aspect ratio.</summary>
    public int OutputHeight => SourceWidth <= 0 || SourceHeight <= 0
        ? 0
        : Math.Max(2, (int)Math.Round(OutputWidth * (double)SourceHeight / SourceWidth / 2, MidpointRounding.AwayFromZero) * 2);

    /// <summary>Gets the size in bytes of one RGB24 output frame.</summary>
    public int FrameLength => OutputWidth * OutputHeight * 3;

    /// <summary>
    /// Returns human-readable problems with these options; empty when they are valid.
    /// </summary>
    /// <returns>Validation errors.</returns>
    public IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(FfmpegPath))
        {
            errors.Add("FFmpeg path is not configured.");
        }

        if (string.IsNullOrWhiteSpace(InputPath))
        {
            errors.Add("Input path is required.");
        }
        else if (InputPath.StartsWith('-'))
        {
            // FFmpeg would parse it as an option.
            errors.Add("Input path must not start with '-'.");
        }

        if (SourceWidth <= 0 || SourceHeight <= 0)
        {
            errors.Add("Source width and height must be positive.");
        }
        else if (OutputHeight > MaxOutputWidth * 4)
        {
            errors.Add("Source aspect ratio is too extreme.");
        }

        if (StartPosition < TimeSpan.Zero)
        {
            errors.Add("Start position must not be negative.");
        }

        if (OutputWidth is < MinOutputWidth or > MaxOutputWidth || OutputWidth % 2 != 0)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"Output width must be an even number between {MinOutputWidth} and {MaxOutputWidth}."));
        }

        if (FramesPerSecond is < 1 or > MaxFramesPerSecond)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"Frame rate must be between 1 and {MaxFramesPerSecond}."));
        }

        if (!Enum.IsDefined(HardwareAcceleration))
        {
            errors.Add("Unknown hardware acceleration.");
        }

        if (BufferCount is < MinBufferCount or > MaxBufferCount)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"Buffer count must be between {MinBufferCount} and {MaxBufferCount}."));
        }

        return errors;
    }

    /// <summary>
    /// Creates options for decoding <paramref name="input"/> with the server's FFmpeg settings.
    /// </summary>
    /// <param name="settings">FFmpeg path and hardware acceleration of the server.</param>
    /// <param name="input">The video to decode.</param>
    /// <param name="startPosition">Media position of the first frame.</param>
    /// <returns>Options with defaults for size, rate and buffers.</returns>
    public static FfmpegFrameSourceOptions Create(FfmpegSettings settings, VideoInput input, TimeSpan startPosition)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(input);
        var acceleration = settings.GetAcceleration(input.Codec);
        return new FfmpegFrameSourceOptions
        {
            FfmpegPath = settings.FfmpegPath,
            InputPath = input.Path,
            SourceWidth = input.Width,
            SourceHeight = input.Height,
            StartPosition = startPosition,
            HardwareAcceleration = acceleration,
            HardwareDevice = acceleration == HardwareAcceleration.None ? null : settings.HardwareDevice,
        };
    }
}
