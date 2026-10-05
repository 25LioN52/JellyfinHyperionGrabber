using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// Builds the FFmpeg command line: seek, decode (hardware when possible), scale on the GPU, constant frame rate,
/// raw RGB24 to stdout, no audio or subtitles.
/// </summary>
internal static class FfmpegArguments
{
    /// <summary>
    /// Returns the arguments, one per element (for <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>,
    /// so paths need no quoting).
    /// </summary>
    /// <param name="options">What to decode.</param>
    /// <param name="acceleration">Acceleration for this attempt (may differ from the options after a fallback).</param>
    /// <param name="isWindows">Whether the server runs on Windows (selects the AMF and QSV device setup).</param>
    /// <returns>The argument list.</returns>
    public static List<string> Build(FfmpegFrameSourceOptions options, HardwareAcceleration acceleration, bool isWindows)
    {
        var width = options.OutputWidth.ToString(CultureInfo.InvariantCulture);
        var height = options.OutputHeight.ToString(CultureInfo.InvariantCulture);
        var fps = "fps=" + options.FramesPerSecond.ToString(CultureInfo.InvariantCulture);
        var device = string.IsNullOrWhiteSpace(options.HardwareDevice) ? null : options.HardwareDevice;
        if (acceleration == HardwareAcceleration.Amf && !isWindows)
        {
            // Jellyfin also decodes with VA-API when AMF is selected on Linux.
            acceleration = HardwareAcceleration.Vaapi;
        }

        var args = new List<string> { "-nostdin", "-hide_banner", "-nostats", "-loglevel", "error" };
        string filter;
        switch (acceleration)
        {
            case HardwareAcceleration.Nvenc:
                args.AddRange(["-hwaccel", "cuda", "-hwaccel_output_format", "cuda"]);
                filter = $"{fps},scale_cuda={width}:{height}:format=yuv420p,hwdownload,format=yuv420p,format=rgb24";
                break;

            case HardwareAcceleration.Qsv:
                if (device is not null && !isWindows)
                {
                    args.AddRange(["-init_hw_device", "vaapi=va:" + device, "-init_hw_device", "qsv=qs@va"]);
                }

                args.AddRange(["-hwaccel", "qsv", "-hwaccel_output_format", "qsv"]);
                filter = $"{fps},scale_qsv=w={width}:h={height}:format=nv12,hwdownload,format=nv12,format=rgb24";
                break;

            case HardwareAcceleration.Vaapi:
                if (device is not null)
                {
                    args.AddRange(["-vaapi_device", device]);
                }

                args.AddRange(["-hwaccel", "vaapi", "-hwaccel_output_format", "vaapi"]);
                filter = $"{fps},scale_vaapi=w={width}:h={height}:format=nv12,hwdownload,format=nv12,format=rgb24";
                break;

            case HardwareAcceleration.Rkmpp:
                args.AddRange(["-hwaccel", "rkmpp", "-hwaccel_output_format", "drm_prime"]);
                filter = $"{fps},scale_rkrga=w={width}:h={height}:format=nv12:afbc=0,hwdownload,format=nv12,format=rgb24";
                break;

            case HardwareAcceleration.Amf:
                // Windows: D3D11VA decoding; frames are downloaded and scaled on the CPU.
                args.AddRange(["-hwaccel", "d3d11va"]);
                filter = CpuFilter(fps, width, height);
                break;

            case HardwareAcceleration.VideoToolbox:
                args.AddRange(["-hwaccel", "videotoolbox"]);
                filter = CpuFilter(fps, width, height);
                break;

            default:
                filter = CpuFilter(fps, width, height);
                break;
        }

        if (options.StartPosition > System.TimeSpan.Zero)
        {
            // Input seeking: fast (from the previous keyframe) and accurate; output timestamps then start at 0.
            args.AddRange(["-ss", options.StartPosition.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-i", options.InputPath]);
        args.AddRange(["-map", "0:v:0", "-an", "-sn", "-dn"]);
        args.AddRange(["-vf", filter]);
        args.AddRange(["-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
        return args;
    }

    // "area" averages all source pixels of an output pixel: the right colors for ambient light, cheap at this size.
    private static string CpuFilter(string fps, string width, string height) => $"{fps},scale={width}:{height}:flags=area,format=rgb24";
}
