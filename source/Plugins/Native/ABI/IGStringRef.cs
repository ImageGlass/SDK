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
/// <para>
/// C/C++/Rust authors: <see cref="Data"/> is <c>char16_t*</c>, NOT <c>char*</c>. The C# type
/// <c>char</c> is a 16-bit UTF-16 code unit, so the C declaration is:
/// <code>
/// typedef struct IGStringRef { const char16_t* Data; int32_t Length; } IGStringRef;
/// </code>
/// Transcribing it as <c>char*</c> yields half-width strings and garbage lengths.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGStringRef
{
    /// <summary>
    /// Pointer to UTF-16 code units (<c>char16_t*</c> in C, 2 bytes per unit).
    /// May be null when <see cref="Length"/> is 0.
    /// </summary>
    public char* Data;

    /// <summary>
    /// Number of UTF-16 code units, NOT bytes: the byte length is <c>Length * 2</c>.
    /// Must be non-negative. The slice is not null-terminated.
    /// </summary>
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
