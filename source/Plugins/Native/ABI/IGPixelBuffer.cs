/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Pixel buffer descriptor returned from a successful decode. Always paired with a free callback.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGPixelBuffer
{
    /// <summary>Pointer to the raw pixels. Owned by whoever set <see cref="ReleaseContext"/>.</summary>
    public byte* Data;

    /// <summary>Pixel width.</summary>
    public int Width;

    /// <summary>Pixel height.</summary>
    public int Height;

    /// <summary>Row stride in bytes. Must be at least <c>Width * bytesPerPixel</c>.</summary>
    public int Stride;

    /// <summary>One of <see cref="IGPixelFormat"/>.</summary>
    public int PixelFormat;

    /// <summary>Opaque value the plugin's free callback uses to identify the buffer.</summary>
    public void* ReleaseContext;
}
