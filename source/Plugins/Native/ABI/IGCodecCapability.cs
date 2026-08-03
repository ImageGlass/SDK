/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Capability descriptor advertised by one codec. Used both at probe time and at codec selection time.
/// All fields use <see cref="int"/> for booleans to stay strictly POD.
/// <para>
/// OWNERSHIP: the PLUGIN allocates this struct and returns a pointer from
/// <c>GetCapability(IGCodecCapability** outCapability)</c>. The allocation and everything it
/// points at must stay valid for the lifetime of the plugin. Do not fill a host-supplied
/// buffer: the host has no way to tell the plugin how large its buffer is before the call,
/// so a plugin built against a larger struct would write past the host's allocation.
/// </para>
/// <para>
/// Capability flags are a 2x2 over {static raster, multi-frame} x {decode, encode}.
/// A codec may implement any subset; a codec that implements none of them is not registered.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGCodecCapability
{
    /// <summary>
    /// Size of this struct in bytes. The plugin sets this to <c>sizeof(IGCodecCapability)</c>
    /// at the time it was compiled; the host reads no field beyond it. Must be first: it is the
    /// only offset guaranteed stable across future additions, so it is the only field the host
    /// can read before validating the rest.
    /// </summary>
    public int StructSize;

    /// <summary>
    /// Stable codec id (e.g. <c>"plugin.wic.jxr"</c>).
    /// </summary>
    public IGStringRef CodecId;

    /// <summary>
    /// Friendly codec name (for diagnostics/UI).
    /// </summary>
    public IGStringRef CodecName;

    /// <summary>
    /// Higher = chosen first when multiple codecs report metadata support.
    /// </summary>
    public int MetadataPriority;

    /// <summary>
    /// Higher = chosen first when multiple codecs report decode support.
    /// </summary>
    public int DecodePriority;

    /// <summary>
    /// 1 if the codec implements <c>LoadMetadata</c>.
    /// </summary>
    public int SupportsMetadata;

    /// <summary>
    /// 1 if the codec returns embedded color profile information.
    /// </summary>
    public int SupportsColorProfiles;

    /// <summary>
    /// 1 if the codec implements <c>DecodeStaticRaster</c>.
    /// </summary>
    public int SupportsStaticRasterDecoding;

    /// <summary>
    /// 1 if the codec implements the animation decode entry points
    /// (<c>GetAnimationInfo</c>, <c>FreeAnimationInfo</c>, <c>DecodeAnimationFrame</c>)
    /// on its <c>IGCodecApi</c>. The host downgrades this flag to 0 if any of the
    /// three function pointers are null.
    /// <para>
    /// Set this only for a real timeline. A page/image container (multi-page TIFF, PDF)
    /// reports <c>FrameCount &gt; 1</c> from <c>LoadMetadata</c> with this flag at 0, and
    /// the host steps its frames through <c>DecodeStaticRaster</c> instead.
    /// </para>
    /// </summary>
    public int SupportsAnimationDecoding;

    /// <summary>
    /// Number of file extensions in <see cref="DecodeExtensions"/>.
    /// </summary>
    public int DecodeExtensionCount;

    /// <summary>
    /// Pointer to an array of <see cref="IGStringRef"/> file extensions the codec can READ
    /// (lowercase, with leading dot). The array and its string data must remain valid for the
    /// lifetime of the plugin.
    /// <para>
    /// This is the only source of a codec's readable formats: the manifest carries no extension
    /// list. The user can narrow this set per plugin in ImageGlass settings.
    /// </para>
    /// </summary>
    public IGStringRef* DecodeExtensions;

    /// <summary>
    /// 1 if the codec implements <c>EncodeStaticRaster</c>. The host downgrades this flag to 0
    /// when that pointer is null or when <see cref="EncodeExtensionCount"/> is 0, because an
    /// empty encode list must never be read as "encodes everything".
    /// </summary>
    public int SupportsStaticRasterEncoding;

    /// <summary>
    /// 1 if the codec implements all three multi-frame encode entry points
    /// (<c>BeginEncodeMultiFrame</c>, <c>EncodeFrame</c>, <c>EndEncodeMultiFrame</c>).
    /// Independent of <see cref="SupportsStaticRasterEncoding"/>, so a codec may write single
    /// PNG but not APNG, or the reverse.
    /// </summary>
    public int SupportsMultiFrameEncoding;

    /// <summary>
    /// Higher = chosen first when multiple codecs can write the same extension.
    /// </summary>
    public int EncodePriority;

    /// <summary>
    /// Number of file extensions in <see cref="EncodeExtensions"/>.
    /// </summary>
    public int EncodeExtensionCount;

    /// <summary>
    /// Pointer to an array of <see cref="IGStringRef"/> file extensions the codec can WRITE
    /// (lowercase, with leading dot). The array and its string data must remain valid for the
    /// lifetime of the plugin.
    /// <para>
    /// Must be non-empty whenever <see cref="SupportsStaticRasterEncoding"/> is 1. These are the
    /// formats the plugin contributes to the app's Save As dialog.
    /// </para>
    /// </summary>
    public IGStringRef* EncodeExtensions;
}
