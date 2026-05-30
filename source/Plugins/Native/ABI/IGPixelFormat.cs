/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Pixel format identifiers used by <see cref="IGPixelBuffer"/> and <see cref="IGImageInfo"/>.
/// Values are stable; never reorder or repurpose.
/// </summary>
public enum IGPixelFormat : int
{
    /// <summary>Unknown or unspecified pixel format.</summary>
    Unknown = 0,

    /// <summary>8 bits per channel, BGRA byte order, unsigned normalized.</summary>
    Bgra8Unorm = 1,

    /// <summary>8 bits per channel, RGBA byte order, unsigned normalized.</summary>
    Rgba8Unorm = 2,

    /// <summary>16 bits per channel, RGBA, unsigned normalized.</summary>
    Rgba16Unorm = 3,

    /// <summary>16-bit half-float per channel, RGBA (used for HDR content).</summary>
    RgbaFloat16 = 4,
}
