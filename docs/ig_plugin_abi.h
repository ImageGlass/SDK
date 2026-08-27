/*
 * ig_plugin_abi.h -- ImageGlass 10 native codec plugin ABI (C declaration)
 *
 * Copyright (C) 2026 DUONG DIEU PHAP
 * Project homepage: https://imageglass.org
 * MIT License
 *
 * ---------------------------------------------------------------------------
 * WHAT THIS IS
 *
 * The canonical C form of the ABI defined in C# under source/Plugins/Native/.
 * Use it to write a plugin in C, C++, Rust (via bindgen), Zig, or anything else
 * that can export a C entry point and produce a native shared library.
 *
 * The C# structs are the source of truth. Nothing in the SDK build compiles this
 * header, so it MUST be reviewed against source/Plugins/Native/ whenever those
 * files change. Field order and types here are byte-for-byte equivalents; the XML
 * docs on the C# members carry the full contract and are not repeated in full.
 *
 * ---------------------------------------------------------------------------
 * RULES THAT APPLY EVERYWHERE
 *
 *  1. Never hand-compute a struct size or field offset. Always use sizeof.
 *     Every struct here is natural-alignment / no-packing, which is what C, C++
 *     and Rust #[repr(C)] all produce, so both sides agree as long as neither
 *     assumes a literal. Note an int32_t followed by a pointer means 4 bytes of
 *     tail padding on 64-bit; that is expected.
 *
 *  2. StructSize is a contract, not decoration. Where a struct has one, the side
 *     that ALLOCATES sets it to sizeof(that struct) and the reader must not touch
 *     anything beyond it. Allocate with a zero-filling allocator (calloc) so a
 *     forgotten StructSize reads as 0 and is rejected cleanly rather than being
 *     undefined behavior.
 *
 *  3. Strings are UTF-16, not UTF-8. IGStringRef.Data is char16_t*, Length counts
 *     CODE UNITS (byte length is Length * 2), and the slice is NOT null-terminated.
 *     On POSIX you must convert to UTF-8 before calling open()/fopen().
 *
 *  4. Fixed-width integers only. Never declare an ABI field as int, long, or char:
 *     C long is 32-bit on Windows (LLP64) and 64-bit elsewhere, and C char is
 *     8-bit while C# char is 16-bit.
 *
 *  5. Booleans cross as int32_t (0 or 1), never C _Bool / C++ bool / Rust bool.
 *
 *  6. Enum values are stable and append-only. Never reorder or repurpose them, and
 *     treat an unknown value as a failure rather than asserting. Rust callers
 *     should return plain i32 for IGStatus: a #[repr(i32)] enum holding a
 *     discriminant it does not declare is undefined behavior, and new statuses are
 *     added over time.
 *
 *  7. Never let a language-level exception cross the boundary. C++ must catch
 *     inside every entry point. In Rust, a panic unwinding through extern "C" is
 *     UB before 1.81 and an abort after, so wrap bodies in catch_unwind.
 *
 *  8. Cdecl is the calling convention. It is the only convention on x86-64 SysV
 *     and AArch64, so it matters in practice only on x86-32, which is not a target.
 *
 * ---------------------------------------------------------------------------
 * MEMORY OWNERSHIP, IN ONE PLACE
 *
 *   Plugin allocates, plugin frees:
 *     - the IGPluginApi and IGCodecApi tables, and everything IGCodecCapability
 *       points at: all must live for the lifetime of the plugin;
 *     - the IGCodecCapability itself (returned by pointer from GetCapability);
 *     - decode pixel buffers, released by the host calling FreePixelBuffer;
 *     - IGAnimationInfo.Frames, released by the host calling FreeAnimationInfo;
 *     - multi-frame encode session state, released in EndEncodeMultiFrame.
 *
 *   Host allocates, host frees:
 *     - IGHostApi / IGHostCoreApi;
 *     - IGImageInfo (you fill it in place);
 *     - IGEncodeOptions, IGMultiFrameEncodeInfo, IGEncodeFrameInfo;
 *     - the ENCODE INPUT pixel buffer. Valid only for the duration of the call.
 *       Never retain it and never pass it to FreePixelBuffer: that frees host
 *       memory. Its ReleaseContext is a non-null host sentinel so a symmetric free
 *       routine can detect and refuse it.
 *
 *   The host may unload the library while decode buffers are still outstanding, so
 *   FreePixelBuffer is not guaranteed to run for every buffer at teardown. Do not
 *   rely on it for correctness accounting.
 */

