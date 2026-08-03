/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Plugins;

/// <summary>
/// JSON metadata for a native plugin, read from <c>igplugin.json</c>.
/// Native plugins are loaded in-process via <c>NativeLibrary.Load</c> and the C ABI in
/// <see cref="IGNativeAbi"/>.
/// <para>
/// This is developer-authored identity metadata only, and it is immutable in both senses: every
/// member is <c>init</c>-only, and the host never writes the file. It carries no file-extension
/// list, because a codec's readable and writable formats are declared in code through
/// <see cref="IGCodecCapability"/> (<c>DecodeExtensions</c> / <c>EncodeExtensions</c>) and are
/// narrowed per user in the host's own settings, never by editing this file.
/// </para>
/// </summary>
public sealed class PluginManifest
{
    /// <summary>
    /// Unique plugin ID (e.g. "Plugin_WicJxr").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name shown in menus.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Short description of the plugin.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Plugin version (e.g. "1.0.0").
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Author name or organization.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Plugin website URL.
    /// </summary>
    public string? Website { get; init; }

    /// <summary>
    /// What the plugin does. Today only <see cref="IGPluginKind.Codec"/> exists.
    /// Defaults to <see cref="IGPluginKind.Codec"/> so manifests that omit this field still load.
    /// </summary>
    public IGPluginKind Kind { get; init; } = IGPluginKind.Codec;

    /// <summary>
    /// Filename of the native shared library, relative to the plugin folder
    /// (e.g. <c>MyPlugin.dll</c>, <c>libMyPlugin.so</c>, <c>MyPlugin.dylib</c>).
    /// </summary>
    public required string Executable { get; init; }

    /// <summary>
    /// Gets the plugin manifest filename.
    /// </summary>
    public static string FILE_NAME { get; } = "igplugin.json";

}
