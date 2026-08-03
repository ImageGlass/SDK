/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Host services exposed to plugins for non-codec concerns (logging, allocation, cancellation).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGHostCoreApi
{
    /// <summary>
    /// Logs a UTF-16 message to the host's plugin log channel.
    /// Signature: <c>void Log(int level, IGStringRef message)</c>.
    /// Levels: 0=trace, 1=debug, 2=info, 3=warn, 4=error.
    /// </summary>
    public delegate* unmanaged[Cdecl]<int, IGStringRef, void> Log;

    /// <summary>
    /// Allocates raw memory the plugin can use; freed via <see cref="Free"/>.
    /// Signature: <c>void* Alloc(nuint sizeInBytes)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<nuint, void*> Alloc;

    /// <summary>
    /// Frees memory previously returned by <see cref="Alloc"/>.
    /// Signature: <c>void Free(void* ptr)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<void*, void> Free;

    /// <summary>
    /// Returns 1 if the host has requested cancellation for the given opaque cancellation token.
    /// Signature: <c>int IsCancellationRequested(void* cancellation)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<void*, int> IsCancellationRequested;

    /// <summary>
    /// Returns the absolute UTF-16 path of the host's config directory into the buffer.
    /// Signature: <c>int32_t GetConfigDirectory(char16_t* buffer, int32_t bufferLengthInCodeUnits)</c>.
    /// Returns the number of code units written, or the required size if the buffer is too small.
    /// <para>
    /// The buffer holds <c>char16_t</c> (2 bytes per unit), NOT bytes, and the length is counted in
    /// code units: allocate <c>bufferLengthInCodeUnits * 2</c> bytes. The result is not
    /// null-terminated.
    /// </para>
    /// </summary>
    public delegate* unmanaged[Cdecl]<char*, int, int> GetConfigDirectory;
}
