using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Frames;

/// <summary>TEMPORARY: compares tonemapx variants on the HDR10 clip of ordinary colors; prints, never fails.</summary>
public class ToneMappingExperiment(FfmpegFixture ffmpeg) : IClassFixture<FfmpegFixture>
{
    private const string Tags = "setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc";

    [Fact]
    public async Task TonemapxVariants()
    {
        var path = await ffmpeg.FindFfmpegWithFilterAsync("tonemapx");
        var clip = await ffmpeg.GetSdrColorsAsHdr10ClipAsync();
        Assert.SkipWhen(path is null || clip is null, "No tonemapx.");

        var variants = new (string Name, string[] Global, string Filter)[]
        {
            ("base", [], $"format=yuv420p10le,{Tags},tonemapx=tonemap=bt2390:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=yuv420p"),
            ("cpuflags0", ["-cpuflags", "0"], $"format=yuv420p10le,{Tags},tonemapx=tonemap=bt2390:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=yuv420p"),
            ("out10", [], $"format=yuv420p10le,{Tags},tonemapx=tonemap=bt2390:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=yuv420p10le"),
            ("p010-nv12", [], $"format=p010le,{Tags},tonemapx=tonemap=bt2390:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=nv12"),
            ("p010-p010", [], $"format=p010le,{Tags},tonemapx=tonemap=bt2390:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=p010le"),
            ("nopeak", [], $"format=yuv420p10le,{Tags},tonemapx=tonemap=bt2390:desat=0:t=bt709:m=bt709:p=bt709:format=yuv420p"),
            ("none", [], $"format=yuv420p10le,{Tags},tonemapx=tonemap=none:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=yuv420p"),
            ("clip", [], $"format=yuv420p10le,{Tags},tonemapx=tonemap=clip:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=yuv420p"),
            ("keep2020", [], $"format=yuv420p10le,{Tags},tonemapx=tonemap=bt2390:desat=0:peak=100:t=bt709:m=bt709:p=bt2020:format=yuv420p"),
            ("full-res", [], $"format=yuv420p10le,{Tags},tonemapx=tonemap=bt2390:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=yuv420p,scale=160:90:flags=area"),
            ("zscale", [], $"format=yuv420p10le,{Tags},zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=mobius:desat=0:peak=10,zscale=t=bt709:m=bt709:r=tv,format=yuv420p"),
        };

        foreach (var (name, global, filter) in variants)
        {
            var scale = name == "full-res" ? string.Empty : "scale=160:90:flags=area,";
            var colors = await ReadAsync(path!, clip!, global, $"fps=25,{scale}{filter},format=rgb24");
            TestContext.Current.TestOutputHelper?.WriteLine($"EXPERIMENT {name}: {colors}");
        }
    }

    private static async Task<string> ReadAsync(string ffmpegPath, string clip, string[] global, string filter)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in global.Concat(["-nostdin", "-hide_banner", "-loglevel", "error", "-i", clip, "-map", "0:v:0", "-vf", filter, "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        var output = new List<byte>();
        var buffer = new byte[65536];
        int read;
        while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token)) > 0)
        {
            output.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        await process.WaitForExitAsync(timeout.Token);
        var error = await stderr;
        var frameLength = 160 * 90 * 3;
        var results = new List<string>();
        for (var second = 0; second < FfmpegFixture.SdrColors.Length; second++)
        {
            var offset = (((second * 25) + 12) * frameLength) + ((45 * 160) + 80) * 3;
            results.Add(offset + 2 < output.Count ? $"({output[offset]},{output[offset + 1]},{output[offset + 2]})" : "-");
        }

        return string.Join(" ", results) + (error.Length > 0 ? " ERR: " + error.Trim() : string.Empty);
    }
}
