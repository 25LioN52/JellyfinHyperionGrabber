using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;

namespace Jellyfin.Plugin.HyperionGrabber.TestSupport;

/// <summary>
/// <see cref="IGrabSessionFactory"/> that records the sessions it starts and what they receive.
/// </summary>
public sealed class RecordingGrabSessionFactory : IGrabSessionFactory
{
    private readonly ConcurrentQueue<RecordingGrabSession> _sessions = new();

    /// <summary>Gets the started sessions, oldest first.</summary>
    public IReadOnlyList<RecordingGrabSession> Sessions => _sessions.ToArray();

    /// <summary>Gets the sessions that are not disposed.</summary>
    public IReadOnlyList<RecordingGrabSession> Running => _sessions.Where(s => !s.IsDisposed).ToArray();

    /// <summary>Gets or sets a value indicating whether <see cref="Start"/> throws.</summary>
    public bool FailStart { get; set; }

    /// <inheritdoc />
    public IGrabSession Start(PlaybackState state)
    {
        if (FailStart)
        {
            throw new InvalidOperationException("Simulated session failure.");
        }

        var session = new RecordingGrabSession(state);
        _sessions.Enqueue(session);
        return session;
    }
}

/// <summary>
/// A session started by <see cref="RecordingGrabSessionFactory"/>.
/// </summary>
public sealed class RecordingGrabSession : IGrabSession
{
    private readonly ConcurrentQueue<PlaybackState> _updates = new();
    private int _disposed;

    internal RecordingGrabSession(PlaybackState initial) => Initial = initial;

    /// <summary>Gets the state the session was started with.</summary>
    public PlaybackState Initial { get; }

    /// <summary>Gets the updates received, oldest first.</summary>
    public IReadOnlyList<PlaybackState> Updates => _updates.ToArray();

    /// <summary>Gets a value indicating whether the session was disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <inheritdoc />
    public void Update(PlaybackState state) => _updates.Enqueue(state);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}
