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
/// the discriminator is preserved so future kinds (e.g. Encoder, Filter, ColorProfile) can be
/// added without breaking the manifest schema.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IGPluginKind>))]
public enum IGPluginKind
{
    /// <summary>Native codec plugin (shared library exposing the codec C ABI).</summary>
    Codec = 0,
}
