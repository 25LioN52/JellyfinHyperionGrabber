using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

/// <summary>
/// Streams the animated <see cref="TestPattern"/> to a sink and clears it afterwards.
/// </summary>
public static class TestPatternPlayer
{
    private static readonly TimeSpan ClearTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Plays the pattern for <see cref="TestPatternOptions.Duration"/>, then clears the sink's priority, also when
    /// cancelled or failed.
    /// </summary>
    /// <param name="sink">Destination, usually a connected <see cref="HyperionClient"/>.</param>
    /// <param name="options">Pattern size, rate and duration.</param>
    /// <param name="timeProvider">Clock used for frame pacing.</param>
    /// <param name="cancellationToken">Stops the pattern early.</param>
    /// <returns>The number of frames sent.</returns>
    public static async Task<int> PlayAsync(IHyperionSink sink, TestPatternOptions options, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var errors = options.GetValidationErrors();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
        }

        var frameCount = Math.Max(1, (int)Math.Ceiling(options.Duration.TotalSeconds * options.FramesPerSecond));
        var framesPerLap = Math.Max(1, options.LapDuration.TotalSeconds * options.FramesPerSecond);
        var frame = new byte[options.Width * options.Height * 3];
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / options.FramesPerSecond), timeProvider);

        var sent = 0;
        try
        {
            for (var index = 0; index < frameCount; index++)
            {
                TestPattern.Render(frame, options.Width, options.Height, index / framesPerLap);
                await sink.SendImageAsync(frame, options.Width, options.Height, cancellationToken).ConfigureAwait(false);
                sent++;

                if (index < frameCount - 1)
                {
                    await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await TryClearAsync(sink, timeProvider).ConfigureAwait(false);
        }

        return sent;
    }

    private static async Task TryClearAsync(IHyperionSink sink, TimeProvider timeProvider)
    {
        try
        {
            using var timeout = new CancellationTokenSource(ClearTimeout, timeProvider);
            await sink.ClearAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HyperionConnectionException or OperationCanceledException)
        {
            // The connection is gone; Hyperion clears our priority when the socket closes.
        }
    }
}
