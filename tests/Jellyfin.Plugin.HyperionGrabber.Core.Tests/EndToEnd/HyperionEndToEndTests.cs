using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.EndToEnd;

/// <summary>
/// Our <see cref="HyperionClient"/> against a real Hyperion.ng or HyperHDR, checked from the inside over JSON-RPC:
/// the priority list (<c>serverinfo</c>) and the colors each instance computes for its LEDs
/// (<see cref="HyperionServerApi.GetLedColorsAsync"/>).
/// </summary>
[Collection(SharedHyperionServer.Name)]
public sealed class HyperionEndToEndTests(HyperionServerFixture fixture)
{
    private const int First = HyperionServerFixture.FirstInstance;
    private const int FrameWidth = 64;
    private const int FrameHeight = 36;

    /// <summary>The test pattern at the middle of each edge, in <see cref="HyperionServerFixture.EdgeRegions"/> order.</summary>
    private static readonly string[] EdgeColors = ["red", "green", "blue", "yellow"];

    /// <summary>The same regions in reverse order, as the second instance's LEDs sample them.</summary>
    private static readonly string[] ReversedEdgeColors = ["yellow", "blue", "green", "red"];

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TestPattern_IsListedWithItsOriginWhilePlaying_AndClearedAfterwards()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.ConnectApiAsync(Ct);
        var options = fixture.ClientOptions;
        await using var client = await HyperionClient.ConnectAsync(options, NullLogger.Instance, Ct);

        var playing = TestPatternPlayer.PlayAsync(client, new TestPatternOptions { Duration = TimeSpan.FromSeconds(6) }, TimeProvider.System, Ct);
        var whilePlaying = await Eventually.ReachAsync(
            () => api.GetPrioritiesAsync(First, Ct),
            priorities => Ours(priorities) is { Active: true, Visible: true },
            Timeout,
            Describe);
        await playing;
        await Eventually.ReachAsync(() => api.GetPrioritiesAsync(First, Ct), priorities => Ours(priorities) is null, Timeout, Describe);

