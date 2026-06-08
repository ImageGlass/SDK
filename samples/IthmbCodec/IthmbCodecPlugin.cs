/*
ImageGlass ITHMB Codec Plugin
Copyright (C) 2026 B67687
MIT License

Reads Apple .ithmb thumbnail-cache files. Primary path: locate an embedded
JPEG payload (JFIF/Exif markers) and decode it via SkiaSharp. Secondary
path: decode known legacy raw thumbnail profiles (RGB565, YUV422, YCbCr420).

Format behavior informed by the IthmbDecoder reference (ImageGlass PR #2316).
This is a clean-room implementation for the v10 native codec plugin ABI.
*/
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImageGlass.SDK.Plugins;
using SkiaSharp;

namespace IthmbCodec;

internal static unsafe class IthmbCodecPlugin
{
    // ------------------------------ Constants ------------------------------
    private const string PluginIdString = "Plugin_IthmbCodec";
    private const string PluginNameString = "ITHMB Codec";
    private const string VersionString = "1.0.0";
    private const string CodecIdString = "plugin.ithmb.codec";
    private const string CodecNameString = "Apple ITHMB Thumbnail Cache";

    private static readonly string[] SupportedExtensions = [".ithmb"];

    // JPEG markers for embedded payload detection
    private static readonly byte[] JfifMarker = "JFIF\0"u8.ToArray();
    private static readonly byte[] ExifMarker = "Exif\0\0"u8.ToArray();

    // ------------------------------ Raw profile enums ------------------------------
    private enum IthmbEncoding { Rgb565, Yuv422, Ycbcr420 }

    private sealed record IthmbVariantProfile(
        int Prefix, int Width, int Height, IthmbEncoding Encoding,
        int FrameByteLength,
        bool SwapsDimensions = false, bool LittleEndian = true);

    private static readonly IReadOnlyDictionary<int, IthmbVariantProfile> KnownProfiles =
        new Dictionary<int, IthmbVariantProfile>
        {
            [1007] = new(1007, 480, 864, IthmbEncoding.Rgb565, 480 * 864 * 2),
            [1009] = new(1009, 42, 30, IthmbEncoding.Rgb565, 42 * 30 * 2),
            [1015] = new(1015, 130, 88, IthmbEncoding.Rgb565, 130 * 88 * 2),
            [1019] = new(1019, 720, 480, IthmbEncoding.Yuv422, 720 * 480 * 2),
            [1020] = new(1020, 176, 220, IthmbEncoding.Rgb565, 176 * 220 * 2, SwapsDimensions: true),
            [1023] = new(1023, 176, 132, IthmbEncoding.Rgb565, 176 * 132 * 2),
        };

    // ------------------------------ Static plugin state ------------------------------
    private static IGPluginApi* _pluginApi;
    private static IGCodecApi* _codecApi;
    private static IGHostApi* _hostApi;

    private static char* _bufPluginId, _bufPluginName, _bufVersion;
    private static char* _bufCodecId, _bufCodecName;
    private static char** _bufExtensions;
    private static IGStringRef* _extArray;

    private static readonly object _bufLock = new();
    private static readonly object _initLock = new();
    private static readonly HashSet<nint> _liveBuffers = new();

    // ------------------------------ Entry point ------------------------------
    [UnmanagedCallersOnly(EntryPoint = IGNativeAbi.ENTRY_POINT_NAME, CallConvs = [typeof(CallConvCdecl)])]
    public static IGPluginApi* GetApi(int hostAbiVersion, IGHostApi* hostApi)
    {
        if (hostAbiVersion / 1_000_000 != IGNativeAbi.IG_PLUGIN_ABI_MAJOR) return null;
        if (hostApi == null) return null;
        if (_pluginApi != null) return _pluginApi;
        lock (_initLock)
        {
            if (_pluginApi != null) return _pluginApi;
            _hostApi = hostApi;
            InitStrings();
            InitCodecApi();
            InitPluginApi();
            return _pluginApi;
        }
    }

    // ------------------------------ Plugin API ------------------------------
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

