/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Cached capability descriptor for one codec advertised by a plugin.
/// JSON-serialized; mirrors the runtime <see cref="IGCodecCapability"/> POD.
/// </summary>
public sealed class CodecPluginCapability
{
    /// <summary>
    /// Gets the stable identifier of the codec inside the plugin.
    /// </summary>
    public required string CodecId { get; init; }

    /// <summary>
    /// Gets the display name shown in diagnostics and codec-selection UI.
    /// </summary>
    public string CodecName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the priority used when choosing a codec for metadata loading.
    /// Higher values win.
    /// </summary>
    public required int MetadataPriority { get; init; }

    /// <summary>
    /// Gets the priority used when choosing a codec for full decode.
    /// Higher values win.
    /// </summary>
    public required int DecodePriority { get; init; }

    /// <summary>
    /// Extensions the codec handles. May be the plugin-reported list or, when
    /// the manifest specifies <see cref="PluginManifest.SupportedExtensions"/>,
    /// the user-overridden list. Always normalized (lowercase, leading dot).
    /// </summary>
    public required string[] SupportedExtensions { get; set; }

    /// <summary>
    /// Gets whether the codec implements metadata probing.
    /// </summary>
    public bool SupportsMetadata { get; init; }

    /// <summary>
    /// Gets whether the codec implements static-raster decoding.
    /// </summary>
    public bool SupportsStaticRaster { get; init; }

    /// <summary>
    /// Gets whether the codec can report embedded color-profile information.
    /// </summary>
    public bool SupportsColorProfiles { get; init; }

    /// <summary>
    /// Gets whether the codec implements the animation entry points
    /// (<c>GetAnimationInfo</c>, <c>FreeAnimationInfo</c>, <c>DecodeAnimationFrame</c>).
    /// </summary>
    public bool SupportsAnimation { get; set; }
}