#ifndef IG_PLUGIN_ABI_H
#define IG_PLUGIN_ABI_H

#include <stddef.h>
#include <stdint.h>

/* char16_t: a built-in type in C++11+, and in <uchar.h> from C11. Fall back to a
 * typedef because some platforms ship no <uchar.h> (older Apple SDKs). */
#if defined(__cplusplus)
  /* built-in */
#elif defined(__STDC_VERSION__) && __STDC_VERSION__ >= 201112L
  #include <uchar.h>
#else
  typedef uint16_t char16_t;
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ===========================================================================
 * Versioning
 *
 * MAJOR * 1000000 + MINOR * 1000 + PATCH. Major mismatch = the host rejects the
 * plugin outright.
 *
 * NOTE: the v1 contract was revised in place when encoding was added, and the
 * version was deliberately NOT bumped. Every plugin must be rebuilt against this
 * header. A plugin built against the earlier contract is refused per codec by the
 * IGCodecApi.StructSize check.
 *
 * MINOR 1 (1001000 and later) added IGHdrTransferFn_ScRgb. Emitting it to an
 * older host degrades to IGHdrTransferFn_None, so gate it on the hostAbiVersion
 * passed to ig_plugin_get_api and fall back to IGHdrTransferFn_Linear below 1001000.
 * ========================================================================= */

#define IG_PLUGIN_ABI_VERSION 1002000
#define IG_PLUGIN_ABI_MAJOR   1
#define IG_PLUGIN_ENTRY_POINT_NAME "ig_plugin_get_api"

/* ===========================================================================
 * Enums (all int32_t)
 * ========================================================================= */

typedef enum IGStatus {
    IGStatus_OK             = 0,
    IGStatus_Unsupported    = 1, /* decline; the host falls back to a built-in */
    IGStatus_Canceled       = 2,
    IGStatus_InvalidArg     = 3,
    IGStatus_DecodeFailed   = 4,
    IGStatus_OutOfMemory    = 5,
    IGStatus_Internal       = 6,
    IGStatus_NotImplemented = 7,
    IGStatus_IoError        = 8,
    IGStatus_EncodeFailed   = 9  /* encoder ran, output invalid */
} IGStatus;

typedef enum IGPixelFormat {
    IGPixelFormat_Unknown     = 0,
    IGPixelFormat_Bgra8Unorm  = 1,
    IGPixelFormat_Rgba8Unorm  = 2,
    IGPixelFormat_Rgba16Unorm = 3,
    IGPixelFormat_RgbaFloat16 = 4
} IGPixelFormat;

typedef enum IGColorSpace {
    IGColorSpace_Unknown       = 0,
    IGColorSpace_Srgb          = 1,
    IGColorSpace_LinearSrgb    = 2,
    IGColorSpace_DisplayP3     = 3,
    IGColorSpace_AdobeRgb      = 4,
    IGColorSpace_Rec2020       = 5,
    IGColorSpace_Rec2020Linear = 6
} IGColorSpace;

typedef enum IGHdrTransferFn {
    IGHdrTransferFn_None    = 0,
    IGHdrTransferFn_PQ      = 1,
    IGHdrTransferFn_HLG     = 2,
    IGHdrTransferFn_GainMap = 3,
    IGHdrTransferFn_Linear  = 4, /* scene-referred linear; 1.0 IS diffuse white */
    IGHdrTransferFn_ScRgb   = 5  /* scRGB extended-range linear; 1.0 is 80 nits, NOT white */
} IGHdrTransferFn;

/* IGPluginKind is manifest JSON only ("kind": "Codec") and never crosses the ABI.
 * An encode-only codec is still kind Codec: it declares
 * SupportsStaticRasterDecoding = 0 and SupportsStaticRasterEncoding = 1. */

/* ===========================================================================
 * Data structs
 * ========================================================================= */

/* UTF-16 slice. Non-owning; the producing side controls the lifetime.
 * Host -> plugin: valid until the call returns.
 * Plugin -> host: valid until the next call on that codec. */
typedef struct IGStringRef {
    const char16_t* Data;   /* NOT char*; may be NULL when Length == 0 */
    int32_t         Length; /* UTF-16 code units, not bytes; not null-terminated */
} IGStringRef;

