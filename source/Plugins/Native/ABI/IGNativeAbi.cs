/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Versioning constants and the well-known native entry-point name for the
/// in-process native plugin ABI.
/// <para>
/// NOTE: the v1 contract was revised in place when encoding support was added
/// (encode entry points, <c>StructSize</c> on <see cref="IGCodecApi"/> and
/// <see cref="IGCodecCapability"/>, a pointer-returning <c>GetCapability</c>, and
/// renamed capability flags). The version was deliberately NOT bumped, so every
/// plugin must be rebuilt against this SDK. A plugin built against the earlier
/// contract is refused per codec by the <c>IGCodecApi.StructSize</c> check.
/// </para>
/// </summary>
public static class IGNativeAbi
{
    /// <summary>
    /// The current native plugin ABI version. Encoded as MAJOR * 1_000_000 + MINOR * 1_000 + PATCH.
    /// Major-version mismatch causes the host to reject the plugin outright.
    /// </summary>
    public const int IG_PLUGIN_ABI_VERSION = 1_000_000;

    /// <summary>
    /// Major component of <see cref="IG_PLUGIN_ABI_VERSION"/>. Plugins and host must agree on this.
    /// </summary>
    public const int IG_PLUGIN_ABI_MAJOR = 1;

    /// <summary>
    /// The well-known name of the single C entry point every native plugin must export.
    /// Signature (C):
    /// <code>
    /// const IGPluginApi* ig_plugin_get_api(int hostAbiVersion, const IGHostApi* hostApi);
    /// </code>
    /// Returns null on rejection (e.g., incompatible ABI).
    /// </summary>
    public const string ENTRY_POINT_NAME = "ig_plugin_get_api";
}
