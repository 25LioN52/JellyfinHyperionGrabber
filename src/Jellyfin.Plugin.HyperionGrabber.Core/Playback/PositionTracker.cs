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
/// <item><description>A start report gives a range of ± <see cref="StartUncertainty"/>: it carries the requested start
/// position, and clients send it at different moments around the first picture.</description></item>
/// <item><description>Any other report gives its own precision: a whole-second position (above 0) is taken as
/// truncated, so the real one lies within the following second; a millisecond position is taken as it is, but while
/// playing it may be up to <see cref="MaxReportAge"/> old (the web client sends the position of its last update).
/// Each gets ± <see cref="ReportTolerance"/> for the time the report took to arrive.</description></item>
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

    /// <summary>How far the real position may be from a start report.</summary>
    internal static readonly TimeSpan StartUncertainty = TimeSpan.FromSeconds(1);

    /// <summary>How far the real position may be from any other report, beyond its precision.</summary>
    internal static readonly TimeSpan ReportTolerance = TimeSpan.FromMilliseconds(100);

    /// <summary>The precision of a whole-second position: the real one may be up to this much later.</summary>
    internal static readonly TimeSpan TruncatedPrecision = TimeSpan.FromSeconds(1);

    /// <summary>How old a millisecond position reported while playing may be (the web client's update interval).</summary>
    internal static readonly TimeSpan MaxReportAge = TimeSpan.FromMilliseconds(250);

    // The real position at _at lies between _earliest and _latest.
    private TimeSpan _earliest;
    private TimeSpan _latest;
    private DateTimeOffset _at;
    private bool _paused;

    /// <summary>Initializes a new instance of the <see cref="PositionTracker"/> class.</summary>
    /// <param name="first">The first known state of the playback: usually the start report, but a session can also
    /// begin from a later report (Jellyfin restarted during playback, the device filter changed).</param>
    public PositionTracker(PlaybackState first)
    {
        ArgumentNullException.ThrowIfNull(first);
        _at = first.ReportedAt;
        _paused = first.IsPaused;
        (_earliest, _latest) = first.IsStart || !first.IsPositionReported
            ? StartRange(first.Position)
            : ReportRange(first.Position, first.IsPaused);
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

            if (report.IsStart)
            {
                // A new start: the old range says nothing about it.
                (_earliest, _latest) = StartRange(position);
            }
            else
            {
                Narrow(ReportRange(position, report.IsPaused));
            }
        }

        _paused = report.IsPaused;
        return Estimate(at) - before;
    }

    private static (TimeSpan Earliest, TimeSpan Latest) StartRange(TimeSpan position)
        => (position - StartUncertainty, position + StartUncertainty);

    private static (TimeSpan Earliest, TimeSpan Latest) ReportRange(TimeSpan position, bool paused)
    {
        // 0 is not taken as truncated: every client reports the very start as 0.
        var precision = position > TimeSpan.Zero && position.Ticks % TimeSpan.TicksPerSecond == 0
            ? TruncatedPrecision
            : paused ? TimeSpan.Zero : MaxReportAge;
        return (position - ReportTolerance, position + precision + ReportTolerance);
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

    private void Narrow((TimeSpan Earliest, TimeSpan Latest) report)
    {
        if (report.Earliest > _latest || report.Latest < _earliest)
        {
            (_earliest, _latest) = report;
            return;
        }

        if (report.Earliest > _earliest)
        {
            _earliest = report.Earliest;
        }

        if (report.Latest < _latest)
        {
            _latest = report.Latest;
        }
    }
}
