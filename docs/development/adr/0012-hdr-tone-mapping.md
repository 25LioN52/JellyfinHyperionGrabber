# 0012: Tone map HDR to SDR after scaling, on the CPU, with the best filter Jellyfin's FFmpeg has

**Status:** Accepted (2026-10-19); filter choice amended 2026-10-31 (see [Amendment](#amendment-2026-10-31-zscale-for-hdr10-and-hlg))

## Context

Hyperion takes an 8-bit RGB picture and treats it as SDR. Up to 0.7 the frame source ([ADR 0007](0007-ffmpeg-frame-source.md))
scaled every video straight to RGB24. For HDR that is wrong in two ways: PQ (HDR10) and HLG code values were shown as
if they were gamma-encoded, and BT.2020 primaries as BT.709, so the lights were pale and dull (a 100-nit gray came out
at 130 of 255, a saturated orange as a washed-out brown). Dolby Vision profile 5 has no HDR10-compatible base layer:
its pixels are IPTPQc2, which only becomes a picture with the Dolby Vision metadata (RPU), so it came out green and
purple.

Jellyfin solves the same problem for transcoding with several tone mapping filters: GPU ones per vendor
(`tonemap_opencl`, `tonemap_cuda`, `tonemap_vaapi`, `vpp_qsv`, `tonemap_videotoolbox`, `libplacebo`) and a software one,
`tonemapx`, which jellyfin-ffmpeg adds (SIMD, BT.2390, and it applies Dolby Vision RPUs). Stock FFmpeg has `zscale`
(zimg) with `tonemap`, which handles HDR10 and HLG but cannot apply an RPU.

Options considered:

- **GPU tone mapping like Jellyfin's transcoder:** one code path per vendor, each with its own device setup (OpenCL
  derivation, VPP options) and its own failure modes. That is complex, and pointless at our size.
- **Tone mapping in C#** on the downloaded frame (a LUT per channel): works with any FFmpeg, but Dolby Vision
  profile 5 still needs FFmpeg to parse and apply the RPU, and it means more of our own code on the per-frame path.
- **Software tone mapping in the FFmpeg filter chain, after scaling.**

## Decision

- **Detect** the dynamic range from Jellyfin's media info (`MediaStream.VideoRangeType`, which Jellyfin computes from
  the transfer, the Dolby Vision profile and the base-layer compatibility id; plus the bit depth):
  SDR; PQ (HDR10, HDR10+, Dolby Vision with an HDR10 base layer: profiles 7 and 8.1); HLG (also Dolby Vision 8.4);
  Dolby Vision without a compatible base layer (profile 5). Video under 10 bits counts as SDR, as in Jellyfin.
- **Tone map after scaling, on the CPU.** At 160×90 the tone mapper sees 14,400 pixels per frame: almost no CPU, and
  the same code for every acceleration method. Averaging in the PQ domain before tone mapping instead of in linear
  light changes the colors far less than an LED can show.
- **Hardware scalers keep 10 bits:** `scale_cuda` / `scale_qsv` / `scale_vaapi` / `scale_rkrga` output `p010` instead of
  `nv12`, then `hwdownload,format=p010`. CPU paths scale to `yuv420p10le`.
- **Filter:** chosen from the filters Jellyfin found when it probed its FFmpeg (`IMediaEncoder.SupportsFilter`)
  (as amended below: `zscale` first for HDR10 and HLG, `tonemapx` first for Dolby Vision profile 5):
  1. `tonemapx` with Jellyfin's defaults (`bt2390`, `desat=0`, `peak=100`), output `yuv420p` so it takes planar
     10-bit input, which its Dolby Vision reshaping needs;
  2. `zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=mobius:desat=0:peak=10,zscale=t=bt709:m=bt709:r=tv`.
     Möbius instead of the often used Hable: Hable turns a 100-nit gray into 158 of 255, Möbius into 216, close to
     BT.2390. `peak=10` is a 1000-nit source, the same as `tonemapx`'s `peak=100`;
  3. none: decode as before and warn.
