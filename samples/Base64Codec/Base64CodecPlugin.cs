/*
ImageGlass Base64 Sample Codec Plugin
Copyright (C) 2026 DUONG DIEU PHAP
MIT License

Demonstrates the IGNativeAbi with a minimal, cross-platform codec that both READS
and WRITES a ".b64" text file.

Decoding: base64-decodes the file contents back into the original image bytes
(PNG / JPEG / WebP / ... - whatever was encoded) and decodes those bytes to 32bpp
BGRA via SkiaSharp.

Encoding: takes the pixels the host hands over, PNG-encodes them with SkiaSharp,
then writes the base64 of that PNG as text. So a decode/encode round trip through
ImageGlass is lossless.

A ".b64" file is just a text file holding a base64 string. Two shapes are accepted
on read; encoding always emits shape 1:

    1. A raw base64 payload:           iVBORw0KGgoAAAANSUhEUgAA...
    2. A data URI:                     data:image/png;base64,iVBORw0KGgo...

Whitespace/newlines anywhere in the payload are ignored.

This codec deliberately implements only 3 of the 4 capability quadrants: static
raster decode, static raster encode, and metadata. It leaves animation decode and
multi-frame encode null to show that each is independently optional.
*/
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImageGlass.SDK.Plugins;
using SkiaSharp;

namespace Base64Codec;

internal static unsafe class Base64CodecPlugin
{
    // ------------------------------ Static buffers ------------------------------
    // Everything the host receives must outlive the call. We pre-allocate
    // process-lifetime native blocks for the API tables, the capability struct, id
    // strings, and the extension tables. They are intentionally never freed.

    private const string PluginIdString = "Plugin_SampleBase64Codec";
    private const string PluginNameString = "Base64 Codec (sample)";
    private const string VersionString = "1.0.0";
    private const string CodecIdString = "plugin.base64.codec";
    private const string CodecNameString = "Base64 Codec";

    // Formats this codec can read, and the (here identical) set it can write.
    // These arrays are the ONLY declaration of the codec's formats: the manifest
    // carries no extension list, and the user narrows the sets in ImageGlass.
    private static readonly string[] DecodeExtensions = [".b64"];
    private static readonly string[] EncodeExtensions = [".b64"];

    private static IGPluginApi* _pluginApi;
    private static IGCodecApi* _codecApi;
    private static IGCodecCapability* _capability;
    private static IGHostApi* _hostApi;

    // UTF-16 string buffers (process-lifetime).
    private static char* _bufPluginId;
    private static char* _bufPluginName;
    private static char* _bufVersion;
    private static char* _bufCodecId;
    private static char* _bufCodecName;
    private static IGStringRef* _decExtArray;
    private static IGStringRef* _encExtArray;

    // Per-decode bookkeeping so FreePixelBuffer can find the malloc to free.
    // Keyed by the pixel pointer.
    private static readonly object _bufLock = new();
    private static readonly System.Collections.Generic.Dictionary<nint, nint> _liveBuffers = new();


    // ------------------------------ Entry point ------------------------------
    //
    // Every [UnmanagedCallersOnly] entry point below is a hard ABI boundary: an escaping managed
    // exception cannot be caught by the host and kills the app, so each one ends in a catch.

    [UnmanagedCallersOnly(EntryPoint = IGNativeAbi.ENTRY_POINT_NAME, CallConvs = [typeof(CallConvCdecl)])]
    public static IGPluginApi* GetApi(int hostAbiVersion, IGHostApi* hostApi)
    {
        // Major-version mismatch: refuse to load.
        if (hostAbiVersion / 1_000_000 != IGNativeAbi.IG_PLUGIN_ABI_MAJOR) return null;
        if (hostApi == null) return null;

        if (_pluginApi != null) return _pluginApi;
        _hostApi = hostApi;

        try
        {
            InitStrings();
            InitCapability();
            InitCodecApi();
            InitPluginApi();
        }
        catch
        {
            // returning null is the ABI's "refused"; the host logs it and skips the plugin
            return null;
        }
        return _pluginApi;
    }


