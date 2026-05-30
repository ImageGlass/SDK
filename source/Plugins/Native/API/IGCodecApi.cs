/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Runtime.InteropServices;

namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Per-codec function pointer table. The plugin allocates one of these for each codec
/// it advertises and keeps the table alive for the lifetime of the plugin.
/// All callbacks must be exported with <c>[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct IGCodecApi
{
    /// <summary>
    /// Returns the capability descriptor for this codec.
    /// Signature: <c>IGStatus GetCapability(IGCodecCapability* outCapability)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGCodecCapability*, IGStatus> GetCapability;

    /// <summary>
    /// Returns 1 if the codec can handle a file with the given extension (lowercase, with leading dot).
    /// Signature: <c>int CanHandleExtension(IGStringRef extension)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStringRef, int> CanHandleExtension;

    /// <summary>
    /// Optional content-sniffing probe. Returns 1 on match, 0 on no-match.
    /// Signature: <c>int CanHandleSignature(byte* signature, int length)</c>.
    /// May be null; if null the host falls back to <see cref="CanHandleExtension"/>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<byte*, int, int> CanHandleSignature;

    /// <summary>
    /// Loads metadata for the given file path into <paramref name="outImageInfo"/>.
    /// Signature: <c>IGStatus LoadMetadata(IGStringRef filePath, IGImageInfo* outImageInfo, void* cancellation)</c>.
    /// <para>
    /// <c>cancellation</c> is an opaque token supplied by the host; the plugin should call
    /// <see cref="IGHostCoreApi.IsCancellationRequested"/> periodically and return
    /// <see cref="IGStatus.Canceled"/> if it returns 1.
    /// </para>
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStringRef, IGImageInfo*, void*, IGStatus> LoadMetadata;

    /// <summary>
    /// Decodes a single static raster frame from the file path. The plugin allocates the buffer and
    /// fills <paramref name="outBuffer"/>; the host releases it via <see cref="FreePixelBuffer"/>.
    /// <para>
    /// <c>frameIndex</c> selects which frame to decode (0-based). For single-frame
    /// images the host always passes 0; multi-frame plugins must respect this value.
    /// Plugins that do not support multi-frame may treat any non-zero index as
    /// <see cref="IGStatus.InvalidArg"/>.
    /// </para>
    /// Signature: <c>IGStatus DecodeStaticRaster(IGStringRef filePath, int frameIndex, IGPixelBuffer* outBuffer, void* cancellation)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStringRef, int, IGPixelBuffer*, void*, IGStatus> DecodeStaticRaster;

    /// <summary>
    /// Releases a buffer previously returned by <see cref="DecodeStaticRaster"/>
    /// or <see cref="DecodeAnimationFrame"/>.
    /// <para>
    /// MUST be thread-safe. The host hands plugin-owned pixel buffers to SkiaSharp
    /// via <c>SKImage.FromPixels(..., releaseDelegate, ctx)</c>; SkiaSharp may invoke
    /// the release delegate (which calls back into <see cref="FreePixelBuffer"/>) from
    /// any thread when the SKImage is disposed. A typical implementation forwards to
    /// <c>free()</c>, <c>NativeMemory.Free</c>, or <c>CoTaskMemFree</c>, which are all
    /// thread-safe.
    /// </para>
    /// Signature: <c>void FreePixelBuffer(IGPixelBuffer* buffer)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGPixelBuffer*, void> FreePixelBuffer;


    // ============================================================================
    // Animation entry points.
    // All three pointers MUST be non-null when SupportsAnimation = 1.
    // ============================================================================

    /// <summary>
    /// Reports per-codec animation traits and per-frame timing.
    /// Required when the codec advertises <c>IGCodecCapability.SupportsAnimation = 1</c>.
    /// <para>
    /// Plugin allocates <see cref="IGAnimationInfo.Frames"/>; the host MUST release the
    /// entire <see cref="IGAnimationInfo"/> via <see cref="FreeAnimationInfo"/>.
    /// </para>
    /// Signature: <c>IGStatus GetAnimationInfo(IGStringRef filePath, IGAnimationInfo* outInfo, void* cancellation)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStringRef, IGAnimationInfo*, void*, IGStatus> GetAnimationInfo;

    /// <summary>
    /// Releases the frame array (and any other plugin-owned memory) attached to an
    /// <see cref="IGAnimationInfo"/> previously returned by <see cref="GetAnimationInfo"/>.
    /// Signature: <c>void FreeAnimationInfo(IGAnimationInfo* info)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGAnimationInfo*, void> FreeAnimationInfo;

    /// <summary>
    /// Decodes a single animation frame. The plugin allocates the buffer and fills
    /// <paramref name="outBuffer"/>; the host releases it via the codec's existing
    /// <see cref="FreePixelBuffer"/>.
    /// <para>
    /// The buffer MUST hold a fully composed RGBA frame at the full canvas size.
    /// The host does not run sub-rect composition or disposal/blend replay --
    /// plugins for codecs whose native frame stream is sub-rect (e.g. GIF, APNG)
    /// must composite internally before returning.
    /// </para>
    /// Signature: <c>IGStatus DecodeAnimationFrame(IGStringRef filePath, int frameIndex, IGPixelBuffer* outBuffer, void* cancellation)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStringRef, int, IGPixelBuffer*, void*, IGStatus> DecodeAnimationFrame;
}
