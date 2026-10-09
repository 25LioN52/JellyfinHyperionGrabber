using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Streaming;

/// <summary>
/// Starts a <see cref="StreamingGrabSession"/> per playback: decodes the playing video with FFmpeg and streams it to
/// Hyperion in step with the reported playback position.
/// </summary>
public sealed class StreamingGrabSessionFactory : IGrabSessionFactory
{
    private readonly Lock _gate = new();
    private Task _previousHyperionReleased = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingGrabSessionFactory"/> class.
    /// </summary>
    /// <param name="resolver">Finds the video file of the playing item.</param>
    /// <param name="ffmpegSettings">The server's FFmpeg path and hardware acceleration.</param>
    /// <param name="streamingSettings">Hyperion server and frame rate.</param>
    /// <param name="timeProvider">Clock for pacing and timeouts.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public StreamingGrabSessionFactory(
        IVideoInputResolver resolver,
        IFfmpegSettingsProvider ffmpegSettings,
        IStreamingSettingsProvider streamingSettings,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
        : this(
            resolver,
            ffmpegSettings,
            streamingSettings,
            timeProvider,
            loggerFactory,
            options => FfmpegFrameSource.Start(options, timeProvider, loggerFactory.CreateLogger<FfmpegFrameSource>()),
            async (options, cancellationToken) => await HyperionClient.ConnectAsync(options, loggerFactory.CreateLogger<HyperionClient>(), cancellationToken).ConfigureAwait(false))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingGrabSessionFactory"/> class with replaceable frame
    /// source and Hyperion connection (for tests).
    /// </summary>
    /// <param name="resolver">Finds the video file of the playing item.</param>
    /// <param name="ffmpegSettings">The server's FFmpeg path and hardware acceleration.</param>
    /// <param name="streamingSettings">Hyperion server and frame rate.</param>
    /// <param name="timeProvider">Clock for pacing and timeouts.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="startFrameSource">Starts decoding with validated options.</param>
    /// <param name="connect">Connects and registers with Hyperion.</param>
    internal StreamingGrabSessionFactory(
        IVideoInputResolver resolver,
        IFfmpegSettingsProvider ffmpegSettings,
        IStreamingSettingsProvider streamingSettings,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        Func<FfmpegFrameSourceOptions, IFrameSource> startFrameSource,
        Func<HyperionClientOptions, CancellationToken, Task<IHyperionConnection>> connect)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(ffmpegSettings);
        ArgumentNullException.ThrowIfNull(streamingSettings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(startFrameSource);
        ArgumentNullException.ThrowIfNull(connect);
        Resolver = resolver;
        FfmpegSettings = ffmpegSettings;
        StreamingSettings = streamingSettings;
        TimeProvider = timeProvider;
        StartFrameSource = startFrameSource;
        Connect = connect;
        SessionLogger = loggerFactory.CreateLogger<StreamingGrabSession>();
    }

    internal IVideoInputResolver Resolver { get; }

    internal IFfmpegSettingsProvider FfmpegSettings { get; }

    internal IStreamingSettingsProvider StreamingSettings { get; }

    internal TimeProvider TimeProvider { get; }

    internal Func<FfmpegFrameSourceOptions, IFrameSource> StartFrameSource { get; }

    internal Func<HyperionClientOptions, CancellationToken, Task<IHyperionConnection>> Connect { get; }

    internal ILogger SessionLogger { get; }

    /// <inheritdoc />
    /// <remarks>A session connects to Hyperion only after the previous one released it (see
    /// <see cref="StreamingGrabSession.HyperionReleased"/>), so they never share the priority.</remarks>
    public IGrabSession Start(PlaybackState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            var session = StreamingGrabSession.Start(this, state, _previousHyperionReleased);
            _previousHyperionReleased = session.HyperionReleased;
            return session;
        }
    }
}
