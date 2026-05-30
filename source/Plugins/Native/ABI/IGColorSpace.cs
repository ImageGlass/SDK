/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Source color space reported by the plugin in <see cref="IGImageInfo.ColorSpace"/>.
/// Used by the host as a cheap fallback hint when the plugin does not supply a
/// raw ICC profile via <see cref="IGImageInfo.IccProfileData"/>.
/// <para>
/// Values are stable; never reorder or repurpose.
/// </para>
/// </summary>
public enum IGColorSpace : int
{
    /// <summary>Unknown / not reported. The host assumes sRGB.</summary>
    Unknown = 0,

    /// <summary>sRGB (IEC 61966-2-1) with the sRGB transfer.</summary>
    Srgb = 1,

    /// <summary>sRGB primaries with a linear transfer function.</summary>
    LinearSrgb = 2,

    /// <summary>Display P3 (sRGB transfer with P3 D65 primaries).</summary>
    DisplayP3 = 3,

    /// <summary>Adobe RGB (1998).</summary>
    AdobeRgb = 4,

    /// <summary>Rec. 2020 primaries with the Rec.2020 (BT.1886) transfer.</summary>
    Rec2020 = 5,

    /// <summary>Rec. 2020 primaries with linear transfer.</summary>
    Rec2020Linear = 6,
}
