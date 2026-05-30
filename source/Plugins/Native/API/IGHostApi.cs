/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Top-level host API table passed to the plugin via the entry point.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGHostApi
{
    /// <summary>Size of this struct in bytes; set by the host.</summary>
    public int StructSize;

    /// <summary>Host ABI version. Must match what the plugin was built against (major).</summary>
    public int AbiVersion;

    /// <summary>Pointer to the core API table. Never null.</summary>
    public IGHostCoreApi* Core;
}
