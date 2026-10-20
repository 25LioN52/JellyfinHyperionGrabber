using System;
using System.Linq;
using Jellyfin.Plugin.HyperionGrabber.Api;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests.Api;

public class StatusResponseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 20, 0, 0, TimeSpan.Zero);
    private static readonly Guid ItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly PlaybackTargetKey LivingRoom = new("192.168.1.20", 19400, 150);

    [Theory]
    [InlineData(false, 1, "Off", "Follow playback is off")]
    [InlineData(true, 0, "NoDevices", "No device is selected: tick your TV under Devices and save.")]
    [InlineData(true, 2, "On", "Waiting for playback on a selected device.")]
    public void From_WithOneTarget_SummarizesIt(bool follows, int devices, string state, string summary)
    {
        var response = StatusResponse.From(new PlaybackMonitorStatus(follows, 0, [Target(devices, null)]), NoItem);

        Assert.Equal(state, response.State);
        Assert.StartsWith(summary, response.Summary, StringComparison.Ordinal);
        var target = Assert.Single(response.Targets);
        Assert.Null(target.DeviceName);
        Assert.Null(target.FramesPerSecond);
    }

    [Fact]
    public void From_WithSeveralTargets_CountsThemAndDescribesEach()
    {
        var bedroom = new TargetStatus("Bedroom", new PlaybackTargetKey("192.168.1.21", 19400, 150), 0, null, null);
        var status = new PlaybackMonitorStatus(true, 0, [Target(1, new GrabSessionStatus { Phase = GrabSessionPhase.Streaming }), bedroom]);

        var response = StatusResponse.From(status, _ => "Big Buck Bunny");

        Assert.Equal(("On", "Following playback on 2 Hyperion servers."), (response.State, response.Summary));
        Assert.Equal(["Living room", "Bedroom"], response.Targets.Select(t => t.Name));
        Assert.Equal("192.168.1.21:19400 (priority 150)", response.Targets[1].Address);
        Assert.Equal("Streaming Big Buck Bunny from Living room TV to Hyperion.", response.Targets[0].Summary);
        Assert.Equal(("NoDevices", "No device is selected for this Hyperion server."), (response.Targets[1].State, response.Targets[1].Summary));
    }

    [Fact]
    public void From_WithFollowPlaybackOff_SaysSoForEveryTarget()
    {
        var bedroom = new TargetStatus("Bedroom", new PlaybackTargetKey("192.168.1.21", 19400, 150), 1, null, null);
        var status = new PlaybackMonitorStatus(false, 0, [Target(1, null), bedroom]);

        var response = StatusResponse.From(status, NoItem);

        Assert.Equal("Off", response.State);
        Assert.All(response.Targets, target => Assert.Equal(("Off", "Follow playback is off."), (target.State, target.Summary)));
    }

    [Theory]
    [InlineData(GrabSessionPhase.Starting, "Starting: opening Big Buck Bunny for Living room TV.")]
    [InlineData(GrabSessionPhase.Streaming, "Streaming Big Buck Bunny from Living room TV to Hyperion.")]
    [InlineData(GrabSessionPhase.Paused, "Paused on Living room TV: the lights hold the last picture.")]
    [InlineData(GrabSessionPhase.PausedReleased, "Paused on Living room TV: the lights are back to Hyperion's default until you resume.")]
    [InlineData(GrabSessionPhase.Reconnecting, "Hyperion is unreachable; reconnecting.")]
    [InlineData(GrabSessionPhase.WaitingForHyperion, "Hyperion has been unreachable for over a minute: decoding stopped, still trying to connect.")]
    [InlineData(GrabSessionPhase.Stopped, "The lights do not follow the playback on Living room TV.")]
    public void Target_WhilePlaying_DescribesThePhase(GrabSessionPhase phase, string summary)
    {
        var response = Describe(new GrabSessionStatus { Phase = phase }, "Big Buck Bunny");

        Assert.Equal(phase.ToString(), response.State);
        Assert.Equal(summary, response.Summary);
    }

    [Fact]
    public void Target_WhilePlaying_CopiesTheSessionDetails()
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

        var response = Describe(session, itemName: null);

        Assert.Equal(("Living room", "192.168.1.20:19400 (priority 150)"), (response.Name, response.Address));
        Assert.Equal("Hyperion is unreachable: refused.", response.Problem);
        Assert.Equal(("Living room TV", "Kodi", Now), (response.DeviceName, response.Client, response.Since));
        Assert.Null(response.ItemName);
        Assert.Equal(754.4, response.PositionSeconds);
        Assert.Equal(0.12, response.PositionUncertaintySeconds);
        Assert.Equal(10.0, response.FramesPerSecond);
        Assert.Equal((10, 7000L, 12L, 2L), (response.TargetFramesPerSecond!.Value, response.FramesSent!.Value, response.FramesDropped!.Value, response.DecoderRestarts!.Value));
        Assert.Equal("CPU", response.Decoder);
        Assert.True(response.HardwareFallback);
        Assert.Equal("The client's reports to Jellyfin.", response.Timing);
    }

    [Theory]
    [InlineData(VideoDynamicRange.Sdr, HdrToneMapper.None, "160×90, SDR")]
    [InlineData(VideoDynamicRange.Hdr10, HdrToneMapper.Tonemapx, "160×90, HDR10, tone mapped to SDR with tonemapx")]
    [InlineData(VideoDynamicRange.Hlg, HdrToneMapper.Zscale, "160×90, HLG, tone mapped to SDR with zscale")]
    [InlineData(VideoDynamicRange.DolbyVision, HdrToneMapper.Tonemapx, "160×90, Dolby Vision profile 5, tone mapped to SDR with tonemapx")]
    [InlineData(VideoDynamicRange.DolbyVision, HdrToneMapper.Zscale, "160×90, Dolby Vision profile 5, tone mapped with zscale: only Jellyfin's FFmpeg (tonemapx) gets its colors right")]
    [InlineData(VideoDynamicRange.Hdr10, HdrToneMapper.None, "160×90, HDR10, not tone mapped: the colors look pale (Jellyfin's FFmpeg has neither tonemapx nor zscale)")]
    public void Target_WhilePlaying_DescribesThePicture(VideoDynamicRange range, HdrToneMapper toneMapper, string picture)
    {
        var session = new GrabSessionStatus { Phase = GrabSessionPhase.Streaming, Picture = new PictureFormat(160, 90, range, toneMapper) };

        Assert.Equal(picture, Describe(session, "x").Picture);
    }

    [Fact]
    public void Target_BeforeTheVideoIsKnown_HasNoPicture()
    {
        Assert.Null(Describe(new GrabSessionStatus { Phase = GrabSessionPhase.Starting }, "x").Picture);
    }

    [Fact]
    public void Target_WithAPlayerClock_NamesIt()
    {
        var session = new GrabSessionStatus { Phase = GrabSessionPhase.Streaming, TimingSource = "Kodi's clock at 192.168.1.50:9090." };

        Assert.Equal("Kodi's clock at 192.168.1.50:9090.", Describe(session, "x").Timing);
    }

    [Fact]
    public void Target_WithoutAnItemName_SpeaksOfTheVideo()
    {
        var response = Describe(new GrabSessionStatus { Phase = GrabSessionPhase.Starting }, itemName: null);

        Assert.Equal("Starting: opening the video for Living room TV.", response.Summary);
    }

    [Fact]
    public void Target_LooksUpTheNameOfTheItemItPlays()
    {
        var response = TargetStatusResponse.From(Target(1, new GrabSessionStatus { Phase = GrabSessionPhase.Streaming }), id => id == ItemId ? "Big Buck Bunny" : null);

        Assert.Equal("Big Buck Bunny", response.ItemName);
    }

    [Fact]
    public void Target_KeepsTheLastProblemWithItsTime()
    {
        var problem = new ProblemReport(Now.AddMinutes(-3), "FFmpeg exited with code 1.");

        var response = TargetStatusResponse.From(new TargetStatus("Living room", LivingRoom, 1, null, problem), NoItem);

        Assert.Equal("FFmpeg exited with code 1.", response.LastProblem);
        Assert.Equal(Now.AddMinutes(-3), response.LastProblemTime);
    }

    [Fact]
    public void Target_WithoutAServer_SaysSo()
    {
        var response = TargetStatusResponse.From(new TargetStatus("Hyperion", PlaybackTargetKey.None, 1, null, null), NoItem);

        Assert.Equal("no Hyperion server", response.Address);
    }

    [Theory]
    [InlineData(HardwareAcceleration.Qsv, "Intel Quick Sync")]
    [InlineData(HardwareAcceleration.Vaapi, "VA-API")]
    [InlineData(HardwareAcceleration.Nvenc, "NVIDIA")]
    [InlineData(HardwareAcceleration.Amf, "AMD")]
    [InlineData(HardwareAcceleration.VideoToolbox, "VideoToolbox")]
    [InlineData(HardwareAcceleration.Rkmpp, "Rockchip MPP")]
    [InlineData(HardwareAcceleration.None, "CPU")]
    public void Target_NamesTheDecoder(HardwareAcceleration decoder, string name)
    {
        Assert.Equal(name, Describe(new GrabSessionStatus { Phase = GrabSessionPhase.Streaming, Decoder = decoder }, "x").Decoder);
    }

    private static string? NoItem(Guid itemId) => null;

    private static TargetStatusResponse Describe(GrabSessionStatus session, string? itemName)
        => TargetStatusResponse.From(Target(1, session), _ => itemName);

    private static TargetStatus Target(int devices, GrabSessionStatus? session)
        => new("Living room", LivingRoom, devices, session is null ? null : new FollowedPlayback("Living room TV", "Kodi", ItemId, Now, session), null);
}
