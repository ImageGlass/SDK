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
    /// Size of this struct in bytes. The plugin sets this to <c>sizeof(IGCodecApi)</c> at the time
    /// it was compiled; the host reads no member beyond it and rejects the codec if the value is
    /// outside the range it understands.
    /// <para>
    /// Must be first: the plugin owns this allocation and the host reads members by offset, so
    /// this is the only offset guaranteed stable across future additions, and therefore the only
    /// member readable before validation. Allocating the table with a zero-filling allocator
    /// (<c>NativeMemory.AllocZeroed</c>, <c>calloc</c>) means "forgot to set it" reads as 0 and is
    /// rejected cleanly instead of being undefined behavior.
    /// </para>
    /// </summary>
    public int StructSize;

    /// <summary>
    /// Returns the capability descriptor for this codec.
    /// Signature: <c>IGStatus GetCapability(IGCodecCapability** outCapability)</c>.
    /// <para>
    /// The PLUGIN allocates the <see cref="IGCodecCapability"/> and returns a pointer to it; the
    /// allocation must stay valid for the lifetime of the plugin. Allocate it once (a process-
    /// lifetime block or a static) rather than per call.
    /// </para>
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGCodecCapability**, IGStatus> GetCapability;

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
    /// Loads metadata for the given file path into <c>outImageInfo</c>.
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
    /// fills <c>outBuffer</c>; the host releases it via <see cref="FreePixelBuffer"/>.
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
    /// <para>
    /// This is for DECODE output only. Never call it on a buffer the host passed IN to an
    /// encode entry point: that memory belongs to the host.
    /// </para>
    /// Signature: <c>void FreePixelBuffer(IGPixelBuffer* buffer)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGPixelBuffer*, void> FreePixelBuffer;


    // ============================================================================
    // Animation decode entry points.
    // All three pointers MUST be non-null when SupportsAnimationDecoding = 1.
    // ============================================================================

    /// <summary>
    /// Reports per-codec animation traits and per-frame timing.
    /// Required when the codec advertises <c>IGCodecCapability.SupportsAnimationDecoding = 1</c>.
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
    /// <c>outBuffer</c>; the host releases it via the codec's existing
    /// <see cref="FreePixelBuffer"/>.
    /// <para>
    /// The buffer MUST hold a fully composed RGBA frame. The host does not run
    /// sub-rect composition or disposal/blend replay -- plugins for codecs whose
    /// native frame stream is sub-rect (e.g. GIF, APNG) must composite internally
    /// before returning.
    /// </para>
    /// <para>
    /// Frames may differ in size. The host reads each frame's dimensions from
    /// <c>outBuffer</c> and re-fits the viewport when they change, so an animated
    /// format can act as a container of unrelated images. A frame is still always
    /// its own full canvas -- there is no placement offset inside a larger one.
    /// </para>
    /// Signature: <c>IGStatus DecodeAnimationFrame(IGStringRef filePath, int frameIndex, IGPixelBuffer* outBuffer, void* cancellation)</c>.
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStringRef, int, IGPixelBuffer*, void*, IGStatus> DecodeAnimationFrame;


    // ============================================================================
    // Encode entry points.
    //
    // The host passes a destination path it owns: a temp file in the user's chosen
    // folder, carrying the real target extension last (e.g. ".ig-save-<guid>.b64").
    // On success the host moves it into place, so a failed encode can never damage
    // the user's original file. Consequences for the plugin:
    //   - create/overwrite exactly that path, write it completely;
    //   - CLOSE the file handle before returning, on EVERY path including failure,
    //     or the host's move/delete fails with a sharing violation on Windows;
    //   - do not derive the user-visible filename from it; read
    //     IGEncodeOptions.SourceFilePath instead.
    //
    // The source IGPixelBuffer is HOST-owned and valid only for the duration of the
    // call. Never retain it, never pass it to FreePixelBuffer. The host normalizes
    // pixels to IGPixelFormat.Bgra8Unorm (unpremultiplied), but read PixelFormat and
    // return Unsupported for anything you do not handle.
    // ============================================================================

    /// <summary>
    /// Encodes one static raster image to <c>destFilePath</c>.
    /// Required when the codec advertises <c>IGCodecCapability.SupportsStaticRasterEncoding = 1</c>.
    /// Signature: <c>IGStatus EncodeStaticRaster(IGStringRef destFilePath, const IGPixelBuffer* source, const IGEncodeOptions* options, void* cancellation)</c>.
    /// <para>
    /// Return <see cref="IGStatus.Unsupported"/> to decline the format and let the host fall back
    /// to its built-in encoder, <see cref="IGStatus.EncodeFailed"/> when the encoder ran but could
    /// not produce valid output, or <see cref="IGStatus.IoError"/> for a file-level failure.
    /// </para>
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStringRef, IGPixelBuffer*, IGEncodeOptions*, void*, IGStatus> EncodeStaticRaster;

    /// <summary>
    /// Opens a multi-frame encode session writing to <c>destFilePath</c> and returns an opaque
    /// session pointer through <c>outSession</c>.
    /// Required (with the other two session members) when the codec advertises
    /// <c>IGCodecCapability.SupportsMultiFrameEncoding = 1</c>.
    /// Signature: <c>IGStatus BeginEncodeMultiFrame(IGStringRef destFilePath, const IGMultiFrameEncodeInfo* info, const IGEncodeOptions* options, void** outSession, void* cancellation)</c>.
    /// <para>
    /// One session covers both animated formats and page/image containers; branch on
    /// <see cref="IGMultiFrameEncodeInfo.IsAnimated"/>. On <see cref="IGStatus.OK"/> the session
    /// pointer must be non-null and the host calls <see cref="EndEncodeMultiFrame"/> exactly once.
    /// On any other status the host does NOT call <c>End</c>, so release everything before
    /// returning.
    /// </para>
    /// <para>
    /// The host opens at most one session per codec at a time and issues every call for a session
    /// from a single thread, so session state needs no internal locking.
    /// </para>
    /// </summary>
    public delegate* unmanaged[Cdecl]<IGStringRef, IGMultiFrameEncodeInfo*, IGEncodeOptions*, void**, void*, IGStatus> BeginEncodeMultiFrame;

    /// <summary>
    /// Appends one frame to an open session. Called exactly
    /// <see cref="IGMultiFrameEncodeInfo.FrameCount"/> times with strictly increasing
    /// <see cref="IGEncodeFrameInfo.FrameIndex"/> starting at 0.
    /// Signature: <c>IGStatus EncodeFrame(void* session, const IGPixelBuffer* frame, const IGEncodeFrameInfo* frameInfo, void* cancellation)</c>.
    /// <para>
    /// The host holds only one frame in memory at a time, so it cannot re-supply an earlier frame.
    /// An encoder needing a second pass must buffer internally. Any non-OK status aborts the
    /// session and the host follows with <c>EndEncodeMultiFrame(session, commit: 0, ...)</c>.
    /// </para>
    /// </summary>
    public delegate* unmanaged[Cdecl]<void*, IGPixelBuffer*, IGEncodeFrameInfo*, void*, IGStatus> EncodeFrame;

    /// <summary>
    /// Finishes a session and releases it. Signature:
    /// <c>IGStatus EndEncodeMultiFrame(void* session, int commit, void* cancellation)</c>.
    /// <para>
    /// <c>commit</c> = 1: write any trailer and flush. <c>commit</c> = 0: the save was aborted or
    /// canceled, so stop writing; the host discards the temp file either way, so there is no need
    /// to delete it.
    /// </para>
    /// <para>
    /// MUST close the destination file handle and free all session state on BOTH paths. The
    /// session pointer is dead afterwards and the host never reuses it.
    /// </para>
    /// </summary>
    public delegate* unmanaged[Cdecl]<void*, int, void*, IGStatus> EndEncodeMultiFrame;
}
