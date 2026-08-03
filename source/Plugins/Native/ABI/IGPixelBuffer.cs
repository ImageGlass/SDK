/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Pixel buffer descriptor. Used in both directions, with opposite ownership:
/// <list type="bullet">
///   <item>
///     DECODE output: the plugin allocates it and the host releases it through the codec's
///     <c>FreePixelBuffer</c>.
///   </item>
///   <item>
///     ENCODE input: the HOST allocates it and it is valid only for the duration of the encode
///     call. Never retain it, and never pass it to <c>FreePixelBuffer</c> -- that frees host
///     memory. The host sets <see cref="ReleaseContext"/> to a non-null sentinel it does not
///     interpret, so a symmetric free routine can detect and refuse this case.
///   </item>
/// </list>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGPixelBuffer
{
    /// <summary>
    /// Pointer to the raw pixels. Owned by whoever set <see cref="ReleaseContext"/>.
    /// </summary>
    public byte* Data;

    /// <summary>
    /// Pixel width.
    /// </summary>
    public int Width;

    /// <summary>
    /// Pixel height.
    /// </summary>
    public int Height;

    /// <summary>
    /// Row stride in bytes. Must be at least <c>Width * bytesPerPixel</c>.
    /// </summary>
    public int Stride;

    /// <summary>
    /// One of <see cref="IGPixelFormat"/>.
    /// </summary>
    public int PixelFormat;

    /// <summary>
    /// Opaque value the plugin's free callback uses to identify the buffer.
    /// </summary>
    public void* ReleaseContext;
}
