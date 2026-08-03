/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Managed mirror of the runtime <see cref="IGCodecCapability"/> POD, produced by the host when it
/// probes a codec.
/// <para>
/// Fully immutable: the host resolves every value (including downgrading a capability flag whose
/// entry points are missing) before constructing it, so a probed capability cannot drift afterwards.
/// </para>
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
    /// Gets the priority used when choosing a codec to write a given extension.
    /// Higher values win.
    /// </summary>
    public int EncodePriority { get; init; }

    /// <summary>
    /// Gets the extensions the codec can READ, as reported by
    /// <c>IGCodecCapability.DecodeExtensions</c>. Always normalized (lowercase, leading dot).
    /// </summary>
    public required string[] DecodingExtensions { get; init; }

    /// <summary>
    /// Gets the extensions the codec can WRITE, as reported by
    /// <c>IGCodecCapability.EncodeExtensions</c>. Always normalized (lowercase, leading dot).
    /// Empty means the codec encodes nothing; it never means "encodes everything".
    /// </summary>
    public string[] EncodingExtensions { get; init; } = [];

    /// <summary>
    /// Gets whether the codec implements metadata probing.
    /// </summary>
    public bool SupportsMetadata { get; init; }

    /// <summary>
    /// Gets whether the codec can report embedded color-profile information.
    /// </summary>
    public bool SupportsColorProfiles { get; init; }

    /// <summary>
    /// Gets whether the codec implements static-raster decoding (<c>DecodeStaticRaster</c>).
    /// </summary>
    public bool SupportsStaticRasterDecoding { get; init; }

    /// <summary>
    /// Gets whether the codec implements the animation decode entry points
    /// (<c>GetAnimationInfo</c>, <c>FreeAnimationInfo</c>, <c>DecodeAnimationFrame</c>).
    /// The host resolves this to <c>false</c> when any of the three is missing.
    /// </summary>
    public bool SupportsAnimationDecoding { get; init; }

    /// <summary>
    /// Gets whether the codec implements static-raster encoding (<c>EncodeStaticRaster</c>).
    /// The host resolves this to <c>false</c> when the entry point is missing or
    /// <see cref="EncodingExtensions"/> is empty.
    /// </summary>
    public bool SupportsStaticRasterEncoding { get; init; }

    /// <summary>
    /// Gets whether the codec implements the multi-frame encode session
    /// (<c>BeginEncodeMultiFrame</c>, <c>EncodeFrame</c>, <c>EndEncodeMultiFrame</c>).
    /// The host resolves this to <c>false</c> when any of the three is missing.
    /// </summary>
    public bool SupportsMultiFrameEncoding { get; init; }
}