    // ------------------------------ Codec API ------------------------------
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IGStatus CodecGetCapability(IGCodecCapability* outCap)
    {
        if (outCap == null) return IGStatus.InvalidArg;
        *outCap = new IGCodecCapability
        {
            CodecId = MakeStringRef(_bufCodecId, CodecIdString.Length),
            CodecName = MakeStringRef(_bufCodecName, CodecNameString.Length),
            MetadataPriority = 90,
            DecodePriority = 90,
            SupportsMetadata = 1,
            SupportsStaticRaster = 1,
            SupportsColorProfiles = 0,
            SupportsAnimation = 0,
            ExtensionCount = SupportedExtensions.Length,
            Extensions = _extArray,
        };
        return IGStatus.OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CodecCanHandleExtension(IGStringRef ext)
    {
        if (ext.Data == null || ext.Length <= 0) return 0;
        var s = new ReadOnlySpan<char>(ext.Data, ext.Length);
        foreach (var supported in SupportedExtensions)
            if (s.Equals(supported, StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CodecCanHandleSignature(byte* signature, int length)
    {
        if (signature == null || length < 8) return 0;
        var span = new ReadOnlySpan<byte>(signature, length);
        int scanLen = Math.Min(length, 256);
        for (int i = 0; i < scanLen - 4; i++)
        {
            if (span[i] != 0xFF || span[i + 1] != 0xD8) continue;
            int sliceStart = i + 2;
            int sliceLen = Math.Min(i + 128, scanLen) - sliceStart;
            var probe = span.Slice(sliceStart, sliceLen);
            if (probe.IndexOf(JfifMarker) >= 0) return 1;
            if (probe.IndexOf(ExifMarker) >= 0) return 1;
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
    private static IGStatus CodecDecodeStaticRaster(IGStringRef filePath, int frameIndex,
        IGPixelBuffer* outBuf, void* cancellation)
    {
        if (outBuf == null) return IGStatus.InvalidArg;
        *outBuf = default;
        if (frameIndex != 0) return IGStatus.InvalidArg; // single-frame
        IGImageInfo info = default;
        return DecodeInternal(filePath, cancellation, &info, outBuf);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CodecFreePixelBuffer(IGPixelBuffer* buf)
    {
        if (buf == null || buf->Data == null) return;
        nint key = (nint)buf->Data;
        lock (_bufLock) { if (!_liveBuffers.Remove(key)) return; }
        NativeMemory.Free((void*)key);
        buf->Data = null;
        buf->ReleaseContext = null;
    }

    // ------------------------------ Core decode pipeline ------------------------------
    private static IGStatus DecodeInternal(IGStringRef filePath, void* cancellation,
        IGImageInfo* outInfo, IGPixelBuffer* outBuf)
    {
        if (filePath.Data == null || filePath.Length <= 0) return IGStatus.InvalidArg;
        var path = new string(filePath.Data, 0, filePath.Length);
        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        // Read the file
        byte[] fileBytes;
        try { fileBytes = File.ReadAllBytes(path); }
        catch (IOException ex) { Log(4, $"ITHMB: read failed '{path}' ({ex.Message})"); return IGStatus.IoError; }
        catch { return IGStatus.Internal; }
        if (fileBytes.Length < 8) return IGStatus.DecodeFailed;

        // Try embedded JPEG path first
        if (TryFindJpegSlice(fileBytes, out var jpegOffset, out var jpegLength, cancellation))
        {
            return DecodeJpegSlice(fileBytes, jpegOffset, jpegLength, cancellation, outInfo, outBuf);
        }

        // Fallback: try known raw profiles via prefix header
        int prefix = ReadInt32BigEndian(fileBytes, 0);
        if (KnownProfiles.TryGetValue(prefix, out var profile))
        {
            return DecodeRawProfile(fileBytes, profile, cancellation, outInfo, outBuf);
        }

        Log(4, $"ITHMB: '{Path.GetFileName(path)}' no embedded JPEG found, unknown profile prefix {prefix}");
        return IGStatus.DecodeFailed;
    }

    // ------------------------------ JPEG extraction ------------------------------
    private static bool TryFindJpegSlice(byte[] data, out int offset, out int length, void* cancellation)
    {
        offset = 0; length = 0;
        // Scan for JPEG SOI marker (FF D8) with JFIF or Exif payload nearby
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (data[i] != 0xFF || data[i + 1] != 0xD8) continue;
            // Search for JFIF or Exif ASCII payload within 128 bytes of SOI
            int scanEnd = Math.Min(i + 128, data.Length);
            int jfifOff = IndexOf(data, JfifMarker, i, scanEnd);
            int exifOff = IndexOf(data, ExifMarker, i, scanEnd);
            if (jfifOff < 0 && exifOff < 0) continue;
            offset = i;
            // Find JPEG EOI marker (FF D9), checking cancellation every 64KB
            for (int k = i + 2; k < data.Length - 1; k++)
            {
                if ((k & 0xFFFF) == 0 && IsCanceled(cancellation)) return false;
                if (data[k] == 0xFF && data[k + 1] == 0xD9)
                {
                    length = (k + 2) - i;
                    return true;
                }
            }
            // If no EOI found, use rest of file
            length = data.Length - i;
            return true;
        }
        return false;
    }

    private static IGStatus DecodeJpegSlice(byte[] data, int offset, int length,
        void* cancellation, IGImageInfo* outInfo, IGPixelBuffer* outBuf)
    {
        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        if (offset < 0 || length <= 0 || offset + length > data.Length)
            return IGStatus.DecodeFailed;

        byte[] jpegBytes;
        if (offset == 0 && length == data.Length)
            jpegBytes = data;
        else
            jpegBytes = data[offset..(offset + length)];

        using var skData = SKData.CreateCopy(jpegBytes);
        using var codec = SKCodec.Create(skData);
        if (codec == null) { Log(4, "ITHMB: JPEG slice is not a valid image"); return IGStatus.DecodeFailed; }

        var srcInfo = codec.Info;
        int w = srcInfo.Width, h = srcInfo.Height;
        if (w <= 0 || h <= 0) return IGStatus.DecodeFailed;

        int hasAlpha = srcInfo.AlphaType == SKAlphaType.Opaque ? 0 : 1;
        FillImageInfo(outInfo, w, h, hasAlpha, ReadExifOrientation(data, offset, length));

        if (outBuf == null) return IGStatus.OK; // metadata-only
        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        return DecodeToPixelBuffer(codec, w, h, outBuf);
    }

    // ------------------------------ Raw profile decoding ------------------------------
    private static IGStatus DecodeRawProfile(byte[] data, IthmbVariantProfile profile,
        void* cancellation, IGImageInfo* outInfo, IGPixelBuffer* outBuf)
    {
        int w = profile.Width, h = profile.Height;
        if (profile.SwapsDimensions) (w, h) = (h, w);
        int frameSize = profile.FrameByteLength;

        if (data.Length < 4 + frameSize) { Log(4, "ITHMB: raw file too small for profile"); return IGStatus.DecodeFailed; }

        FillImageInfo(outInfo, w, h, hasAlpha: 0, orientation: 1);

        if (outBuf == null) return IGStatus.OK;
        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        var allocStatus = AllocateBgraBuffer(w, h, out var stride, out var pixels);
        if (allocStatus != IGStatus.OK) return allocStatus;

        var raw = data.AsSpan(4);
        switch (profile.Encoding)
        {
            case IthmbEncoding.Rgb565:
                DecodeRgb565(raw, pixels, w, h, profile.LittleEndian);
                break;
            case IthmbEncoding.Yuv422:
                DecodeYuv422(raw, pixels, w, h);
                break;
            case IthmbEncoding.Ycbcr420:
                DecodeYcbcr420(raw, pixels, w, h);
                break;
        }

        outBuf->Data = pixels;
        outBuf->Width = w;
        outBuf->Height = h;
        outBuf->Stride = (int)stride;
        outBuf->PixelFormat = (int)IGPixelFormat.Bgra8Unorm;
        outBuf->ReleaseContext = pixels;
        lock (_bufLock) { _liveBuffers.Add((nint)pixels); }
        return IGStatus.OK;
    }

    private static void DecodeRgb565(ReadOnlySpan<byte> src, byte* dst, int w, int h, bool littleEndian)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 2;
                ushort rgb = littleEndian
                    ? (ushort)(src[idx] | (src[idx + 1] << 8))
                    : (ushort)((src[idx] << 8) | src[idx + 1]);
                int r = ((rgb >> 11) & 0x1F) * 255 / 31;
                int g = ((rgb >> 5) & 0x3F) * 255 / 63;
                int b = (rgb & 0x1F) * 255 / 31;
                int dstIdx = (y * w + x) * 4;
                dst[dstIdx] = (byte)b;
                dst[dstIdx + 1] = (byte)g;
                dst[dstIdx + 2] = (byte)r;
                dst[dstIdx + 3] = 255;
            }
        }
    }

    private static void DecodeYuv422(ReadOnlySpan<byte> src, byte* dst, int w, int h)
    {
        // UYVY interleaved: every 4 bytes = U0 Y0 V0 Y1
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x += 2)
            {
                int idx = (y * w + x) * 2;
                int u = src[idx] - 128;
                int y0 = src[idx + 1];
                int v = src[idx + 2] - 128;
                int y1 = src[idx + 3];

                WriteYuvPixel(dst, y, x, w, y0, u, v);
                WriteYuvPixel(dst, y, x + 1, w, y1, u, v);
            }
        }
    }

    private static void DecodeYcbcr420(ReadOnlySpan<byte> src, byte* dst, int w, int h)
    {
        int ySize = w * h;
        int uvSize = ySize / 4;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int yy = src[y * w + x];
                int uvIdx = ySize + (y / 2) * (w / 2) + (x / 2);
                int cb = src[uvIdx] - 128;
                int cr = src[uvIdx + uvSize] - 128;
                WriteYuvPixel(dst, y, x, w, yy, cb, cr);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteYuvPixel(byte* dst, int y, int x, int w, int luma, int cb, int cr)
    {
        int r = Clamp(luma + ((359 * cr) >> 8));
        int g = Clamp(luma - ((88 * cb) >> 8) - ((183 * cr) >> 8));
        int b = Clamp(luma + ((454 * cb) >> 8));
        int idx = (y * w + x) * 4;
        dst[idx] = (byte)b; dst[idx + 1] = (byte)g;
        dst[idx + 2] = (byte)r; dst[idx + 3] = 255;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    // ------------------------------ Shared SkiaSharp decode ------------------------------
    private static IGStatus DecodeToPixelBuffer(SKCodec codec, int w, int h, IGPixelBuffer* outBuf)
    {
        var allocStatus = AllocateBgraBuffer(w, h, out var stride, out var pixels);
        if (allocStatus != IGStatus.OK) return allocStatus;

        try
        {
            var dstInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            var result = codec.GetPixels(dstInfo, (nint)pixels);
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                NativeMemory.Free(pixels);
                Log(4, $"ITHMB: SKCodec.GetPixels failed ({result})");
                return IGStatus.DecodeFailed;
            }
        }
        catch
        {
            NativeMemory.Free(pixels);
            return IGStatus.Internal;
        }

        outBuf->Data = pixels;
        outBuf->Width = w; outBuf->Height = h;
        outBuf->Stride = (int)stride;
        outBuf->PixelFormat = (int)IGPixelFormat.Bgra8Unorm;
        outBuf->ReleaseContext = pixels;
        lock (_bufLock) { _liveBuffers.Add((nint)pixels); }
        return IGStatus.OK;
    }

    // ------------------------------ Helpers ------------------------------

    /// <summary>Populates an IGImageInfo with common defaults.</summary>
    private static void FillImageInfo(IGImageInfo* info, int w, int h, int hasAlpha, int orientation)
    {
        info->Width = w;
        info->Height = h;
        info->PixelFormat = (int)IGPixelFormat.Bgra8Unorm;
        info->HasAlpha = hasAlpha;
        info->HdrTransferFn = (int)IGHdrTransferFn.None;
        info->ColorSpace = (int)IGColorSpace.Srgb;
        info->Orientation = orientation;
        info->FrameCount = 1;
        info->FileSizeBytes = -1;
        info->IccProfileData = null;
        info->IccProfileSize = 0;
    }

    /// <summary>Allocates a BGRA8 pixel buffer; returns OOM status on failure.</summary>
    private static IGStatus AllocateBgraBuffer(int w, int h, out ulong stride, out byte* pixels)
    {
        stride = (ulong)w * 4UL;
        ulong size = stride * (ulong)h;
        if (size > int.MaxValue) { pixels = null; return IGStatus.OutOfMemory; }
        pixels = (byte*)NativeMemory.Alloc((nuint)size);
        if (pixels == null) return IGStatus.OutOfMemory;
        return IGStatus.OK;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadU16LE(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadU16BE(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadU32LE(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) |
               (data[offset + 2] << 16) | (data[offset + 3] << 24));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadU32BE(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
               (data[offset + 2] << 8) | data[offset + 3]);

    private static int ReadInt32BigEndian(byte[] data, int offset) =>
        (int)ReadU32BE(data, offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexOf(byte[] haystack, byte[] needle, int start, int end)
    {
        for (int i = start; i <= end - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        return -1;
    }

    /// <summary>
    /// Reads the EXIF Orientation tag (0x0112) from a JPEG slice.
    /// Returns 1-8 on success, or 1 (normal) if not found.
    /// </summary>
    private static int ReadExifOrientation(byte[] data, int jpegOffset, int jpegLength)
    {
        int end = jpegOffset + jpegLength;
        for (int i = jpegOffset + 2; i < end - 4; i++)
        {
            if (data[i] != 0xFF || data[i + 1] != 0xE1) continue;
            // APP1 segment: FF E1 len_len (big-endian 16-bit length including self)
            if (i + 4 >= end) break;
            int segEnd = i + 2 + ReadU16BE(data, i + 2);
            if (segEnd > end) break;
            // Look for "Exif\0\0" header within APP1
            int exifOff = i + 4;
            if (exifOff + 6 > end) break;
            if (data[exifOff] != 'E' || data[exifOff + 1] != 'x' ||
                data[exifOff + 2] != 'i' || data[exifOff + 3] != 'f' ||
                data[exifOff + 4] != 0 || data[exifOff + 5] != 0) continue;
            // TIFF header: "II" (little-endian) or "MM" (big-endian)
            int tiffStart = exifOff + 6;
            if (tiffStart + 8 > end) break;
            bool le = data[tiffStart] == 'I' && data[tiffStart + 1] == 'I';
            bool be = data[tiffStart] == 'M' && data[tiffStart + 1] == 'M';
            if (!le && !be) continue;
            // TIFF magic: 0x002A
            if ((le ? ReadU16LE(data, tiffStart + 2) : ReadU16BE(data, tiffStart + 2)) != 0x002A) continue;
            // IFD0 offset
            int ifdOff = tiffStart + 4;
            int ifdPos = tiffStart + (int)(le ? ReadU32LE(data, ifdOff) : ReadU32BE(data, ifdOff));
            if (ifdPos <= tiffStart || ifdPos + 2 > end) continue;
            // Number of IFD entries (16-bit)
            int numEntries = le ? ReadU16LE(data, ifdPos) : ReadU16BE(data, ifdPos);
            // Scan IFD for Orientation tag (0x0112)
            int entryStart = ifdPos + 2;
            for (int e = 0; e < numEntries && entryStart + 12 <= end; e++, entryStart += 12)
            {
                int tag = le ? ReadU16LE(data, entryStart) : ReadU16BE(data, entryStart);
                if (tag != 0x0112) continue;
                // Type must be SHORT (3), count 1
                int type = le ? ReadU16LE(data, entryStart + 2) : ReadU16BE(data, entryStart + 2);
                int count = (int)(le ? ReadU32LE(data, entryStart + 4) : ReadU32BE(data, entryStart + 4));
                if (type != 3 || count != 1) continue;
                // Orientation value is in the last 2 bytes (SHORT fits in 2 bytes)
                int orient = le ? ReadU16LE(data, entryStart + 8) : ReadU16BE(data, entryStart + 8);
                return orient is >= 1 and <= 8 ? orient : 1;
            }
            return 1;
        }
        return 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCanceled(void* cancellation)
    {
        if (cancellation == null || _hostApi == null || _hostApi->Core == null) return false;
        var fn = _hostApi->Core->IsCancellationRequested;
        return fn != null && fn(cancellation) != 0;
    }

    private static void Log(int level, string message)
    {
        if (_hostApi == null || _hostApi->Core == null) return;
        var fn = _hostApi->Core->Log;
        if (fn == null) return;
        fixed (char* p = message) fn(level, new IGStringRef { Data = p, Length = message.Length });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IGStringRef MakeStringRef(char* data, int len) => new() { Data = data, Length = len };

    // ------------------------------ Init ------------------------------
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
        _codecApi->CanHandleSignature = &CodecCanHandleSignature;
        _codecApi->LoadMetadata = &CodecLoadMetadata;
        _codecApi->DecodeStaticRaster = &CodecDecodeStaticRaster;
        _codecApi->FreePixelBuffer = &CodecFreePixelBuffer;
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
