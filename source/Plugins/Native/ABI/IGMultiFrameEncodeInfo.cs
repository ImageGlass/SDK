/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Describes a multi-frame encode session up front, passed to
/// <c>BeginEncodeMultiFrame</c>. Covers both animated formats and page/image
/// containers; see <see cref="IsAnimated"/>.
/// <para>
/// Host-allocated and valid only for the duration of the <c>Begin</c> call. The
/// host sets <see cref="StructSize"/> and the plugin MUST NOT read past it.
/// Unlike <see cref="IGAnimationFrameInfo"/> this struct MAY gain trailing
/// fields later, because the host allocates exactly one of it and passes a
/// pointer rather than an array the plugin has to stride.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct IGMultiFrameEncodeInfo
{
    /// <summary>
    /// Size of this struct in bytes, set by the host to <c>sizeof(IGMultiFrameEncodeInfo)</c>.
    /// Plugins must not read fields beyond this offset.
    /// </summary>
    public int StructSize;

    /// <summary>
    /// Exact number of <c>EncodeFrame</c> calls that will follow, always >= 1.
    /// </summary>
    public int FrameCount;

    /// <summary>
    /// 1 = the frames form a timeline (animated GIF/WEBP/APNG), so
    /// <see cref="LoopCount"/> and each frame's <c>DurationMs</c> are meaningful.
    /// <para>
    /// 0 = the frames are an unordered-in-time container (multi-page TIFF, PDF,
    /// multi-size ICO). <see cref="LoopCount"/> is ignored and every frame's
    /// <c>DurationMs</c> is 0.
    /// </para>
    /// </summary>
    public int IsAnimated;

    /// <summary>
    /// Number of playback loops when <see cref="IsAnimated"/> is 1; 0 = infinite.
    /// </summary>
    public int LoopCount;

    /// <summary>
    /// Canvas width, or 0 when frames may differ in size and each carries its own.
    /// </summary>
    public int CanvasWidth;

    /// <summary>
    /// Canvas height, or 0 when frames may differ in size and each carries its own.
    /// </summary>
    public int CanvasHeight;
}