    // ------------------------------ Plugin API callbacks ------------------------------

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IGStatus OnInitialize() => IGStatus.OK;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnShutdown() { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IGStatus OnGetCodec(int index, IGCodecApi** outCodecApi)
    {
        if (outCodecApi == null) return IGStatus.InvalidArg;
        if (index != 0) { *outCodecApi = null; return IGStatus.InvalidArg; }
        *outCodecApi = _codecApi;
        return IGStatus.OK;
    }


    // ------------------------------ Codec API callbacks ------------------------------

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IGStatus CodecGetCapability(IGCodecCapability** outCap)
    {
        if (outCap == null) return IGStatus.InvalidArg;

        // Hand back the plugin-owned struct built once in InitCapability. Never fill
        // a host buffer: the host cannot tell us how large its allocation is.
        *outCap = _capability;
        return IGStatus.OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CodecCanHandleExtension(IGStringRef ext)
    {
        if (ext.Data == null || ext.Length <= 0) return 0;
        var s = new ReadOnlySpan<char>(ext.Data, ext.Length);
        foreach (var supported in DecodeExtensions)
        {
            if (s.Equals(supported, StringComparison.OrdinalIgnoreCase)) return 1;
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IGStatus CodecLoadMetadata(IGStringRef filePath, IGImageInfo* outInfo, void* cancellation)
    {
        if (outInfo == null) return IGStatus.InvalidArg;
        *outInfo = default;

        try
        {
            return DecodeInternal(filePath, cancellation, outInfo, null);
        }
        catch (Exception ex)
        {
            // e.g. a missing libSkiaSharp surfaces here as a type-initializer failure
            Log(4, $"Base64Codec: LoadMetadata failed. {ex}");
            return IGStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IGStatus CodecDecodeStaticRaster(IGStringRef filePath, int frameIndex, IGPixelBuffer* outBuf, void* cancellation)
    {
        if (outBuf == null) return IGStatus.InvalidArg;
        *outBuf = default;
        // ".b64" wraps a single still image; only frame 0 is valid.
        if (frameIndex != 0) return IGStatus.InvalidArg;

        IGImageInfo info = default;

        try
        {
            return DecodeInternal(filePath, cancellation, &info, outBuf);
        }
        catch (Exception ex)
        {
            Log(4, $"Base64Codec: DecodeStaticRaster failed. {ex}");
            return IGStatus.Internal;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CodecFreePixelBuffer(IGPixelBuffer* buf)
    {
        if (buf == null || buf->Data == null) return;

        // The host may call this from any thread, including the GC finalizer thread, where an
        // escaping exception is fatal and there is nobody to report it to.
        try
        {
            nint key = (nint)buf->Data;
            nint pixels;
            lock (_bufLock)
            {
                // Not one of ours: either already freed, or this is an ENCODE input
                // buffer the host owns. Never free memory we did not allocate.
                if (!_liveBuffers.Remove(key, out pixels)) return;
            }
            NativeMemory.Free((void*)pixels);
            buf->Data = null;
            buf->ReleaseContext = null;
        }
        catch { }
    }


    // ------------------------------ Encode ------------------------------
    //
    // destFilePath is a host-owned temp file carrying the real target extension;
    // the host moves it into place on success. So: write it completely, and close
    // the handle before returning on every path (File.WriteAllText does both).
    // The source buffer belongs to the HOST and dies when this call returns.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IGStatus CodecEncodeStaticRaster(IGStringRef destFilePath, IGPixelBuffer* source,
        IGEncodeOptions* options, void* cancellation)
    {
        if (destFilePath.Data == null || destFilePath.Length <= 0) return IGStatus.InvalidArg;
        if (source == null || source->Data == null) return IGStatus.InvalidArg;
        if (source->Width <= 0 || source->Height <= 0) return IGStatus.InvalidArg;

        // The host normalizes to BGRA8 unpremultiplied, but check rather than assume:
        // a future host may hand over a wider format.
        if (source->PixelFormat != (int)IGPixelFormat.Bgra8Unorm) return IGStatus.Unsupported;

        var destPath = string.Empty;

        try
        {
            destPath = new string(destFilePath.Data, 0, destFilePath.Length);
            var quality = ReadQuality(options);

            if (IsCanceled(cancellation)) return IGStatus.Canceled;

            // 1) Wrap the host pixels and PNG-encode them. FromPixelCopy copies, so
            //    nothing outlives the call. (PNG ignores quality; it is passed for
            //    illustration of how options reach the encoder.)
            var info = new SKImageInfo(source->Width, source->Height,
                SKColorType.Bgra8888, SKAlphaType.Unpremul);

            using var image = SKImage.FromPixelCopy(info, (nint)source->Data, source->Stride);
            if (image is null)
            {
                Log(4, "Base64Codec: could not wrap the source pixels.");
                return IGStatus.EncodeFailed;
            }

            if (IsCanceled(cancellation)) return IGStatus.Canceled;

            using var png = image.Encode(SKEncodedImageFormat.Png, quality);
            if (png is null || png.Size == 0)
            {
                Log(4, "Base64Codec: PNG encoding produced no data.");
                return IGStatus.EncodeFailed;
            }

            if (IsCanceled(cancellation)) return IGStatus.Canceled;

            // 2) Base64 the PNG bytes and write the text file.
            var base64 = Convert.ToBase64String(png.AsSpan());
            File.WriteAllText(destPath, base64);

            return IGStatus.OK;
        }
        catch (IOException ex)
        {
            Log(4, $"Base64Codec: failed to write '{destPath}' ({ex.Message}).");
            return IGStatus.IoError;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log(4, $"Base64Codec: access denied writing '{destPath}' ({ex.Message}).");
            return IGStatus.IoError;
        }
        catch (Exception ex)
        {
            Log(4, $"Base64Codec: unexpected encode failure. {ex}");
            return IGStatus.Internal;
        }
    }


    /// <summary>
    /// Reads <c>Quality</c> from the host's options, honoring <c>StructSize</c>: a host
    /// built against a smaller struct may not carry the field at all, so never read
    /// past what it reports.
    /// </summary>
    private static int ReadQuality(IGEncodeOptions* options)
    {
        // offsetof(Quality) + sizeof(int): the smallest StructSize that includes Quality.
        const int QUALITY_END = sizeof(int) * 2;

        if (options == null || options->StructSize < QUALITY_END) return 100;
        return Math.Clamp(options->Quality, 1, 100);
    }


    // ------------------------------ Core decode pipeline ------------------------------
    //
    // If outBuf is null we only fill outInfo (metadata-only path) and skip the
    // pixel allocation. Cancellation is honored at coarse boundaries.

    private static IGStatus DecodeInternal(IGStringRef filePath, void* cancellation, IGImageInfo* outInfo, IGPixelBuffer* outBuf)
    {
        if (filePath.Data == null || filePath.Length <= 0) return IGStatus.InvalidArg;

        var managedPath = new string(filePath.Data, 0, filePath.Length);

        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        // 1) Read the text file and turn the base64 payload back into image bytes.
        byte[] imageBytes;
        try
        {
            var text = File.ReadAllText(managedPath);
            imageBytes = DecodeBase64Payload(text);
        }
        catch (FormatException)
        {
            Log(4, $"Base64Codec: '{Path.GetFileName(managedPath)}' is not valid base64.");
            return IGStatus.DecodeFailed;
        }
        catch (IOException ex)
        {
            Log(4, $"Base64Codec: failed to read '{managedPath}' ({ex.Message}).");
            return IGStatus.IoError;
        }
        catch
        {
            return IGStatus.Internal;
        }

        if (imageBytes.Length == 0) return IGStatus.DecodeFailed;
        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        // 2) Decode the embedded image bytes via SkiaSharp.
        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data);
        if (codec == null)
        {
            Log(4, $"Base64Codec: embedded payload of '{Path.GetFileName(managedPath)}' is not a recognized image format.");
            return IGStatus.DecodeFailed;
        }

        var srcInfo = codec.Info;
        int w = srcInfo.Width, h = srcInfo.Height;
        if (w <= 0 || h <= 0) return IGStatus.DecodeFailed;

        outInfo->Width = w;
        outInfo->Height = h;
        outInfo->PixelFormat = (int)IGPixelFormat.Bgra8Unorm;
        outInfo->HasAlpha = srcInfo.AlphaType == SKAlphaType.Opaque ? 0 : 1;
        outInfo->HdrTransferFn = (int)IGHdrTransferFn.None;
        outInfo->ColorSpace = (int)IGColorSpace.Srgb;
        outInfo->Orientation = 1;
        outInfo->FrameCount = 1;
        outInfo->FileSizeBytes = -1;
        outInfo->IccProfileData = null;
        outInfo->IccProfileSize = 0;

        // Metadata-only path: done.
        if (outBuf == null) return IGStatus.OK;

        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        // 3) Decode straight into a native, plugin-owned BGRA8 buffer.
        ulong stride = (ulong)w * 4UL;
        ulong size = stride * (ulong)h;
        if (size > int.MaxValue) return IGStatus.OutOfMemory;

        var pixels = (byte*)NativeMemory.Alloc((nuint)size);
        if (pixels == null) return IGStatus.OutOfMemory;

        try
        {
            // Unpremul, matching what IGPixelFormat.Bgra8Unorm means to the host.
            var dstInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var result = codec.GetPixels(dstInfo, (nint)pixels);

            // IncompleteInput still yields a usable (partially-decoded) image.
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                NativeMemory.Free(pixels);
                Log(4, $"Base64Codec: SKCodec.GetPixels failed ({result}).");
                return IGStatus.DecodeFailed;
            }
        }
        catch
        {
            NativeMemory.Free(pixels);
            return IGStatus.Internal;
        }

        outBuf->Data = pixels;
        outBuf->Width = w;
        outBuf->Height = h;
        outBuf->Stride = (int)stride;
        outBuf->PixelFormat = (int)IGPixelFormat.Bgra8Unorm;
        outBuf->ReleaseContext = pixels;

        lock (_bufLock)
        {
            _liveBuffers[(nint)pixels] = (nint)pixels;
        }
        return IGStatus.OK;
    }


    // ------------------------------ Helpers ------------------------------

    /// <summary>
    /// Extracts and decodes the base64 payload from a ".b64" file's text. Strips an
    /// optional <c>data:[mime];base64,</c> URI prefix; <see cref="Convert.FromBase64String"/>
    /// then ignores the embedded whitespace/newlines. Throws <see cref="FormatException"/>
    /// when the remaining text is not valid base64.
    /// </summary>
    private static byte[] DecodeBase64Payload(string text)
    {
        var payload = text.AsSpan().Trim();

        // Strip a "data:...;base64," prefix if present (everything up to and including the comma).
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = payload.IndexOf(',');
            if (comma < 0) throw new FormatException("Malformed data URI: missing ','.");
            payload = payload[(comma + 1)..].Trim();
        }

        return Convert.FromBase64String(payload.ToString());
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCanceled(void* cancellation)
    {
        if (cancellation == null || _hostApi == null || _hostApi->Core == null) return false;
        var fn = _hostApi->Core->IsCancellationRequested;
        if (fn == null) return false;
        return fn(cancellation) != 0;
    }

    /// <summary>
    /// Sends a UTF-16 message to the host's plugin log channel.
    /// Levels: 0=trace, 1=debug, 2=info, 3=warn, 4=error.
    /// </summary>
    private static void Log(int level, string message)
    {
        if (_hostApi == null || _hostApi->Core == null) return;
        var fn = _hostApi->Core->Log;
        if (fn == null) return;
        fixed (char* pMsg = message)
        {
            fn(level, new IGStringRef { Data = pMsg, Length = message.Length });
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IGStringRef MakeStringRef(char* data, int len) => new() { Data = data, Length = len };

    private static void InitStrings()
    {
        _bufPluginId = AllocUtf16(PluginIdString);
        _bufPluginName = AllocUtf16(PluginNameString);
        _bufVersion = AllocUtf16(VersionString);
        _bufCodecId = AllocUtf16(CodecIdString);
        _bufCodecName = AllocUtf16(CodecNameString);

        _decExtArray = AllocExtensionArray(DecodeExtensions);
        _encExtArray = AllocExtensionArray(EncodeExtensions);
    }

    /// <summary>
    /// Allocates a process-lifetime <see cref="IGStringRef"/> array for the given extensions.
    /// The host keeps reading this array for as long as the plugin is loaded.
    /// </summary>
    private static IGStringRef* AllocExtensionArray(string[] extensions)
    {
        var array = (IGStringRef*)NativeMemory.AllocZeroed((nuint)(sizeof(IGStringRef) * extensions.Length));
        for (var i = 0; i < extensions.Length; i++)
        {
            array[i] = MakeStringRef(AllocUtf16(extensions[i]), extensions[i].Length);
        }
        return array;
    }

    private static void InitCapability()
    {
        _capability = (IGCodecCapability*)NativeMemory.AllocZeroed((nuint)sizeof(IGCodecCapability));
        _capability->StructSize = sizeof(IGCodecCapability);
        _capability->CodecId = MakeStringRef(_bufCodecId, CodecIdString.Length);
        _capability->CodecName = MakeStringRef(_bufCodecName, CodecNameString.Length);

        // No built-in codec claims ".b64", but advertise a high priority so this
        // plugin reliably wins selection for the extension it owns.
        _capability->MetadataPriority = 200;
        _capability->DecodePriority = 200;
        _capability->EncodePriority = 200;

        _capability->SupportsMetadata = 1;
        _capability->SupportsColorProfiles = 0;
        _capability->SupportsStaticRasterDecoding = 1;
        _capability->SupportsAnimationDecoding = 0;
        _capability->SupportsStaticRasterEncoding = 1;
        _capability->SupportsMultiFrameEncoding = 0;

        _capability->DecodeExtensionCount = DecodeExtensions.Length;
        _capability->DecodeExtensions = _decExtArray;
        _capability->EncodeExtensionCount = EncodeExtensions.Length;
        _capability->EncodeExtensions = _encExtArray;
    }

    private static void InitCodecApi()
    {
        _codecApi = (IGCodecApi*)NativeMemory.AllocZeroed((nuint)sizeof(IGCodecApi));
        _codecApi->StructSize = sizeof(IGCodecApi);
        _codecApi->GetCapability = &CodecGetCapability;
        _codecApi->CanHandleExtension = &CodecCanHandleExtension;
        _codecApi->CanHandleSignature = null; // base64 text has no reliable magic; match by extension only.
        _codecApi->LoadMetadata = &CodecLoadMetadata;
        _codecApi->DecodeStaticRaster = &CodecDecodeStaticRaster;
        _codecApi->FreePixelBuffer = &CodecFreePixelBuffer;

        // Static-image-only codec: leave the animation decode entry points null.
        _codecApi->GetAnimationInfo = null;
        _codecApi->FreeAnimationInfo = null;
        _codecApi->DecodeAnimationFrame = null;

        // Single-frame encoding only; no multi-frame session.
        _codecApi->EncodeStaticRaster = &CodecEncodeStaticRaster;
        _codecApi->BeginEncodeMultiFrame = null;
        _codecApi->EncodeFrame = null;
        _codecApi->EndEncodeMultiFrame = null;
    }

    private static void InitPluginApi()
    {
        _pluginApi = (IGPluginApi*)NativeMemory.AllocZeroed((nuint)sizeof(IGPluginApi));
        _pluginApi->StructSize = sizeof(IGPluginApi);
        _pluginApi->AbiVersion = IGNativeAbi.IG_PLUGIN_ABI_VERSION;
        _pluginApi->Info = new IGPluginInfo
        {
            PluginId = MakeStringRef(_bufPluginId, PluginIdString.Length),
            Name = MakeStringRef(_bufPluginName, PluginNameString.Length),
            Version = MakeStringRef(_bufVersion, VersionString.Length),
            AbiVersion = IGNativeAbi.IG_PLUGIN_ABI_VERSION,
            CodecCount = 1,
        };
        _pluginApi->GetCodec = &OnGetCodec;
        _pluginApi->Initialize = &OnInitialize;
        _pluginApi->Shutdown = &OnShutdown;
        _pluginApi->SelfTest = null;
    }

    private static char* AllocUtf16(string s)
    {
        var buf = (char*)NativeMemory.Alloc((nuint)((s.Length + 1) * sizeof(char)));
        for (var i = 0; i < s.Length; i++) buf[i] = s[i];
        buf[s.Length] = '\0';
        return buf;
    }
}
