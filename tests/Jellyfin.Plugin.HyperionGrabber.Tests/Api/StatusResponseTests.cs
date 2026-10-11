using System;
using Jellyfin.Plugin.HyperionGrabber.Api;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Api;

public class StatusResponseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 20, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, 1, "Off", "Follow playback is off")]
    [InlineData(true, 0, "NoDevices", "No device is selected")]
    [InlineData(true, 2, "Waiting", "Waiting for playback on a selected device.")]
    public void From_WithoutPlayback_SaysWhatIsMissing(bool follows, int devices, string state, string summary)
    {
        var response = StatusResponse.From(new PlaybackMonitorStatus(follows, devices, 0, null, null), itemName: null);

        Assert.Equal(state, response.State);
        Assert.StartsWith(summary, response.Summary, StringComparison.Ordinal);
        Assert.Null(response.DeviceName);
        Assert.Null(response.FramesPerSecond);
    }

    [Theory]
    [InlineData(GrabSessionPhase.Starting, "Starting: opening Big Buck Bunny for Living room.")]
    [InlineData(GrabSessionPhase.Streaming, "Streaming Big Buck Bunny from Living room to Hyperion.")]
    [InlineData(GrabSessionPhase.Paused, "Paused on Living room: the lights hold the last picture.")]
    [InlineData(GrabSessionPhase.PausedReleased, "Paused on Living room: the lights are back to Hyperion's default until you resume.")]
    [InlineData(GrabSessionPhase.Reconnecting, "Hyperion is unreachable; reconnecting while Living room plays.")]
    [InlineData(GrabSessionPhase.WaitingForHyperion, "Hyperion has been unreachable for over a minute: decoding stopped, still trying to connect.")]
    [InlineData(GrabSessionPhase.Stopped, "The lights do not follow the playback on Living room.")]
    public void From_WhilePlaying_DescribesThePhase(GrabSessionPhase phase, string summary)
    {
        var response = StatusResponse.From(Playing(new GrabSessionStatus { Phase = phase }), "Big Buck Bunny");

        Assert.Equal(phase.ToString(), response.State);
        Assert.Equal(summary, response.Summary);
    }

    [Fact]
    public void From_WhilePlaying_CopiesTheSessionDetails()
    {
        var session = new GrabSessionStatus
        {
            Phase = GrabSessionPhase.Reconnecting,
            Problem = "Hyperion is unreachable: refused.",
            Position = TimeSpan.FromSeconds(754.36),
            PositionUncertainty = TimeSpan.FromMilliseconds(123),
            FramesPerSecond = 9.97,
            TargetFramesPerSecond = 10,
            FramesSent = 7000,
            FramesDropped = 12,
            DecoderRestarts = 2,
            Decoder = HardwareAcceleration.None,
            IsHardwareFallback = true,
        };

        var response = StatusResponse.From(Playing(session), itemName: null);

        Assert.Equal("Hyperion is unreachable: refused.", response.Problem);
        Assert.Equal(("Living room", "Kodi", Now), (response.DeviceName, response.Client, response.Since));
        Assert.Null(response.ItemName);
        Assert.Equal(754.4, response.PositionSeconds);
        Assert.Equal(0.12, response.PositionUncertaintySeconds);
        Assert.Equal(10.0, response.FramesPerSecond);
        Assert.Equal((10, 7000L, 12L, 2L), (response.TargetFramesPerSecond!.Value, response.FramesSent!.Value, response.FramesDropped!.Value, response.DecoderRestarts!.Value));
        Assert.Equal("CPU", response.Decoder);
        Assert.True(response.HardwareFallback);
    }

    [Fact]
    public void From_WithoutAnItemName_SpeaksOfTheVideo()
    {
        var response = StatusResponse.From(Playing(new GrabSessionStatus { Phase = GrabSessionPhase.Starting }), itemName: null);

        Assert.Equal("Starting: opening the video for Living room.", response.Summary);
    }

    [Fact]
    public void From_KeepsTheLastProblemWithItsTime()
    {
        var problem = new ProblemReport(Now.AddMinutes(-3), "FFmpeg exited with code 1.");

        var response = StatusResponse.From(new PlaybackMonitorStatus(true, 1, 0, null, problem), itemName: null);

        Assert.Equal("FFmpeg exited with code 1.", response.LastProblem);
        Assert.Equal(Now.AddMinutes(-3), response.LastProblemTime);
    }

    [Theory]
    [InlineData(HardwareAcceleration.Qsv, "Intel Quick Sync")]
    [InlineData(HardwareAcceleration.Vaapi, "VA-API")]
    [InlineData(HardwareAcceleration.Nvenc, "NVIDIA")]
    [InlineData(HardwareAcceleration.Amf, "AMD")]
    [InlineData(HardwareAcceleration.VideoToolbox, "VideoToolbox")]
    [InlineData(HardwareAcceleration.Rkmpp, "Rockchip MPP")]
    [InlineData(HardwareAcceleration.None, "CPU")]
    public void From_NamesTheDecoder(HardwareAcceleration decoder, string name)
    {
        var response = StatusResponse.From(Playing(new GrabSessionStatus { Phase = GrabSessionPhase.Streaming, Decoder = decoder }), "x");

        Assert.Equal(name, response.Decoder);
    }

    private static PlaybackMonitorStatus Playing(GrabSessionStatus session)
        => new(true, 1, 0, new FollowedPlayback("Living room", "Kodi", Guid.NewGuid(), Now, session), null);
}
