using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Frames;

/// <summary>
/// Finds FFmpeg and generates a test clip: 4 seconds at 25 fps, 320x180, one solid color per second
/// (red, green, blue, white). Tests that need FFmpeg call <see cref="SkipIfUnavailable"/>.
/// </summary>
/// <remarks>
/// FFmpeg is taken from <c>HYPERION_GRABBER_FFMPEG</c> or the PATH. Set <c>HYPERION_GRABBER_REQUIRE_FFMPEG=true</c>
/// (CI on Linux does) to fail instead of skipping when it is missing.
/// </remarks>
public sealed class FfmpegFixture : IAsyncLifetime
{
    public const int ClipWidth = 320;
    public const int ClipHeight = 180;

    /// <summary>Clip color for each second.</summary>
    public static readonly (byte R, byte G, byte B)[] Colors = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 255)];

    private static readonly string[] ColorNames = ["red", "lime", "blue", "white"];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hyperion-grabber-tests-" + Guid.NewGuid().ToString("N"));

    public string? FfmpegPath { get; private set; }

    public string ClipPath => Path.Combine(_directory, "colors.mkv");

    public void SkipIfUnavailable() => Assert.SkipWhen(FfmpegPath is null, "FFmpeg is not installed (set HYPERION_GRABBER_FFMPEG or add it to PATH).");

    public async ValueTask InitializeAsync()
    {
        FfmpegPath = Locate();
        if (FfmpegPath is null)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("HYPERION_GRABBER_REQUIRE_FFMPEG"), "true", StringComparison.OrdinalIgnoreCase))
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

    private static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable("HYPERION_GRABBER_FFMPEG");
        if (!string.IsNullOrEmpty(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), name))
            .FirstOrDefault(File.Exists);
    }

    private async Task<bool> TryGenerateAsync(string[] codec)
    {
        var startInfo = new ProcessStartInfo(FfmpegPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var color in ColorNames)
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("lavfi");
            startInfo.ArgumentList.Add($"-i");
            startInfo.ArgumentList.Add($"color=c={color}:s={ClipWidth}x{ClipHeight}:r=25:d=1");
        }

        var inputs = string.Concat(Enumerable.Range(0, ColorNames.Length).Select(i => $"[{i}:v]"));
        startInfo.ArgumentList.Add("-filter_complex");
        startInfo.ArgumentList.Add($"{inputs}concat=n={ColorNames.Length}:v=1:a=0");
        foreach (var argument in codec.Concat(["-g", "25", ClipPath]))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        await stderr;
        return process.ExitCode == 0 && File.Exists(ClipPath);
    }
}
