using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;

namespace Jellyfin.Plugin.HyperionGrabber.Api;

/// <summary>
/// What the lights are doing, for the status panel on the configuration page.
/// </summary>
/// <remarks>The wording is built here so the page only displays it. Playback details are <see langword="null"/>
/// while no playback drives the lights.</remarks>
public sealed record StatusResponse
{
    /// <summary>Gets a machine-readable state: <c>Off</c>, <c>NoDevices</c>, <c>Waiting</c> or a
    /// <see cref="GrabSessionPhase"/> name.</summary>
    public required string State { get; init; }

    /// <summary>Gets one sentence for the user.</summary>
    public required string Summary { get; init; }

    /// <summary>Gets what keeps the lights from following playback right now, if anything.</summary>
    public string? Problem { get; init; }

    /// <summary>Gets the playing device.</summary>
    public string? DeviceName { get; init; }

    /// <summary>Gets the client application.</summary>
    public string? Client { get; init; }

    /// <summary>Gets the name of the playing item, if Jellyfin still knows it.</summary>
    public string? ItemName { get; init; }

    /// <summary>Gets when the lights started following the playback.</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>Gets the estimated playback position in seconds.</summary>
    public double? PositionSeconds { get; init; }

    /// <summary>Gets how far the position may be off, in seconds.</summary>
    public double? PositionUncertaintySeconds { get; init; }

    /// <summary>Gets the pictures sent to Hyperion per second, over the last second.</summary>
    public double? FramesPerSecond { get; init; }

    /// <summary>Gets the configured frame rate.</summary>
    public int? TargetFramesPerSecond { get; init; }

    /// <summary>Gets the number of pictures sent.</summary>
    public long? FramesSent { get; init; }

    /// <summary>Gets the number of decoded pictures that were not sent.</summary>
    public long? FramesDropped { get; init; }

    /// <summary>Gets the number of decoder restarts (seeks, lagging decoder).</summary>
    public long? DecoderRestarts { get; init; }

    /// <summary>Gets the decoder in use, in words (for example "Intel Quick Sync" or "CPU").</summary>
    public string? Decoder { get; init; }

    /// <summary>Gets a value indicating whether hardware decoding failed and the CPU decodes.</summary>
    public bool HardwareFallback { get; init; }

    /// <summary>Gets the most recent problem, also after it was resolved.</summary>
    public string? LastProblem { get; init; }

    /// <summary>Gets when <see cref="LastProblem"/> happened.</summary>
    public DateTimeOffset? LastProblemTime { get; init; }

    /// <summary>Creates the response.</summary>
    /// <param name="status">The monitor's status.</param>
    /// <param name="itemName">The playing item's name, if known.</param>
    /// <returns>The response.</returns>
    internal static StatusResponse From(PlaybackMonitorStatus status, string? itemName)
    {
        ArgumentNullException.ThrowIfNull(status);
        var response = new StatusResponse
        {
            State = "Waiting",
            Summary = "Waiting for playback on a selected device.",
            LastProblem = status.LastProblem?.Message,
            LastProblemTime = status.LastProblem?.Time,
        };

        if (status.Playback is not { } playback)
        {
            return !status.FollowsPlayback
                ? response with { State = "Off", Summary = "Follow playback is off: the lights do not follow any playback." }
                : status.DeviceCount == 0
                    ? response with { State = "NoDevices", Summary = "No device is selected: tick your TV under Devices and save." }
                    : response;
        }

        var session = playback.Session;
        var video = string.IsNullOrEmpty(itemName) ? "the video" : itemName;
        return response with
        {
            State = session.Phase.ToString(),
            Summary = Summarize(session.Phase, playback.DeviceName, video),
            Problem = session.Problem,
            DeviceName = playback.DeviceName,
            Client = playback.Client,
            ItemName = itemName,
            Since = playback.Since,
            PositionSeconds = Math.Round(session.Position.TotalSeconds, 1),
            PositionUncertaintySeconds = Math.Round(session.PositionUncertainty.TotalSeconds, 2),
            FramesPerSecond = Math.Round(session.FramesPerSecond, 1),
            TargetFramesPerSecond = session.TargetFramesPerSecond,
            FramesSent = session.FramesSent,
            FramesDropped = session.FramesDropped,
            DecoderRestarts = session.DecoderRestarts,
            Decoder = session.Decoder is { } decoder ? DescribeDecoder(decoder) : null,
            HardwareFallback = session.IsHardwareFallback,
        };
    }

    private static string Summarize(GrabSessionPhase phase, string device, string video) => phase switch
    {
        GrabSessionPhase.Starting => $"Starting: opening {video} for {device}.",
        GrabSessionPhase.Streaming => $"Streaming {video} from {device} to Hyperion.",
        GrabSessionPhase.Paused => $"Paused on {device}: the lights hold the last picture.",
        GrabSessionPhase.PausedReleased => $"Paused on {device}: the lights are back to Hyperion's default until you resume.",
        GrabSessionPhase.Reconnecting => "Hyperion is unreachable; reconnecting.",
        GrabSessionPhase.WaitingForHyperion => "Hyperion has been unreachable for over a minute: decoding stopped, still trying to connect.",
        _ => $"The lights do not follow the playback on {device}.",
    };

    private static string DescribeDecoder(HardwareAcceleration decoder) => decoder switch
    {
        HardwareAcceleration.None => "CPU",
        HardwareAcceleration.Qsv => "Intel Quick Sync",
        HardwareAcceleration.Vaapi => "VA-API",
        HardwareAcceleration.Nvenc => "NVIDIA",
        HardwareAcceleration.Amf => "AMD",
        HardwareAcceleration.VideoToolbox => "VideoToolbox",
        HardwareAcceleration.Rkmpp => "Rockchip MPP",
        _ => decoder.ToString(),
    };
}
