/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Text.Json.Serialization;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Identifies what a native plugin DOES. There is exactly one plugin kind today (codec);
/// the discriminator is preserved so future kinds (e.g. Filter, ColorProfile) can be
/// added without breaking the manifest schema.
/// <para>
/// An encoder is NOT a separate kind. A codec that only writes is still
/// <see cref="Codec"/>, with <c>SupportsStaticRasterDecoding = 0</c> and
/// <c>SupportsStaticRasterEncoding = 1</c> in its <see cref="IGCodecCapability"/>: it loads
/// through the same entry point and the same codec tables, so a second kind would carry no
/// information. Add a kind only for a genuinely different load path.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IGPluginKind>))]
public enum IGPluginKind
{
    /// <summary>
    /// Native codec plugin (shared library exposing the codec C ABI).
    /// </summary>
    Codec = 0,
}
