using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Drives the lights for one playback on one target, from start until it is disposed.
/// </summary>
/// <remarks>
/// <para>Created by <see cref="IGrabSessionFactory.Start"/> and owned by <see cref="PlaybackMonitor"/>, which calls
/// members from one task at a time.</para>
/// <para><see cref="Update"/> must return quickly and must not throw: do the work on the session's own task.
/// <see cref="IAsyncDisposable.DisposeAsync"/> stops that work and releases the target (for example Hyperion's
/// priority).</para>
/// </remarks>
public interface IGrabSession : IAsyncDisposable
{
    /// <summary>Reports a newer state of the same playback (position, pause, resume, seek).</summary>
    /// <param name="state">The new state; the item is the one the session was started for.</param>
    void Update(PlaybackState state);
}