/* Used in BOTH directions; see MEMORY OWNERSHIP above. */
typedef struct IGPixelBuffer {
    uint8_t* Data;
    int32_t  Width;
    int32_t  Height;
    int32_t  Stride;         /* bytes per row, >= Width * bytesPerPixel */
    int32_t  PixelFormat;    /* an IGPixelFormat value */
    void*    ReleaseContext; /* decode: yours. encode: host sentinel, do not free */
} IGPixelBuffer;

/* Host-allocated; the plugin fills it during LoadMetadata. */
typedef struct IGImageInfo {
    int32_t        Width;
    int32_t        Height;
    int32_t        PixelFormat;    /* an IGPixelFormat value */
    int32_t        HasAlpha;
    int32_t        HdrTransferFn;  /* an IGHdrTransferFn value */
    int32_t        ColorSpace;     /* an IGColorSpace value; fallback for IccProfileData */
    int32_t        Orientation;    /* EXIF 1..8; 0 = unknown */
    int32_t        FrameCount;     /* >= 1 */
    int64_t        FileSizeBytes;  /* int64_t, NOT long; -1 = unknown */
    const uint8_t* IccProfileData; /* plugin-owned, read synchronously by the host */
    int32_t        IccProfileSize;
} IGImageInfo;

/* MUST NEVER GAIN FIELDS: the plugin allocates the array and the host strides it
 * with its own sizeof, so any size disagreement misaligns every element. That
 * restriction comes from who allocates the array, not from being an ABI type;
 * structs the host allocates one of and passes by pointer carry a StructSize and
 * may gain trailing fields. */
typedef struct IGAnimationFrameInfo {
    int32_t DurationMs;
    int32_t HasAlpha;
} IGAnimationFrameInfo;

typedef struct IGAnimationInfo {
    int32_t               FrameCount;
    int32_t               LoopCount; /* 0 = infinite */
    IGAnimationFrameInfo* Frames;    /* plugin-owned; host calls FreeAnimationInfo */
} IGAnimationInfo;

/* Host-allocated. No destination-format field: the destination path passed to the
 * encode entry point carries the target extension. */
typedef struct IGEncodeOptions {
    int32_t        StructSize;      /* host sets; do not read past it */
    int32_t        Quality;         /* 1..100 */
    int32_t        Lossless;        /* independent of Quality */
    int32_t        PreserveAlpha;
    IGStringRef    SourceFilePath;  /* original file, empty for clipboard/selection.
                                     * Read EXIF/XMP/ICC from here: the host hands
                                     * over raw pixels only. */
    const uint8_t* IccProfileData;  /* NULL = the pixels are sRGB */
    int32_t        IccProfileSize;
} IGEncodeOptions;

/* Host-allocated, passed to BeginEncodeMultiFrame. Covers animated formats and
 * page/image containers alike; branch on IsAnimated. */
typedef struct IGMultiFrameEncodeInfo {
    int32_t StructSize;   /* host sets; do not read past it */
    int32_t FrameCount;   /* exact number of EncodeFrame calls that follow */
    int32_t IsAnimated;   /* 1 = timeline (durations + LoopCount meaningful)
                           * 0 = container: TIFF pages, PDF, multi-size ICO */
    int32_t LoopCount;    /* 0 = infinite; ignored when IsAnimated == 0 */
    int32_t CanvasWidth;  /* 0 = frames carry their own size */
    int32_t CanvasHeight;
} IGMultiFrameEncodeInfo;

/* Host-allocated, passed with each EncodeFrame call. */
typedef struct IGEncodeFrameInfo {
    int32_t StructSize; /* host sets; do not read past it */
    int32_t FrameIndex; /* 0-based, strictly increasing, no gaps */
    int32_t DurationMs; /* always 0 when IsAnimated == 0 */
    int32_t HasAlpha;
} IGEncodeFrameInfo;

/* PLUGIN-allocated and returned by pointer from GetCapability. Allocate once for
 * the lifetime of the plugin, not per call. Do not fill a host buffer: the host
 * cannot tell you how large its buffer is beforehand.
 *
 * Capability flags are a 2x2 over {static raster, multi-frame} x {decode, encode};
 * implement any subset. A codec that implements none is not registered. */
