namespace Jellyfin.Plugin.HyperionGrabber.Core.Frames;

/// <summary>
/// Hardware decoding method, mirroring Jellyfin's hardware acceleration types.
/// </summary>
public enum HardwareAcceleration
{
    /// <summary>Software decoding and scaling on the CPU.</summary>
    None = 0,

    /// <summary>AMD AMF: D3D11VA decoding on Windows, VA-API decoding and scaling elsewhere.</summary>
    Amf,

    /// <summary>Intel QuickSync: decoding and scaling with <c>scale_qsv</c>.</summary>
    Qsv,

    /// <summary>NVIDIA NVDEC/CUDA: decoding and scaling with <c>scale_cuda</c>.</summary>
    Nvenc,

    /// <summary>VA-API (Intel and AMD on Linux): decoding and scaling with <c>scale_vaapi</c>.</summary>
    Vaapi,

    /// <summary>Apple VideoToolbox: hardware decoding, scaling on the CPU.</summary>
    VideoToolbox,

    /// <summary>Rockchip MPP: decoding with RKMPP and scaling with <c>scale_rkrga</c>.</summary>
    Rkmpp,
}
