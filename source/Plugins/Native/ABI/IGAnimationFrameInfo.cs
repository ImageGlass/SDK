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
/// composed RGBA frame, so this struct does not describe sub-rect placement,
/// blend operation, or disposal. Plugins that would otherwise emit raw
/// sub-rect frames must run their own composition (matching the codec's
/// disposal rules) before returning the buffer. Frames need not share one
/// size: the host reads each frame's dimensions from the returned pixel
/// buffer and re-fits the viewport when they change.
/// </para>
/// <para>
/// MAINTENANCE WARNING: this struct must never gain fields. The plugin
/// allocates the <c>Frames</c> array and the host walks it with its own
/// <c>sizeof</c>, so growing it would silently cause out-of-bounds reads
/// from every previously built plugin.
/// </para>
/// <para>
/// That restriction follows from WHO ALLOCATES THE ARRAY, not from the struct
/// being an ABI type: there is no <c>StructSize</c> to bound the stride, and a
/// stride mismatch misaligns every element. Structs the host allocates one of
/// and passes by pointer (<see cref="IGEncodeFrameInfo"/>,
/// <see cref="IGMultiFrameEncodeInfo"/>, <see cref="IGEncodeOptions"/>) carry a
/// <c>StructSize</c> and MAY gain trailing fields.
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

    /// <summary>
    /// 1 = this frame has alpha.
    /// </summary>
    public int HasAlpha;
}
