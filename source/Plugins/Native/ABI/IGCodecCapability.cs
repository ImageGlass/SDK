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
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGCodecCapability
{
    /// <summary>Stable codec id (e.g. <c>"plugin.wic.jxr"</c>).</summary>
    public IGStringRef CodecId;

    /// <summary>Friendly codec name (for diagnostics/UI).</summary>
    public IGStringRef CodecName;

    /// <summary>Higher = chosen first when multiple codecs report metadata support.</summary>
    public int MetadataPriority;

    /// <summary>Higher = chosen first when multiple codecs report decode support.</summary>
    public int DecodePriority;

    /// <summary>1 if the codec implements <c>LoadMetadata</c>.</summary>
    public int SupportsMetadata;

    /// <summary>1 if the codec implements <c>DecodeStaticRaster</c>.</summary>
    public int SupportsStaticRaster;

    /// <summary>1 if the codec returns embedded color profile information.</summary>
    public int SupportsColorProfiles;

    /// <summary>
    /// 1 if the codec implements the animation entry points
    /// (<c>GetAnimationInfo</c>, <c>FreeAnimationInfo</c>, <c>DecodeAnimationFrame</c>)
    /// on its <c>IGCodecApi</c>. The host downgrades this flag to 0 if any of the
    /// three function pointers are null.
    /// </summary>
    public int SupportsAnimation;

    /// <summary>Number of file extensions in <see cref="Extensions"/>.</summary>
    public int ExtensionCount;

    /// <summary>
    /// Pointer to an array of <see cref="IGStringRef"/> file extensions (lowercase, with leading dot).
    /// The array and its string data must remain valid for the lifetime of the plugin.
    /// </summary>
    public IGStringRef* Extensions;
}