typedef struct IGCodecCapability {
    int32_t            StructSize; /* = sizeof(IGCodecCapability); must be first */
    IGStringRef        CodecId;
    IGStringRef        CodecName;
    int32_t            MetadataPriority;
    int32_t            DecodePriority;
    int32_t            SupportsMetadata;
    int32_t            SupportsColorProfiles;
    int32_t            SupportsStaticRasterDecoding;
    int32_t            SupportsAnimationDecoding; /* real timeline only; a page
                                                   * container reports FrameCount > 1
                                                   * with this at 0 */
    int32_t            DecodeExtensionCount;
    const IGStringRef* DecodeExtensions; /* lowercase, leading dot; plugin lifetime.
                                          * The only source of readable formats:
                                          * the manifest carries no list. */
    int32_t            SupportsStaticRasterEncoding;
    int32_t            SupportsMultiFrameEncoding;
    int32_t            EncodePriority;
    int32_t            EncodeExtensionCount;
    const IGStringRef* EncodeExtensions; /* must be non-empty when
                                          * SupportsStaticRasterEncoding == 1;
                                          * empty NEVER means "encodes everything" */
} IGCodecCapability;

typedef struct IGPluginInfo {
    IGStringRef PluginId; /* must match the manifest "id" */
    IGStringRef Name;
    IGStringRef Version;
    int32_t     AbiVersion;
    int32_t     CodecCount;
} IGPluginInfo;

/* ===========================================================================
 * Host services (host-allocated, valid for the plugin's lifetime)
 * ========================================================================= */

typedef struct IGHostCoreApi {
    /* levels: 0=trace 1=debug 2=info 3=warn 4=error */
    void    (*Log)(int32_t level, IGStringRef message);
    void*   (*Alloc)(size_t sizeInBytes);
    void    (*Free)(void* ptr);
    /* poll during long work; return 1 means return IGStatus_Canceled */
    int32_t (*IsCancellationRequested)(void* cancellation);
    /* buffer is char16_t and the length counts CODE UNITS, so allocate
     * bufferLengthInCodeUnits * 2 bytes. Returns units written, or the required
     * size when the buffer is too small. Not null-terminated. */
    int32_t (*GetConfigDirectory)(char16_t* buffer, int32_t bufferLengthInCodeUnits);
} IGHostCoreApi;

typedef struct IGHostApi {
    int32_t              StructSize;
    int32_t              AbiVersion;
    const IGHostCoreApi* Core; /* never NULL */
} IGHostApi;

/* ===========================================================================
 * Codec table (PLUGIN-allocated, plugin lifetime)
 *
 * StructSize must be first and must be set: the plugin owns this allocation and
 * the host reads members by offset, so it is the only offset guaranteed stable
 * across future additions and therefore the only member readable before
 * validation.
 *
 * ENCODE CONTRACT. The host passes a destination path it owns: a temp file in the
 * user's chosen folder carrying the real target extension last, e.g.
 * ".ig-save-<guid>.b64". On success the host moves it into place, so a failed
 * encode can never damage the user's original file. Therefore:
 *   - create/overwrite exactly that path and write it completely;
 *   - CLOSE the file handle before returning, on EVERY path including failure, or
 *     the host's move/delete fails with a sharing violation on Windows;
 *   - do not derive the user-visible filename from it; read
 *     IGEncodeOptions.SourceFilePath instead.
 * The source IGPixelBuffer is host-owned and call-scoped. The host normalizes
 * pixels to IGPixelFormat_Bgra8Unorm, UNPREMULTIPLIED, but read PixelFormat and
 * return IGStatus_Unsupported for anything you do not handle.
 * ========================================================================= */

