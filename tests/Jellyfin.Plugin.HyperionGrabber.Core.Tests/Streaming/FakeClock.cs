using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Streaming;

/// <summary>
/// <see cref="IPlaybackClock"/> whose readings the test sets.
/// </summary>
internal sealed class FakeClock : IPlaybackClock
{
    private ClockReading? _latest;
    private string _description = "Kodi's clock at 192.168.1.50:9090.";
    private int _disposed;

    public ClockReading? Latest
    {
        get => Volatile.Read(ref _latest);
        set => Volatile.Write(ref _latest, value);
    }

    public string Description
    {
        get => Volatile.Read(ref _description);
        set => Volatile.Write(ref _description, value);
    }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}
