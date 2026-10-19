using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// Builds the FFmpeg command line: seek, decode (hardware when possible), scale on the GPU, tone map HDR to SDR,
/// constant frame rate, raw RGB24 to stdout, no audio or subtitles.
/// </summary>
/// <remarks>
/// HDR is tone mapped after scaling, on the CPU: at 160 pixels wide that costs next to nothing and works the same for
/// every acceleration method. The hardware scalers keep 10 bits (<c>p010</c>) so the tone mapper gets the full range.
/// </remarks>
internal static class FfmpegArguments
{
    // Same as Jellyfin's software tone mapping with its default settings (bt2390, desat 0, peak 100). format=yuv420p
    // makes tonemapx take planar 10-bit input, which its Dolby Vision reshaping needs.
    private const string Tonemapx = "tonemapx=tonemap=bt2390:desat=0:peak=100:t=bt709:m=bt709:p=bt709:format=yuv420p";

    // Stock FFmpeg with zimg. Möbius keeps the picture's brightness up to the knee, close to BT.2390; peak=10 is a
    // 1000-nit source like tonemapx's peak=100.
    private const string Zscale = "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=mobius:desat=0:peak=10,zscale=t=bt709:m=bt709:r=tv,format=yuv420p";

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
        var toneMap = options.IsToneMapped;
        if (acceleration == HardwareAcceleration.Qsv
            && options.DynamicRange == VideoDynamicRange.DolbyVision
            && options.ToneMapper == HdrToneMapper.Tonemapx)
        {
            // Quick Sync decoders drop the Dolby Vision metadata that tonemapx applies; like Jellyfin, decode with the
            // system's own decoder API on the same GPU instead: VA-API on Linux, D3D11VA (the AMF path) on Windows.
            acceleration = isWindows ? HardwareAcceleration.Amf : HardwareAcceleration.Vaapi;
        }

        if (acceleration == HardwareAcceleration.Amf && !isWindows)
        {
            // Jellyfin also decodes with VA-API when AMF is selected on Linux.
            acceleration = HardwareAcceleration.Vaapi;
        }

        // Tone mapping needs the 10 bits the hardware scalers would otherwise reduce to 8.
        var gpuFormat = toneMap ? "p010" : "nv12";
        var args = new List<string> { "-nostdin", "-hide_banner", "-nostats", "-loglevel", "error" };
        string scaling;
        switch (acceleration)
        {
            case HardwareAcceleration.Nvenc:
                args.AddRange(["-hwaccel", "cuda", "-hwaccel_output_format", "cuda"]);
                var cudaFormat = toneMap ? "p010" : "yuv420p";
                scaling = $"{fps},scale_cuda={width}:{height}:format={cudaFormat},hwdownload,format={cudaFormat}";
                break;

            case HardwareAcceleration.Qsv:
                if (device is not null && !isWindows)
                {
                    args.AddRange(["-init_hw_device", "vaapi=va:" + device, "-init_hw_device", "qsv=qs@va"]);
                }

                args.AddRange(["-hwaccel", "qsv", "-hwaccel_output_format", "qsv"]);
                scaling = $"{fps},scale_qsv=w={width}:h={height}:format={gpuFormat},hwdownload,format={gpuFormat}";
                break;

            case HardwareAcceleration.Vaapi:
                if (device is not null)
                {
                    args.AddRange(["-vaapi_device", device]);
                }

                args.AddRange(["-hwaccel", "vaapi", "-hwaccel_output_format", "vaapi"]);
                scaling = $"{fps},scale_vaapi=w={width}:h={height}:format={gpuFormat},hwdownload,format={gpuFormat}";
                break;

            case HardwareAcceleration.Rkmpp:
                args.AddRange(["-hwaccel", "rkmpp", "-hwaccel_output_format", "drm_prime"]);
                scaling = $"{fps},scale_rkrga=w={width}:h={height}:format={gpuFormat}:afbc=0,hwdownload,format={gpuFormat}";
                break;

            case HardwareAcceleration.Amf:
                // Windows: D3D11VA decoding; frames are downloaded and scaled on the CPU.
                args.AddRange(["-hwaccel", "d3d11va"]);
                scaling = CpuScaling(fps, width, height, toneMap);
                break;

            case HardwareAcceleration.VideoToolbox:
                args.AddRange(["-hwaccel", "videotoolbox"]);
                scaling = CpuScaling(fps, width, height, toneMap);
                break;

            default:
                scaling = CpuScaling(fps, width, height, toneMap);
                break;
        }

        if (options.StartPosition > System.TimeSpan.Zero)
        {
            // Input seeking: fast (from the previous keyframe) and accurate; output timestamps then start at 0.
            args.AddRange(["-ss", options.StartPosition.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-i", options.InputPath]);
        args.AddRange(["-map", "0:v:0", "-an", "-sn", "-dn"]);
        args.AddRange(["-vf", toneMap ? $"{scaling},{ToneMapping(options)},format=rgb24" : $"{scaling},format=rgb24"]);
        args.AddRange(["-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"]);
        return args;
    }

    // "area" averages all source pixels of an output pixel: the right colors for ambient light, cheap at this size.
    private static string CpuScaling(string fps, string width, string height, bool toneMap)
        => toneMap ? $"{fps},scale={width}:{height}:flags=area,format=yuv420p10le" : $"{fps},scale={width}:{height}:flags=area";

    /// <summary>
    /// The tone mapping filters. Like Jellyfin, the input's color properties are set first: files and hardware scalers
    /// do not always carry them, and the tone mappers rely on them. Dolby Vision profile 5 is tagged PQ; tonemapx
    /// replaces that with the colors its metadata describes.
    /// </summary>
    private static string ToneMapping(FfmpegFrameSourceOptions options)
    {
        var transfer = options.DynamicRange == VideoDynamicRange.Hlg ? "arib-std-b67" : "smpte2084";
        var mapper = options.ToneMapper == HdrToneMapper.Tonemapx ? Tonemapx : Zscale;
        return $"setparams=color_primaries=bt2020:color_trc={transfer}:colorspace=bt2020nc,{mapper}";
    }
}
