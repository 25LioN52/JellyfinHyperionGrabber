using System;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Playback;

/// <summary>
/// Estimates the playback position from a client's position reports, keeping track of how precise the estimate is.
/// </summary>
/// <remarks>
/// <para>Clients report rarely, and some imprecisely: Jellyfin for Kodi truncates the position to whole seconds and
/// reports it on pause, resume and seek and otherwise about every 4 minutes. Moving the estimate to every report would
/// shift the lights by up to a second each time. Instead the tracker keeps the range the real position can be in:</para>
/// <list type="bullet">
/// <item><description>The first report gives a range of ± <see cref="StartUncertainty"/>: clients send it at
/// different moments around the first picture.</description></item>
/// <item><description>A report in whole seconds (above 0) is taken as truncated: the real position lies within the
/// following second. Any other position is taken as it is. Both get ± <see cref="ReportTolerance"/> for the
/// time the report took to arrive.</description></item>
/// <item><description>A report that agrees with the range narrows it; one that contradicts it (a seek, or the clocks
/// drifted apart) replaces it.</description></item>
/// <item><description>While playing, the range moves with time and widens by <see cref="DriftRate"/> of the elapsed
/// time on each side, because the client's and the server's clocks do not run at exactly the same speed.</description></item>
/// </list>
/// <para>The estimate is the middle of the range, so every report that agrees makes it more precise instead of
/// resetting it. Not thread-safe: a tracker belongs to one session's task.</para>
/// </remarks>
internal sealed class PositionTracker
{
    /// <summary>How much faster or slower than the server's clock a client may play (0.1 %).</summary>
    internal const double DriftRate = 0.001;

    /// <summary>How far the real position may be from the first report.</summary>
    internal static readonly TimeSpan StartUncertainty = TimeSpan.FromSeconds(1);

    /// <summary>How far the real position may be from a report, beyond its precision.</summary>
    internal static readonly TimeSpan ReportTolerance = TimeSpan.FromMilliseconds(100);

    /// <summary>The precision of a whole-second position: the real one may be up to this much later.</summary>
    internal static readonly TimeSpan TruncatedPrecision = TimeSpan.FromSeconds(1);

    // The real position at _at lies between _earliest and _latest.
    private TimeSpan _earliest;
    private TimeSpan _latest;
    private DateTimeOffset _at;
    private bool _paused;

    /// <summary>Initializes a new instance of the <see cref="PositionTracker"/> class.</summary>
    /// <param name="start">The first known state of the playback.</param>
    public PositionTracker(PlaybackState start)
    {
        ArgumentNullException.ThrowIfNull(start);
        _earliest = start.Position - StartUncertainty;
        _latest = start.Position + StartUncertainty;
        _at = start.ReportedAt;
        _paused = start.IsPaused;
    }

    /// <summary>Gets half the width of the range: how far the estimate may be off at most.</summary>
    public TimeSpan Uncertainty => TimeSpan.FromTicks((_latest.Ticks - _earliest.Ticks) / 2);

    /// <summary>Estimates the position at a time: the middle of the range, moved on by the time since while playing.</summary>
    /// <param name="now">The time to estimate for.</param>
    /// <returns>The position; may be negative just before the start.</returns>
    public TimeSpan Estimate(DateTimeOffset now)
    {
        var middle = TimeSpan.FromTicks(_earliest.Ticks + ((_latest.Ticks - _earliest.Ticks) / 2));
        return _paused || now <= _at ? middle : middle + (now - _at);
    }

    /// <summary>Takes a new report into account.</summary>
    /// <param name="report">The playback state from the report.</param>
    /// <returns>How far the report moved the estimate; more than a second means a seek.</returns>
    public TimeSpan Apply(PlaybackState report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var at = report.ReportedAt > _at ? report.ReportedAt : _at;
        var before = Estimate(at);
        MoveTo(at);
        if (report.IsPositionReported)
        {
            // Reports arrive in order; one that is older than the range still describes a playing position later on.
            var position = report.Position;
            if (!report.IsPaused && at > report.ReportedAt)
            {
                position += at - report.ReportedAt;
            }

            Narrow(position);
        }

        _paused = report.IsPaused;
        return Estimate(at) - before;
    }

    private void MoveTo(DateTimeOffset at)
    {
        if (!_paused && at > _at)
        {
            var elapsed = at - _at;
            var drift = elapsed * DriftRate;
            _earliest += elapsed - drift;
            _latest += elapsed + drift;
        }

        _at = at;
    }

    private void Narrow(TimeSpan position)
    {
        // 0 is taken as exact: every client reports the very start as 0.
        var truncated = position > TimeSpan.Zero && position.Ticks % TimeSpan.TicksPerSecond == 0;
        var earliest = position - ReportTolerance;
        var latest = position + ReportTolerance + (truncated ? TruncatedPrecision : TimeSpan.Zero);
        if (earliest > _latest || latest < _earliest)
        {
            _earliest = earliest;
            _latest = latest;
            return;
        }

        if (earliest > _earliest)
        {
            _earliest = earliest;
        }

        if (latest < _latest)
        {
            _latest = latest;
        }
    }
}