typedef struct IGCodecApi {
    int32_t StructSize; /* = sizeof(IGCodecApi); must be first */

    /* Plugin allocates *outCapability and returns a pointer to it. */
    IGStatus (*GetCapability)(IGCodecCapability** outCapability);

    int32_t  (*CanHandleExtension)(IGStringRef extension);
    /* optional; may be NULL */
    int32_t  (*CanHandleSignature)(const uint8_t* signature, int32_t length);

    IGStatus (*LoadMetadata)(IGStringRef filePath, IGImageInfo* outImageInfo,
                             void* cancellation);

    /* Plugin allocates outBuffer; the host releases it via FreePixelBuffer. */
    IGStatus (*DecodeStaticRaster)(IGStringRef filePath, int32_t frameIndex,
                                   IGPixelBuffer* outBuffer, void* cancellation);

    /* MUST be thread-safe: the host hands decode buffers to Skia, which may
     * release from any thread, including a finalizer thread. DECODE OUTPUT ONLY. */
    void     (*FreePixelBuffer)(IGPixelBuffer* buffer);

    /* --- animation decode: all three non-NULL when
     *     SupportsAnimationDecoding == 1, else the host downgrades the flag --- */

    IGStatus (*GetAnimationInfo)(IGStringRef filePath, IGAnimationInfo* outInfo,
                                 void* cancellation);
    void     (*FreeAnimationInfo)(IGAnimationInfo* info);
    /* Must return a FULLY COMPOSED full-canvas RGBA frame: the host runs no
     * sub-rect composition and no disposal/blend replay. Frames may differ in
     * size (the host re-fits), but each is its own full canvas with no offset. */
    IGStatus (*DecodeAnimationFrame)(IGStringRef filePath, int32_t frameIndex,
                                     IGPixelBuffer* outBuffer, void* cancellation);

    /* --- encode: required when SupportsStaticRasterEncoding == 1 --- */

    IGStatus (*EncodeStaticRaster)(IGStringRef destFilePath,
                                   const IGPixelBuffer* source,
                                   const IGEncodeOptions* options,
                                   void* cancellation);

    /* --- multi-frame encode session: all three non-NULL when
     *     SupportsMultiFrameEncoding == 1.
     *
     * One session at a time per codec, and every call for a session comes from a
     * single thread, so session state needs no locking. On IGStatus_OK from Begin
     * the session must be non-NULL and the host calls End exactly once; on any
     * other status the host does NOT call End, so release everything first. The
     * host holds one frame at a time and cannot re-supply an earlier one, so an
     * encoder needing a second pass must buffer internally. --- */

    IGStatus (*BeginEncodeMultiFrame)(IGStringRef destFilePath,
                                      const IGMultiFrameEncodeInfo* info,
                                      const IGEncodeOptions* options,
                                      void** outSession, void* cancellation);
    IGStatus (*EncodeFrame)(void* session, const IGPixelBuffer* frame,
                            const IGEncodeFrameInfo* frameInfo, void* cancellation);
    /* commit 1 = write the trailer and flush; commit 0 = aborted, stop writing
     * (the host discards the temp file, so do not delete it). MUST close the file
     * handle and free session state on BOTH paths. */
    IGStatus (*EndEncodeMultiFrame)(void* session, int32_t commit,
                                    void* cancellation);

    /* --- optional scaled decode for thumbnails/previews (ABI 1.2.0) ---
     *
     * Requires a host at ABI 1.2.0 or later.
     *
     * May be NULL; the host then decodes full size and downscales itself. The
     * box is an upper bound, not an exact size: return whatever the format
     * decodes to cheaply (JPEG DCT scales, an embedded thumbnail) so long as
     * neither side exceeds it, keep the aspect ratio, and report the real size
     * in outBuffer. A source already inside the box comes back unscaled.
     * Ownership matches DecodeStaticRaster: the host frees via FreePixelBuffer. */
    IGStatus (*DecodeStaticRasterScaled)(IGStringRef filePath, int32_t frameIndex,
                                         int32_t maxWidth, int32_t maxHeight,
                                         IGPixelBuffer* outBuffer,
                                         void* cancellation);
} IGCodecApi;

/* ===========================================================================
 * Plugin table (PLUGIN-allocated, plugin lifetime)
 * ========================================================================= */

typedef struct IGPluginApi {
    int32_t      StructSize; /* = sizeof(IGPluginApi) */
    int32_t      AbiVersion; /* = IG_PLUGIN_ABI_VERSION */
    IGPluginInfo Info;

    /* index in [0, Info.CodecCount); the returned pointer must live as long as
     * the plugin */
    IGStatus (*GetCodec)(int32_t index, IGCodecApi** outCodecApi);

    IGStatus (*Initialize)(void); /* optional; may be NULL */
    void     (*Shutdown)(void);   /* optional; may be NULL */
    IGStatus (*SelfTest)(void);   /* optional; may be NULL */
} IGPluginApi;

/* ===========================================================================
 * The single exported entry point
 *
 * Export it under exactly the name IG_PLUGIN_ENTRY_POINT_NAME. Return NULL to
 * refuse to load, which is what a major-version mismatch must do:
 *
 *     if (hostAbiVersion / 1000000 != IG_PLUGIN_ABI_MAJOR) return NULL;
 *
 * Discovery is not trust: the host loads a plugin only after the user enables it
 * and only while the library still matches the SHA-256 pinned at that moment.
 * ========================================================================= */

const IGPluginApi* ig_plugin_get_api(int32_t hostAbiVersion,
                                     const IGHostApi* hostApi);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* IG_PLUGIN_ABI_H */
