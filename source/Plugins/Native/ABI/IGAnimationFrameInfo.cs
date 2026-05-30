/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Per-frame timing for an animated codec.
/// All booleans use <see cref="int"/> to stay strictly POD across the ABI.
/// <para>
/// The host always treats <c>DecodeAnimationFrame</c> output as a fully
/// composed RGBA frame at the full canvas size, so this struct does not
/// describe sub-rect placement, blend operation, or disposal. Plugins that
/// would otherwise emit raw sub-rect frames must run their own composition
/// (matching the codec's disposal rules) before returning the buffer.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct IGAnimationFrameInfo
{
    /// <summary>
    /// Frame display duration in milliseconds. 0 or very small values are
    /// normalized by the host (legacy GIF compatibility).
    /// </summary>
    public int DurationMs;

    /// <summary>1 = this frame has alpha.</summary>
    public int HasAlpha;
}
