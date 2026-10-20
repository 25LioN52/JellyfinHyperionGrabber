using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Frames;

/// <summary>
/// Finds FFmpeg and generates test clips: 4 seconds at 25 fps, 320x180, one solid color per second (red, green, blue,
/// white), and a 3-second HDR picture tagged PQ and HLG (<see cref="HdrClipPath"/>). Tests that need FFmpeg call
/// <see cref="SkipIfUnavailable"/>.
/// </summary>
/// <remarks>
/// FFmpeg is taken from <c>HYPERION_GRABBER_FFMPEG</c> or the PATH. Set <c>HYPERION_GRABBER_REQUIRE_FFMPEG=true</c>
/// (CI on Linux does) to fail instead of skipping when it is missing. Jellyfin's FFmpeg (jellyfin-ffmpeg, the one with
/// the <c>tonemapx</c> filter) is taken from <c>HYPERION_GRABBER_JELLYFIN_FFMPEG</c>; CI on Linux installs it.
/// </remarks>
public sealed class FfmpegFixture : IAsyncLifetime
{
    public const int ClipWidth = 320;
    public const int ClipHeight = 180;

    /// <summary>Clip color for each second.</summary>
    public static readonly (byte R, byte G, byte B)[] Colors = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255)];

    /// <summary>
    /// 10-bit limited-range code values (Y, Cb, Cr) of the HDR clip, one per second: black, gray (about 100 nits with
    /// PQ) and a saturated BT.2020 orange.
    /// </summary>
    public static readonly (int Y, int Cb, int Cr)[] HdrCodes = [(64, 512, 512), (512, 512, 512), (400, 400, 700)];

    private static readonly string[] ColorNames = ["red", "lime", "blue", "white"];

    private readonly ConcurrentDictionary<(string Path, string Filter), Task<bool>> _filters = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hyperion-grabber-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Gets a value indicating whether missing FFmpeg features fail tests instead of skipping them.</summary>
    public static bool IsRequired => string.Equals(Environment.GetEnvironmentVariable("HYPERION_GRABBER_REQUIRE_FFMPEG"), "true", StringComparison.OrdinalIgnoreCase);

    public string? FfmpegPath { get; private set; }

    /// <summary>Gets Jellyfin's FFmpeg when <c>HYPERION_GRABBER_JELLYFIN_FFMPEG</c> points to it.</summary>
    public string? JellyfinFfmpegPath { get; } = ExistingFile(Environment.GetEnvironmentVariable("HYPERION_GRABBER_JELLYFIN_FFMPEG"));

    public string ClipPath => Path.Combine(_directory, "colors.mkv");

    public void SkipIfUnavailable() => Assert.SkipWhen(FfmpegPath is null, "FFmpeg is not installed (set HYPERION_GRABBER_FFMPEG or add it to PATH).");

    /// <summary>Returns the lossless 10-bit BT.2020 HDR clip (<see cref="HdrCodes"/>) with a transfer: <c>smpte2084</c> (PQ) or
    /// <c>arib-std-b67</c> (HLG).</summary>
    public string HdrClipPath(string transfer) => Path.Join(_directory, transfer + ".mkv");

    public async ValueTask InitializeAsync()
    {
        FfmpegPath = Locate();
        if (FfmpegPath is null)
        {
            if (IsRequired)
            {
                throw new InvalidOperationException("FFmpeg is required (HYPERION_GRABBER_REQUIRE_FFMPEG=true) but was not found.");
            }

            return;
        }

        Directory.CreateDirectory(_directory);

        // Prefer H.264 (what hardware decoders support); fall back to MPEG-4 Part 2, which every FFmpeg build has.
        if (!await TryGenerateAsync(["-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p"]))
        {
            Assert.True(await TryGenerateAsync(["-c:v", "mpeg4", "-q:v", "2", "-pix_fmt", "yuv420p"]), "Could not generate the test clip.");
        }

        foreach (var transfer in new[] { "smpte2084", "arib-std-b67" })
        {
            Assert.True(await TryGenerateHdrAsync(transfer), $"Could not generate the {transfer} test clip.");
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort; the directory is in the temp folder.
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Returns the PQ clip encoded as HEVC Main 10, which hardware decoders support, or <see langword="null"/> when
    /// FFmpeg has no libx265.
    /// </summary>
    public async Task<string?> GetHevcHdrClipAsync()
    {
        var path = Path.Join(_directory, "smpte2084-hevc.mkv");
        if (!File.Exists(path))
        {
            var (exitCode, _) = await RunAsync(
                FfmpegPath!,
                [
                    "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", HdrClipPath("smpte2084"), "-c:v", "libx265", "-preset", "ultrafast",
                    "-pix_fmt", "yuv420p10le", "-x265-params", "log-level=error:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:range=limited", path,
                ]);
            if (exitCode != 0)
            {
                return null;
            }
        }

        return path;
    }

    /// <summary>Returns the first of the system's and Jellyfin's FFmpeg that has a filter, or <see langword="null"/>.</summary>
    public async Task<string?> FindFfmpegWithFilterAsync(string filter)
    {
        foreach (var path in new[] { FfmpegPath, JellyfinFfmpegPath })
        {
            if (path is not null && await _filters.GetOrAdd((path, filter), key => HasFilterAsync(key.Path, key.Filter)))
            {
                return path;
            }
        }

        return null;
    }

    private static string? ExistingFile(string? path) => !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;

    private static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable("HYPERION_GRABBER_FFMPEG");
        if (!string.IsNullOrEmpty(configured))
        {
            return ExistingFile(configured);
        }

        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), name))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Lists FFmpeg's filters: one per line, flags first, for example <c> .S zscale  V-&gt;V  ...</c>.</summary>
    private static async Task<bool> HasFilterAsync(string ffmpegPath, string filter)
    {
        var (exitCode, output) = await RunAsync(ffmpegPath, ["-hide_banner", "-filters"]);
        return exitCode == 0 && Regex.IsMatch(output, @"^\s*\S+\s+" + Regex.Escape(filter) + @"\s", RegexOptions.Multiline);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string ffmpegPath, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        await stderr;
        return (process.ExitCode, await stdout);
    }

    private async Task<bool> TryGenerateAsync(string[] codec)
    {
        var arguments = new List<string> { "-nostdin", "-hide_banner", "-loglevel", "error", "-y" };
        foreach (var color in ColorNames)
        {
            arguments.AddRange(["-f", "lavfi", "-i", $"color=c={color}:s={ClipWidth}x{ClipHeight}:r=25:d=1"]);
        }

        var inputs = string.Concat(Enumerable.Range(0, ColorNames.Length).Select(i => $"[{i}:v]"));
        arguments.AddRange(["-filter_complex", $"{inputs}concat=n={ColorNames.Length}:v=1:a=0"]);
        arguments.AddRange(codec);
        arguments.AddRange(["-g", "25", ClipPath]);
        var (exitCode, _) = await RunAsync(FfmpegPath!, arguments);
        return exitCode == 0 && File.Exists(ClipPath);
    }

    /// <summary>Writes exact 10-bit code values with geq, stored losslessly (FFV1 is in every FFmpeg build).</summary>
    private async Task<bool> TryGenerateHdrAsync(string transfer)
    {
        static string PerSecond(Func<(int Y, int Cb, int Cr), int> component)
            => $"if(lt(T,1),{component(HdrCodes[0])},if(lt(T,2),{component(HdrCodes[1])},{component(HdrCodes[2])}))";

        var source = $"nullsrc=s={ClipWidth}x{ClipHeight}:r=25:d={HdrCodes.Length},format=yuv420p10le,"
            + $"geq=lum='{PerSecond(c => c.Y)}':cb='{PerSecond(c => c.Cb)}':cr='{PerSecond(c => c.Cr)}',"
            + $"setparams=color_primaries=bt2020:color_trc={transfer}:colorspace=bt2020nc:range=tv";
        var path = HdrClipPath(transfer);
        var (exitCode, _) = await RunAsync(
            FfmpegPath!,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", source, "-c:v", "ffv1",
                "-color_primaries", "bt2020", "-color_trc", transfer, "-colorspace", "bt2020nc", "-color_range", "tv", "-g", "25", path,
            ]);
        return exitCode == 0 && File.Exists(path);
    }
}
