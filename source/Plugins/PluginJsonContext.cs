/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Source-generated JSON serialization context for native plugin types
/// (<see cref="PluginManifest"/>, <see cref="CodecPluginCapability"/>).
/// </summary>
/// <remarks>
/// Writes use camelCase. Reads accept either casing because plugin manifests are hand-authored
/// and frequently use PascalCase.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PluginManifest))]
[JsonSerializable(typeof(CodecPluginCapability))]
public partial class PluginJsonContext : JsonSerializerContext;
