/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Plugins;

/// <summary>
/// HDR transfer function reported by the plugin in <see cref="IGImageInfo.HdrTransferFn"/>.
/// Values are stable; never reorder or repurpose.
/// </summary>
public enum IGHdrTransferFn : int
{
    /// <summary>
    /// SDR; no HDR transfer function applies.
    /// </summary>
    None = 0,

    /// <summary>
    /// Perceptual Quantizer (SMPTE ST 2084). HDR10 / Dolby Vision.
    /// </summary>
    PQ = 1,

    /// <summary>
    /// Hybrid Log-Gamma. Broadcast HDR.
    /// </summary>
    HLG = 2,

    /// <summary>
    /// HDR via gain map (Ultra HDR / ISO 21496-1). Base layer is SDR.
    /// </summary>
    GainMap = 3,

    /// <summary>
    /// Scene-referred linear HDR (e.g. OpenEXR, Radiance HDR).
    /// Pixels are already linear, may exceed 1.0, and 1.0 IS diffuse white.
    /// </summary>
    Linear = 4,

    /// <summary>
    /// scRGB extended-range linear (IEC 61966-2-2), the color space WIC infers for every
    /// float / fixed-point format. Like <see cref="Linear"/>, but 1.0 is 80 nits, NOT white.
    /// </summary>
    ScRgb = 5,
}