- **Tags first:** like Jellyfin, `setparams` sets BT.2020 primaries and matrix and the PQ or HLG transfer before the
  tone mapper, because files and hardware scalers do not always carry them. Dolby Vision profile 5 is tagged PQ;
  `tonemapx` replaces that with what its RPU describes.
- **Dolby Vision on Quick Sync:** Intel's QSV decoders drop the RPU. For profile 5 with `tonemapx`, decode with the
  system's own API on the same GPU instead, as Jellyfin does: VA-API on Linux, D3D11VA on Windows.
- **No setting.** LEDs cannot show HDR, so HDR is always tone mapped. The status panel and the log say how.

## Consequences

- HDR10 and HLG get saturated, correctly bright colors with jellyfin-ffmpeg and with stock FFmpeg builds that have
  zimg; Dolby Vision profile 5 gets its colors with jellyfin-ffmpeg (every Jellyfin 10.11 and 12 install that uses the
  bundled FFmpeg).
- Black stays black (code value 64 → RGB 0-1), so Hyperion's black border detection keeps working on letterboxed HDR
  films.
- Tests: the command line per acceleration method and filter; FFmpeg integration tests decode a lossless 10-bit PQ and
  HLG clip with every filter the machine has (CI installs jellyfin-ffmpeg next to the system FFmpeg); a manual test
  decodes an HEVC Main 10 clip on real hardware (CUDA and D3D11VA were checked on an RTX 4070).
- Not covered by automated tests: a real Dolby Vision profile 5 file (needs licensed sample content), and whether every
  GPU's VPP supports `p010` output. If a scaler fails, FFmpeg exits before the first frame and the frame source falls
  back to the CPU as before.

## Amendment (2026-10-31): zscale for HDR10 and HLG

With 0.8.0 the maintainer saw Spider-Man's red turn cherry and purple on the LEDs (HDR10 film, Intel Quick Sync,
jellyfin-ffmpeg 7.1). The first tests only checked a color outside BT.709 (it must stay red), not that ordinary colors
keep their hue. A new test converts ordinary SDR colors to HDR10 with zscale (SDR white at 100 nits) and tone maps them
back. On jellyfin-ffmpeg 7.1.4:

| SDR original | `tonemapx` | `zscale` chain |
| --- | --- | --- |
| orange-brown (150, 60, 30) | (116, 43, 31) | (155, 69, 41) |
| crimson (200, 30, 50) | (148, 0, 46): green lost, toward magenta | (190, 37, 57) |
| skin (220, 170, 140) | (162, 124, 106) | (200, 157, 131) |
| blue (60, 120, 200) | (49, 94, 148) | (63, 118, 190) |

`tonemapx` drops the green of saturated reds and lifts small channels, so reds turn cherry. The darker picture is
expected: it takes 203 nits as SDR white (BT.2408). The color shift is not in its tone curve. It stays the same with
`tonemap=none` or `clip`, its SIMD or plain C code (`-cpuflags 0`), 8- or 10-bit output, planar or biplanar
formats, with or without `peak`, and at full resolution. Its output is tagged BT.709 correctly (`showinfo`), and
reading it back explicitly as BT.709 gives the same colors, so the loss happens inside `tonemapx`. Keeping BT.2020
primaries (`p=bt2020`) keeps the green, which points to its BT.2020 → BT.709 conversion. The `zscale` chain keeps every
hue within 1°.

Decision: `FfmpegSettings.GetToneMapper(range)` picks per video.

- HDR10 and HLG: `zscale` when FFmpeg has it (Jellyfin's does), otherwise `tonemapx`.
- Dolby Vision profile 5: `tonemapx`, the only filter that applies the RPU, otherwise `zscale` (wrong colors, warned
  as before).

Jellyfin's own transcoding is not affected by this choice. `Hdr10_KeepsTheHueOfOrdinaryColors` guards it within 5°.
Profile 5 may still show a smaller shift in saturated reds. That is better than the green and purple picture it has
without `tonemapx`, and it needs a profile 5 sample to measure.
