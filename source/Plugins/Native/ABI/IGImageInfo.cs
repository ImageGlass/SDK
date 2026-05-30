/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Metadata populated by the plugin during a metadata-load call.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGImageInfo
{
    public int Width;
    public int Height;

    /// <summary>One of <see cref="IGPixelFormat"/>.</summary>
    public int PixelFormat;

    /// <summary>1 if the source carries an alpha channel.</summary>
    public int HasAlpha;

    /// <summary>
    /// HDR transfer function of the source (one of <see cref="IGHdrTransferFn"/>).
    /// The plugin owns this decision; the host trusts the value verbatim.
    /// Use <see cref="IGHdrTransferFn.None"/> for SDR.
    /// </summary>
    public int HdrTransferFn;

    /// <summary>
    /// Source color space (one of <see cref="IGColorSpace"/>). Cheap fallback
    /// hint used by the host when <see cref="IccProfileData"/> is null. Use
    /// <see cref="IGColorSpace.Unknown"/> if the plugin cannot determine it
    /// (the host will assume sRGB).
    /// </summary>
    public int ColorSpace;

    /// <summary>EXIF orientation (1..8); 0 means unknown.</summary>
    public int Orientation;

    /// <summary>Frame count (>= 1). Plugins that support multi-frame decoding
    /// must report the actual frame count here.</summary>
    public int FrameCount;

    /// <summary>Source file size in bytes. -1 if unknown.</summary>
    public long FileSizeBytes;

    /// <summary>
    /// Optional pointer to the source's raw ICC profile bytes. When non-null
    /// the host prefers this over <see cref="ColorSpace"/> and builds the Skia
    /// color space via <c>SKColorSpace.CreateIcc</c>, allowing arbitrary
    /// profiles (e.g. ProPhoto RGB) that the <see cref="IGColorSpace"/> enum
    /// cannot express. The host also derives <c>ColorProfileName</c> from
    /// these bytes.
    ///
    /// Ownership stays with the plugin. The host consumes the bytes
    /// synchronously inside the call that returned this struct, so the plugin
    /// only has to keep the allocation alive until the next ABI entry call.
    /// </summary>
    public byte* IccProfileData;

    /// <summary>Size in bytes of <see cref="IccProfileData"/>. Zero means no profile.</summary>
    public int IccProfileSize;
}
