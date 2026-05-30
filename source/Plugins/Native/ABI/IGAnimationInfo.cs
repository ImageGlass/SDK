/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Top-level descriptor for an animated codec. Returned from
/// <c>IGCodecApi.GetAnimationInfo</c>. The plugin owns <see cref="Frames"/>
/// and the host MUST release the entire structure via <c>IGCodecApi.FreeAnimationInfo</c>.
/// <para>
/// Each <c>DecodeAnimationFrame</c> call is required to return a fully composed
/// RGBA frame at the full canvas size. The host does not run any sub-rect
/// composition or disposal/blend replay -- plugins that would otherwise emit
/// raw sub-rect frames must composite internally.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGAnimationInfo
{
    /// <summary>Total frame count (>= 1).</summary>
    public int FrameCount;

    /// <summary>0 = infinite loop. Otherwise the number of full play-throughs.</summary>
    public int LoopCount;

    /// <summary>
    /// Pointer to <see cref="FrameCount"/> entries. Plugin owns the allocation;
    /// the host releases via <c>IGCodecApi.FreeAnimationInfo</c>.
    /// </summary>
    public IGAnimationFrameInfo* Frames;
}
