/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Encoding parameters passed from the host to a codec's encode entry points.
/// All booleans use <see cref="int"/> to stay strictly POD across the ABI.
/// <para>
/// Host-allocated. The pointer is valid only for the duration of the call
/// (or, for a multi-frame session, only for the <c>BeginEncodeMultiFrame</c>
/// call: copy anything you need to keep). The host sets
/// <see cref="StructSize"/> and the plugin MUST NOT read past it.
/// </para>
/// <para>
/// There is no destination-format field: the destination path passed to the
/// encode entry point carries the target extension.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGEncodeOptions
{
    /// <summary>
    /// Size of this struct in bytes, set by the host to <c>sizeof(IGEncodeOptions)</c>.
    /// Plugins must not read fields beyond this offset.
    /// </summary>
    public int StructSize;

    /// <summary>
    /// Requested output quality, 1..100. Meaning is format-specific; ignore it
    /// for formats with no quality knob.
    /// </summary>
    public int Quality;

    /// <summary>
    /// 1 = the caller wants lossless output. Set independently of
    /// <see cref="Quality"/> because "100" means near-lossless in some encoders
    /// and truly lossless in others (WEBP, JXL, AVIF).
    /// </summary>
    public int Lossless;

    /// <summary>
    /// 1 = the source alpha channel is meaningful and must be preserved.
    /// 0 lets the encoder pick an opaque palette or chroma subsampling.
    /// </summary>
    public int PreserveAlpha;

    /// <summary>
    /// Absolute path of the image the pixels came from, or an empty slice when
    /// encoding clipboard content or a selection.
    /// <para>
    /// The host hands over raw pixels only, so EXIF/XMP/ICC of the original are
    /// NOT included. A plugin that wants to carry metadata across a save should
    /// read it from this file. Read-only: the user already has this file open.
    /// </para>
    /// </summary>
    public IGStringRef SourceFilePath;

    /// <summary>
    /// Optional raw ICC profile bytes describing the pixels being handed over.
    /// Null means the pixels are sRGB. Use this to tag the output so colors
    /// survive the round trip.
    /// <para>
    /// Ownership stays with the host; the bytes are valid only for the duration
    /// of the call.
    /// </para>
    /// </summary>
    public byte* IccProfileData;

    /// <summary>
    /// Size in bytes of <see cref="IccProfileData"/>. Zero means no profile.
    /// </summary>
    public int IccProfileSize;
}
