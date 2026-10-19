using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.EndToEnd;

/// <summary>
/// Polls a real server until a condition holds. Hyperion processes FlatBuffers and JSON-RPC connections
/// independently, so its state is only eventually consistent with what a test just sent.
/// </summary>
internal static class Eventually
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>Reads until <paramref name="done"/> holds and returns that value.</summary>
    /// <exception cref="TimeoutException">Not reached in time; the message describes the last value read.</exception>
    public static async Task<T> ReachAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan timeout, Func<T, string> describe)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var value = await read();
            if (done(value))
            {
                return value;
            }

            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException($"Not reached within {timeout.TotalSeconds:0} s; last seen: {describe(value)}");
            }

            await Task.Delay(PollInterval);
        }
    }
}