        var entry = Ours(whilePlaying)!;
        Output("instance 0 priorities while playing: " + Describe(whilePlaying));
        Assert.Equal("FLATBUFSERVER", entry.ComponentId);
        Assert.StartsWith(options.Origin + "@", entry.Origin, StringComparison.Ordinal);
        Assert.True(client.IsConnected, "The player's Clear released the priority, not a dropped connection.");
    }

    [Fact]
    public async Task TestPattern_LightsTheLedOfEachEdgeInThatEdgesColor()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.ConnectApiAsync(Ct);
        await using var client = await HyperionClient.ConnectAsync(fixture.ClientOptions, NullLogger.Instance, Ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var streaming = StreamStillFrameAsync(client, stop.Token);
        var colors = await ReadLedColorsUntilAsync(api, First, EdgeColors);
        await stop.CancelAsync();
        await streaming;

        Assert.Equal(EdgeColors, colors.Select(ColorName));
    }

    [Fact]
    public async Task Picture_ReachesEveryRunningInstance_EachThroughItsOwnLayout()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.ConnectApiAsync(Ct);
        await fixture.SetSecondInstanceRunningAsync(api, running: true, Ct);
        await using var client = await HyperionClient.ConnectAsync(fixture.ClientOptions, NullLogger.Instance, Ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var streaming = StreamStillFrameAsync(client, stop.Token);
        var first = await ReadLedColorsUntilAsync(api, First, EdgeColors);
        var second = await ReadLedColorsUntilAsync(api, fixture.SecondInstance, ReversedEdgeColors);
        var onSecond = Ours(await api.GetPrioritiesAsync(fixture.SecondInstance, Ct));
        await stop.CancelAsync();
        await streaming;

        Assert.Equal(EdgeColors, first.Select(ColorName));
        Assert.Equal(ReversedEdgeColors, second.Select(ColorName));
        Assert.NotNull(onSecond);
        Output($"instance {fixture.SecondInstance} lists {onSecond}");
        Assert.StartsWith(fixture.ClientOptions.Origin + "@", onSecond.Origin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Picture_ReachesAnInstanceStartedWhileStreaming()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.ConnectApiAsync(Ct);
        await fixture.SetSecondInstanceRunningAsync(api, running: false, Ct);
        await using var client = await HyperionClient.ConnectAsync(fixture.ClientOptions, NullLogger.Instance, Ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var streaming = StreamStillFrameAsync(client, stop.Token);
        await ReadLedColorsUntilAsync(api, First, EdgeColors);
        await fixture.SetSecondInstanceRunningAsync(api, running: true, Ct);
        var second = await ReadLedColorsUntilAsync(api, fixture.SecondInstance, ReversedEdgeColors);
        await stop.CancelAsync();
        await streaming;

        Assert.Equal(ReversedEdgeColors, second.Select(ColorName));
    }

    [Fact]
    public async Task Picture_ComesBack_AfterTheRemoteControlClearsOurPriority()
    {
        fixture.SkipIfUnavailable();
        await using var api = await fixture.ConnectApiAsync(Ct);
        await using var client = await HyperionClient.ConnectAsync(fixture.ClientOptions, NullLogger.Instance, Ct);
        await StreamUntilLedColorsAsync(client, api, First, EdgeColors);

        // Not sending meanwhile, so nothing can bring the priority back before the clear is seen to have worked.
        await api.ClearAsync(First, fixture.ClientOptions.Priority, Ct);
        await Eventually.ReachAsync(() => api.GetPrioritiesAsync(First, Ct), priorities => Ours(priorities) is null, Timeout, Describe);
        Output($"priority {fixture.ClientOptions.Priority} cleared on instance {First} over JSON-RPC");
        var colors = await StreamUntilLedColorsAsync(client, api, First, EdgeColors);
        var entry = Ours(await api.GetPrioritiesAsync(First, Ct));

        Assert.Equal(EdgeColors, colors.Select(ColorName));
        Assert.NotNull(entry);
        Assert.StartsWith(fixture.ClientOptions.Origin + "@", entry.Origin, StringComparison.Ordinal);
        Assert.True(client.IsConnected, "The picture came back over the same connection, not a new one.");
    }

    /// <summary>Sends the test pattern with its marker in the top-left corner, away from the sampled edge middles, at 10 fps.</summary>
    private static async Task StreamStillFrameAsync(HyperionClient client, CancellationToken cancellationToken)
    {
        var frame = new byte[FrameWidth * FrameHeight * 3];
        TestPattern.Render(frame, FrameWidth, FrameHeight, phase: 0);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            do
            {
                await client.SendImageAsync(frame, FrameWidth, FrameHeight, cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The test has seen what it needed.
        }
    }

    /// <summary>Names a color by the channels that are on, which survives Hyperion's gamma and brightness compensation.</summary>
    private static string ColorName(Rgb color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        if (max < 64)
        {
            return "black";
        }

        var half = max / 2;
        return (color.R > half, color.G > half, color.B > half) switch
        {
            (true, false, false) => "red",
            (false, true, false) => "green",
            (false, false, true) => "blue",
            (true, true, false) => "yellow",
            (true, true, true) => "white",
            _ => "mixed",
        };
    }

    private static string Describe(IReadOnlyList<HyperionPriorityInfo> priorities) => "[" + string.Join(", ", priorities) + "]";

    private static string Describe(int instance, IReadOnlyList<Rgb> colors)
        => colors.Count == 0
            ? $"instance {instance} LEDs: not updated within 1 s (nothing to show)"
            : $"instance {instance} LEDs: " + string.Join(", ", colors.Select(color => $"{ColorName(color)} ({color.R},{color.G},{color.B})"));

    /// <summary>Streams the still frame until an instance's LEDs show the expected colors.</summary>
    private async Task<IReadOnlyList<Rgb>> StreamUntilLedColorsAsync(HyperionClient client, HyperionServerApi api, int instance, IEnumerable<string> expected)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var streaming = StreamStillFrameAsync(client, stop.Token);
        try
        {
            return await ReadLedColorsUntilAsync(api, instance, expected);
        }
        finally
        {
            await stop.CancelAsync();
            await streaming;
        }
    }

    /// <summary>Reads an instance's LED colors until they match (smoothing fades in, a starting instance needs a moment).</summary>
    private async Task<IReadOnlyList<Rgb>> ReadLedColorsUntilAsync(HyperionServerApi api, int instance, IEnumerable<string> expected)
    {
        var colors = await Eventually.ReachAsync(
            () => api.GetLedColorsAsync(instance, Ct),
            leds => leds.Select(ColorName).SequenceEqual(expected),
            Timeout,
            leds => Describe(instance, leds));
        Output(Describe(instance, colors));
        return colors;
    }

    /// <summary>Records what the real server reported in the test output (and the TRX file), for reviews.</summary>
    private void Output(string line) => TestContext.Current.TestOutputHelper?.WriteLine($"{fixture.Server?.Version}: {line}");

    private HyperionPriorityInfo? Ours(IReadOnlyList<HyperionPriorityInfo> priorities)
        => priorities.SingleOrDefault(priority => priority.Priority == fixture.ClientOptions.Priority);
}
