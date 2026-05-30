/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Identity record for a plugin instance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct IGPluginInfo
{
    /// <summary>Stable plugin id (must match manifest <c>Id</c>).</summary>
    public IGStringRef PluginId;

    /// <summary>Plugin display name.</summary>
    public IGStringRef Name;

    /// <summary>Plugin version string.</summary>
    public IGStringRef Version;

    /// <summary>ABI version reported by the plugin. Must equal <see cref="IGNativeAbi.IG_PLUGIN_ABI_VERSION"/>'s major.</summary>
    public int AbiVersion;

    /// <summary>Number of codec entries advertised by this plugin.</summary>
    public int CodecCount;
}
