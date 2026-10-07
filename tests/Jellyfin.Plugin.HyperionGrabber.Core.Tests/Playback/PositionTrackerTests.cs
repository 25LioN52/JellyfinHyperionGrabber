using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Playback;

/// <summary>
/// Position estimates from reports of different precision. Times and positions are in seconds.
/// </summary>
public class PositionTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_EstimateIsTheStartPositionPlusTheTimeSince()
    {
        var tracker = new PositionTracker(Report(60, at: 0));

        AssertNear(62.5, tracker.Estimate(At(2.5)));
        Assert.Equal(PositionTracker.StartUncertainty, tracker.Uncertainty);
    }

    [Fact]
    public void Start_WhilePaused_StaysAtTheStartPosition()
    {
        var tracker = new PositionTracker(Report(60, at: 0, paused: true));

        AssertNear(60, tracker.Estimate(At(30)));
    }

    [Fact]
    public void PreciseReport_MovesTheEstimateToIt()
    {
        var tracker = new PositionTracker(Report(0, at: 0));

        var moved = tracker.Apply(Report(10.3, at: 10));

        AssertNear(10.3, tracker.Estimate(At(10)));
        AssertNear(0.3, moved);
        AssertNear(0.1, tracker.Uncertainty);
    }

    [Fact]
    public void WholeSecondReport_AfterASeek_EstimatesTheMiddleOfThatSecond()
    {
        var tracker = new PositionTracker(Report(0, at: 0));

        tracker.Apply(Report(600, at: 10)); // Jellyfin for Kodi truncates: the real position is between 600 and 601 s.

        AssertNear(600.5, tracker.Estimate(At(10)));
        AssertNear(0.6, tracker.Uncertainty);
    }

    [Fact]
    public void TruncatedReports_AtDifferentMoments_ConvergeOnTheRealPosition()
    {
        // The client really is 0.7 s further than its start report said, and truncates every later report.
        static double Real(double at) => at + 0.7;
        var tracker = new PositionTracker(Report(0, at: 0));

        foreach (var at in new[] { 10, 41.5, 73.25, 100.4 })
        {
            tracker.Apply(Report(Math.Floor(Real(at)), at));

            var error = Real(at) - tracker.Estimate(At(at)).TotalSeconds;
            Assert.InRange(error, -tracker.Uncertainty.TotalSeconds, tracker.Uncertainty.TotalSeconds);
        }

        // Moving the estimate to the last report would be 0.1 s late here, and up to a second after the others.
        AssertNear(Real(100.4), tracker.Estimate(At(100.4)), toleranceMs: 30);
        Assert.InRange(tracker.Uncertainty, TimeSpan.Zero, TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void TruncatedPauseAndResumeReports_KeepThePreciseEstimate()
    {
        var tracker = new PositionTracker(Report(0, at: 0));
        tracker.Apply(Report(40.6, at: 40));

        tracker.Apply(Report(50, at: 50, paused: true)); // Paused at 50.6 s, reported truncated.
        AssertNear(50.6, tracker.Estimate(At(80)), toleranceMs: 15);
        var moved = tracker.Apply(Report(50, at: 80)); // Resumed, reported truncated again.

        AssertNear(0, moved, toleranceMs: 15);
        AssertNear(51.6, tracker.Estimate(At(81)), toleranceMs: 15);
    }

    [Fact]
    public void ContradictingReport_ReplacesTheEstimate()
    {
        var tracker = new PositionTracker(Report(0, at: 0));
        tracker.Apply(Report(30.25, at: 30));

        var moved = tracker.Apply(Report(5.5, at: 40)); // The user jumped back.

        AssertNear(5.5, tracker.Estimate(At(40)));
        AssertNear(-34.75, moved);
    }

    [Fact]
    public void ReportWithoutPosition_OnlyChangesThePause()
    {
        var tracker = new PositionTracker(Report(0, at: 0));
        tracker.Apply(Report(20.5, at: 20));

        tracker.Apply(Report(0, at: 30, paused: true) with { IsPositionReported = false });

        AssertNear(30.5, tracker.Estimate(At(60)));
    }

    [Fact]
    public void Uncertainty_GrowsWhilePlaying_NotWhilePaused()
    {
        var playing = new PositionTracker(Report(60, at: 0));
        var paused = new PositionTracker(Report(60, at: 0, paused: true));

        playing.Apply(Report(0, at: 100) with { IsPositionReported = false });
        paused.Apply(Report(0, at: 100, paused: true) with { IsPositionReported = false });

        AssertNear(1.1, playing.Uncertainty); // ±1 s from the start report plus 0.1 % of 100 s.
        AssertNear(1, paused.Uncertainty);
    }

    [Fact]
    public void ReportAtZero_IsTakenAsExact()
    {
        var tracker = new PositionTracker(Report(0, at: 0, paused: true)); // Opening, before the first picture.

        tracker.Apply(Report(0, at: 2));

        AssertNear(0, tracker.Estimate(At(2)));
        AssertNear(1, tracker.Estimate(At(3)));
    }

    [Fact]
    public void ReportOlderThanTheEstimate_IsMovedOnToItsTime()
    {
        var tracker = new PositionTracker(Report(0, at: 10));

        tracker.Apply(Report(8.5, at: 9)); // 8.5 s at 9 s is 9.5 s at 10 s.

        AssertNear(9.5, tracker.Estimate(At(10)));
    }

    private static DateTimeOffset At(double seconds) => T0 + TimeSpan.FromSeconds(seconds);

    private static PlaybackState Report(double position, double at, bool paused = false) => new()
    {
        SessionId = "session",
        DeviceId = "kodi",
        ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Position = TimeSpan.FromSeconds(position),
        IsPaused = paused,
        ReportedAt = At(at),
    };

    private static void AssertNear(double expectedSeconds, TimeSpan actual, double toleranceMs = 1)
        => Assert.InRange(actual.TotalMilliseconds, (expectedSeconds * 1000) - toleranceMs, (expectedSeconds * 1000) + toleranceMs);
}
