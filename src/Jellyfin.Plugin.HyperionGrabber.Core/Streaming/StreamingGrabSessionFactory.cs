using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Jellyfin.Plugin.HyperionGrabber.Core.Kodi;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Streaming;

/// <summary>
/// Starts a <see cref="StreamingGrabSession"/> per playback and target: decodes the playing video with FFmpeg and
/// streams it to the target's Hyperion in step with the reported playback position.
/// </summary>
public sealed class StreamingGrabSessionFactory : IGrabSessionFactory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<PlaybackTargetKey, Task> _hyperionReleased = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingGrabSessionFactory"/> class.
    /// </summary>
    /// <param name="resolver">Finds the video file of the playing item.</param>
    /// <param name="ffmpegSettings">The server's FFmpeg path and hardware acceleration.</param>
    /// <param name="streamingSettings">Frame rate, timing and Kodi settings.</param>
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
            async (options, cancellationToken) => await HyperionClient.ConnectAsync(options, loggerFactory.CreateLogger<HyperionClient>(), timeProvider, cancellationToken).ConfigureAwait(false))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingGrabSessionFactory"/> class with replaceable frame
    /// source and Hyperion connection (for tests).
    /// </summary>
    /// <param name="resolver">Finds the video file of the playing item.</param>
    /// <param name="ffmpegSettings">The server's FFmpeg path and hardware acceleration.</param>
    /// <param name="streamingSettings">Frame rate, timing and Kodi settings.</param>
    /// <param name="timeProvider">Clock for pacing and timeouts.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="startFrameSource">Starts decoding with validated options.</param>
    /// <param name="connect">Connects and registers with Hyperion.</param>
    /// <param name="startClock">Starts reading the position from a Kodi client; <see langword="null"/> for <see cref="KodiClock"/>.</param>
    internal StreamingGrabSessionFactory(
        IVideoInputResolver resolver,
        IFfmpegSettingsProvider ffmpegSettings,
        IStreamingSettingsProvider streamingSettings,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        Func<FfmpegFrameSourceOptions, IFrameSource> startFrameSource,
        Func<HyperionClientOptions, CancellationToken, Task<IHyperionConnection>> connect,
        Func<KodiClockOptions, IPlaybackClock>? startClock = null)
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
        var clockLogger = loggerFactory.CreateLogger<KodiClock>();
        StartClock = startClock ?? (options => KodiClock.Start(options, timeProvider, clockLogger));
        SessionLogger = loggerFactory.CreateLogger<StreamingGrabSession>();
    }

    internal IVideoInputResolver Resolver { get; }

    internal IFfmpegSettingsProvider FfmpegSettings { get; }

    internal IStreamingSettingsProvider StreamingSettings { get; }

    internal TimeProvider TimeProvider { get; }

    internal Func<FfmpegFrameSourceOptions, IFrameSource> StartFrameSource { get; }

    internal Func<HyperionClientOptions, CancellationToken, Task<IHyperionConnection>> Connect { get; }

    /// <summary>Gets what starts reading the position from a Kodi client (<see cref="KodiClock"/> unless a test replaces it).</summary>
    internal Func<KodiClockOptions, IPlaybackClock> StartClock { get; }

    internal ILogger SessionLogger { get; }

    /// <inheritdoc />
    /// <remarks>A session connects to Hyperion only after the previous session of the same server and priority
    /// released it (see <see cref="StreamingGrabSession.HyperionReleased"/>), so they never share the priority.
    /// Sessions of other targets do not wait for each other.</remarks>
    public IGrabSession Start(PlaybackState state, PlaybackTarget target)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            // Released servers need no waiting; forgetting them keeps edited targets from piling up.
            foreach (var released in _hyperionReleased.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToList())
            {
                _hyperionReleased.Remove(released);
            }

            var previous = _hyperionReleased.GetValueOrDefault(target.Key, Task.CompletedTask);
            var session = StreamingGrabSession.Start(this, state, target, previous);
            _hyperionReleased[target.Key] = session.HyperionReleased;
            return session;
        }
    }
}
