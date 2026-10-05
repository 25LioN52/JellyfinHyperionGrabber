using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HyperionGrabber.Playback;

/// <summary>
/// Feeds Jellyfin's playback events and the saved playback filter into a <see cref="PlaybackMonitor"/>.
/// </summary>
/// <remarks>
/// Event handlers run on Jellyfin's threads; they only translate the event and queue it, never wait.
/// </remarks>
internal sealed partial class PlaybackMonitorService : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly IPlaybackFilterSource _filterSource;
    private readonly ILogger _logger;
    private int _subscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackMonitorService"/> class.
    /// </summary>
    /// <param name="sessionManager">Source of playback events.</param>
    /// <param name="sessionFactory">Starts the sessions that drive the lights.</param>
    /// <param name="filterSource">Saved device and user filter.</param>
    /// <param name="timeProvider">Clock.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public PlaybackMonitorService(
        ISessionManager sessionManager,
        IGrabSessionFactory sessionFactory,
        IPlaybackFilterSource filterSource,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _sessionManager = sessionManager;
        _filterSource = filterSource;
        _logger = loggerFactory.CreateLogger<PlaybackMonitorService>();
        Monitor = new PlaybackMonitor(sessionFactory, timeProvider, loggerFactory.CreateLogger<PlaybackMonitor>());
    }

    /// <summary>Gets the monitor fed by this service.</summary>
    internal PlaybackMonitor Monitor { get; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Monitor.UpdateFilter(_filterSource.GetCurrent());
        Monitor.Start();
        Subscribe();
        Log.Started(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return Monitor.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Unsubscribe();
        _ = Monitor.StopAsync(CancellationToken.None);
    }

    private void Subscribe()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) != 0)
        {
            return;
        }

        _filterSource.Changed += OnFilterChanged;
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
    }

    private void Unsubscribe()
    {
        if (Interlocked.Exchange(ref _subscribed, 0) == 0)
        {
            return;
        }

        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _filterSource.Changed -= OnFilterChanged;
    }

    private void OnFilterChanged(object? sender, EventArgs e) => Monitor.UpdateFilter(_filterSource.GetCurrent());

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e) => Post(PlaybackEventKind.Started, e);

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e) => Post(PlaybackEventKind.Progress, e);

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e) => Post(PlaybackEventKind.Stopped, e);

    private void Post(PlaybackEventKind kind, PlaybackProgressEventArgs args)
    {
        if (PlaybackEventMapper.Map(kind, args) is { } playbackEvent)
        {
            Monitor.Post(playbackEvent);
        }
        else
        {
            Log.Ignored(_logger, kind);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Listening for playback events")]
        public static partial void Started(ILogger logger);

        [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Ignored a {Kind} playback event without session, device or item")]
        public static partial void Ignored(ILogger logger, PlaybackEventKind kind);
    }
}
