/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Per-frame descriptor passed alongside each <c>EncodeFrame</c> call.
/// All booleans use <see cref="int"/> to stay strictly POD across the ABI.
/// <para>
/// Host-allocated and valid only for the duration of the call. The host sets
/// <see cref="StructSize"/> and the plugin MUST NOT read past it. Unlike
/// <see cref="IGAnimationFrameInfo"/> this struct MAY gain trailing fields
/// later, because the host passes one pointer rather than an array the plugin
/// has to stride.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct IGEncodeFrameInfo
{
    /// <summary>
    /// Size of this struct in bytes, set by the host to <c>sizeof(IGEncodeFrameInfo)</c>.
    /// Plugins must not read fields beyond this offset.
    /// </summary>
    public int StructSize;

    /// <summary>
    /// 0-based frame index. Strictly increasing across a session, with no gaps.
    /// </summary>
    public int FrameIndex;

    /// <summary>
    /// Frame display duration in milliseconds. Always 0 when the session's
    /// <see cref="IGMultiFrameEncodeInfo.IsAnimated"/> is 0.
    /// </summary>
    public int DurationMs;

    /// <summary>
    /// 1 = this frame has alpha.
    /// </summary>
    public int HasAlpha;
}
