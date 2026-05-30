/*
ImageGlass Base64 Sample Codec Plugin
Copyright (C) 2026 DUONG DIEU PHAP
MIT License

Demonstrates the IGNativeAbi with a minimal, cross-platform codec that reads a
".b64" text file, base64-decodes its contents back into the original image bytes
(PNG / JPEG / WebP / ... — whatever was encoded), and decodes those bytes to
32bpp premultiplied BGRA via SkiaSharp.

A ".b64" file is just a text file holding a base64 string. Two shapes are
accepted:

    1. A raw base64 payload:           iVBORw0KGgoAAAANSUhEUgAA...
    2. A data URI:                     data:image/png;base64,iVBORw0KGgo...

Whitespace/newlines anywhere in the payload are ignored.
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
    // process-lifetime native blocks for the API tables, id strings, and the
    // extension table. They are intentionally never freed.

    private const string PluginIdString = "Plugin_SampleBase64Codec";
    private const string PluginNameString = "Base64 Codec (sample)";
    private const string VersionString = "1.0.0";
    private const string CodecIdString = "plugin.base64.codec";
    private const string CodecNameString = "Base64 Codec";

    private static readonly string[] SupportedExtensions = [".b64"];

    private static IGPluginApi* _pluginApi;
    private static IGCodecApi* _codecApi;
    private static IGHostApi* _hostApi;

    // UTF-16 string buffers (process-lifetime).
    private static char* _bufPluginId;
    private static char* _bufPluginName;
    private static char* _bufVersion;
    private static char* _bufCodecId;
    private static char* _bufCodecName;
    private static char** _bufExtensions; // one char* per entry in SupportedExtensions
    private static IGStringRef* _extArray;

    // Per-decode bookkeeping so FreePixelBuffer can find the malloc to free.
    // Keyed by the pixel pointer.
    private static readonly object _bufLock = new();
    private static readonly System.Collections.Generic.Dictionary<nint, nint> _liveBuffers = new();


    // ------------------------------ Entry point ------------------------------

    [UnmanagedCallersOnly(EntryPoint = IGNativeAbi.ENTRY_POINT_NAME, CallConvs = [typeof(CallConvCdecl)])]
    public static IGPluginApi* GetApi(int hostAbiVersion, IGHostApi* hostApi)
    {
        // Major-version mismatch: refuse to load.
        if (hostAbiVersion / 1_000_000 != IGNativeAbi.IG_PLUGIN_ABI_MAJOR) return null;
        if (hostApi == null) return null;

        if (_pluginApi != null) return _pluginApi;
        _hostApi = hostApi;

        InitStrings();
        InitCodecApi();
        InitPluginApi();
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
    private static IGStatus CodecGetCapability(IGCodecCapability* outCap)
    {
        if (outCap == null) return IGStatus.InvalidArg;
        *outCap = BuildCapability();
        return IGStatus.OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CodecCanHandleExtension(IGStringRef ext)
    {
        if (ext.Data == null || ext.Length <= 0) return 0;
        var s = new ReadOnlySpan<char>(ext.Data, ext.Length);
        foreach (var supported in SupportedExtensions)
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
        return DecodeInternal(filePath, cancellation, outInfo, null);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IGStatus CodecDecodeStaticRaster(IGStringRef filePath, int frameIndex, IGPixelBuffer* outBuf, void* cancellation)
    {
        if (outBuf == null) return IGStatus.InvalidArg;
        *outBuf = default;
        // ".b64" wraps a single still image; only frame 0 is valid.
        if (frameIndex != 0) return IGStatus.InvalidArg;

        IGImageInfo info = default;
        return DecodeInternal(filePath, cancellation, &info, outBuf);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CodecFreePixelBuffer(IGPixelBuffer* buf)
    {
        if (buf == null || buf->Data == null) return;
        nint key = (nint)buf->Data;
        nint pixels;
        lock (_bufLock)
        {
            if (!_liveBuffers.Remove(key, out pixels)) return;
        }
        NativeMemory.Free((void*)pixels);
        buf->Data = null;
        buf->ReleaseContext = null;
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

        // 3) Decode straight into a native, host-owned BGRA8 (premultiplied) buffer.
        ulong stride = (ulong)w * 4UL;
        ulong size = stride * (ulong)h;
        if (size > int.MaxValue) return IGStatus.OutOfMemory;

        var pixels = (byte*)NativeMemory.Alloc((nuint)size);
        if (pixels == null) return IGStatus.OutOfMemory;

        try
        {
            var dstInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
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


    private static IGCodecCapability BuildCapability()
    {
        return new IGCodecCapability
        {
            CodecId = MakeStringRef(_bufCodecId, CodecIdString.Length),
            CodecName = MakeStringRef(_bufCodecName, CodecNameString.Length),
            // No built-in codec claims ".b64", but advertise a high priority so this
            // plugin reliably wins selection for the extension it owns.
            MetadataPriority = 200,
            DecodePriority = 200,
            SupportsMetadata = 1,
            SupportsStaticRaster = 1,
            SupportsColorProfiles = 0,
            SupportsAnimation = 0,
            ExtensionCount = SupportedExtensions.Length,
            Extensions = _extArray,
        };
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

        var count = SupportedExtensions.Length;
        _bufExtensions = (char**)NativeMemory.AllocZeroed((nuint)(sizeof(nint) * count));
        _extArray = (IGStringRef*)NativeMemory.AllocZeroed((nuint)(sizeof(IGStringRef) * count));
        for (var i = 0; i < count; i++)
        {
            var ext = SupportedExtensions[i];
            _bufExtensions[i] = AllocUtf16(ext);
            _extArray[i] = MakeStringRef(_bufExtensions[i], ext.Length);
        }
    }

    private static void InitCodecApi()
    {
        _codecApi = (IGCodecApi*)NativeMemory.AllocZeroed((nuint)sizeof(IGCodecApi));
        _codecApi->GetCapability = &CodecGetCapability;
        _codecApi->CanHandleExtension = &CodecCanHandleExtension;
        _codecApi->CanHandleSignature = null; // base64 text has no reliable magic; match by extension only.
        _codecApi->LoadMetadata = &CodecLoadMetadata;
        _codecApi->DecodeStaticRaster = &CodecDecodeStaticRaster;
        _codecApi->FreePixelBuffer = &CodecFreePixelBuffer;

        // Static-image-only codec: leave the animation entry points null.
        _codecApi->GetAnimationInfo = null;
        _codecApi->FreeAnimationInfo = null;
        _codecApi->DecodeAnimationFrame = null;
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
