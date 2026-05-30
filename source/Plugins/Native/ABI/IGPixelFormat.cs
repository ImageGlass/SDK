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
    Unknown = 0,
    Bgra8Unorm = 1,
    Rgba8Unorm = 2,
    Rgba16Unorm = 3,
    RgbaFloat16 = 4,
}
