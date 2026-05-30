/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// UTF-16 string slice. The pointer is non-owning; the producing side controls the lifetime.
/// Strings passed from host -> plugin live until the call returns.
/// Strings passed from plugin -> host live until the next plugin call on that codec.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGStringRef
{
    /// <summary>Pointer to UTF-16 code units. May be null when <see cref="Length"/> is 0.</summary>
    public char* Data;

    /// <summary>Number of UTF-16 code units (not bytes). Must be non-negative.</summary>
    public int Length;
}


/// <summary>
/// Convenience helpers for working with <see cref="IGStringRef"/> on the SDK side.
/// </summary>
public static unsafe class IGStringRefExtensions
{
    /// <summary>
    /// Materializes the slice as a managed string. Returns <see cref="string.Empty"/> if the slice is empty.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToManaged(this IGStringRef s)
    {
        if (s.Data == null || s.Length <= 0) return string.Empty;
        return new string(s.Data, 0, s.Length);
    }
}
