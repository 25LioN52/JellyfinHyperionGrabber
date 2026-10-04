using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Microsoft.Extensions.Logging;
using static System.FormattableString;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

/// <summary>
/// Runs the "Test connection" and "Send test pattern" actions of the configuration page.
/// </summary>
/// <remarks>
/// Failures and cancellation are returned as results with an actionable message instead of being thrown, because they
/// are expected while an administrator is still configuring the plugin. Invalid options are the caller's responsibility.
/// </remarks>
public sealed class HyperionConnectionTester
{
    private readonly ILogger<HyperionClient> _clientLogger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperionConnectionTester"/> class.
    /// </summary>
    /// <param name="clientLogger">Logger passed to the clients this tester creates.</param>
    /// <param name="timeProvider">Clock for timing and frame pacing.</param>
    public HyperionConnectionTester(ILogger<HyperionClient> clientLogger, TimeProvider timeProvider)
    {
        _clientLogger = clientLogger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Connects, registers and disconnects.
    /// </summary>
    /// <param name="options">Validated connection options.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>The outcome.</returns>
    public async Task<HyperionTestResult> TestConnectionAsync(HyperionClientOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var started = _timeProvider.GetTimestamp();
        try
        {
            var client = await HyperionClient.ConnectAsync(options, _clientLogger, cancellationToken).ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
            var elapsed = _timeProvider.GetElapsedTime(started);
            return new HyperionTestResult(
                true,
                Invariant($"Connected to {options.Host}:{options.Port} and registered priority {options.Priority} in {elapsed.TotalMilliseconds:0} ms."),
                elapsed);
        }
        catch (Exception ex) when (ex is HyperionConnectionException or HyperionProtocolException)
        {
            return new HyperionTestResult(false, ex.Message, _timeProvider.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The administrator left the page; not an error worth logging.
            return new HyperionTestResult(false, "Cancelled.", _timeProvider.GetElapsedTime(started));
        }
    }

    /// <summary>
    /// Connects and plays the LED layout test pattern, then clears the priority.
    /// </summary>
    /// <param name="options">Validated connection options.</param>
    /// <param name="patternOptions">Validated pattern options.</param>
    /// <param name="cancellationToken">Stops the pattern early.</param>
    /// <returns>The outcome.</returns>
    public async Task<HyperionTestResult> SendTestPatternAsync(
        HyperionClientOptions options,
        TestPatternOptions patternOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(patternOptions);
        var started = _timeProvider.GetTimestamp();
        try
        {
            var client = await HyperionClient.ConnectAsync(options, _clientLogger, cancellationToken).ConfigureAwait(false);
            await using (client.ConfigureAwait(false))
            {
                var frames = await TestPatternPlayer.PlayAsync(client, patternOptions, _timeProvider, cancellationToken).ConfigureAwait(false);
                var elapsed = _timeProvider.GetElapsedTime(started);
                return new HyperionTestResult(
                    true,
                    Invariant($"Sent {frames} test pattern frames to {options.Host}:{options.Port}. Expected: red top, green right, blue bottom, yellow left, white light running clockwise from the top-left corner."),
                    elapsed,
                    frames);
            }
        }
        catch (Exception ex) when (ex is HyperionConnectionException or HyperionProtocolException)
        {
            return new HyperionTestResult(false, ex.Message, _timeProvider.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The administrator left the page; not an error worth logging.
            return new HyperionTestResult(false, "Cancelled.", _timeProvider.GetElapsedTime(started));
        }
    }
}
