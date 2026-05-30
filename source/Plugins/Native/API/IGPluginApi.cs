/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Plugin-side API table. Returned from the well-known entry point
/// <see cref="IGNativeAbi.ENTRY_POINT_NAME"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGPluginApi
{
    /// <summary>
    /// Size of this struct in bytes. Plugins set this to <c>sizeof(IGPluginApi)</c> at the time
    /// they were compiled. The host validates this matches its expected size for the negotiated ABI.
    /// </summary>
    public int StructSize;

    /// <summary>ABI version the plugin was built against. Must match the host's major version.</summary>
    public int AbiVersion;

    /// <summary>Identity of the plugin.</summary>
    public IGPluginInfo Info;

    /// <summary>
    /// Returns the codec API table for the given codec index in [0, <see cref="IGPluginInfo.CodecCount"/>).
    /// Signature: <c>IGStatus GetCodec(int index, IGCodecApi** outCodecApi)</c>.
    /// The returned pointer must remain valid for the plugin's lifetime.
    /// </summary>
    public delegate* unmanaged[Cdecl]<int, IGCodecApi**, IGStatus> GetCodec;

    /// <summary>
    /// Optional one-time initialization callback. May be null.
    /// Signature: <c>IGStatus Initialize()</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStatus> Initialize;

    /// <summary>
    /// Optional shutdown callback. May be null. Called once at host shutdown.
    /// Signature: <c>void Shutdown()</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<void> Shutdown;

    /// <summary>
    /// Optional self-test callback. May be null. Returns <see cref="IGStatus.OK"/> on success.
    /// Signature: <c>IGStatus SelfTest()</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStatus> SelfTest;
}
