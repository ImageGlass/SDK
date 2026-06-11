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
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImageGlass.SDK.Plugins;
using StbImageSharp;

namespace IthmbCodec;

internal static unsafe partial class IthmbCodecPlugin
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
    private static readonly byte[] JpegSoiMarker = [0xFF, 0xD8];
    private static readonly byte[] JpegEoiMarker = [0xFF, 0xD9];
    private static readonly byte[] App1Marker = [0xFF, 0xE1];

    // ------------------------------ Raw profile enums ------------------------------
    internal enum IthmbEncoding { Rgb565, Yuv422, Ycbcr420 }

    internal readonly record struct IthmbVariantProfile(
        int Prefix, int Width, int Height, IthmbEncoding Encoding,
        int FrameByteLength,
        bool SwapsDimensions = false, bool LittleEndian = true,
        bool IsPadded = false, bool IsInterlaced = false);

    private static FrozenDictionary<int, IthmbVariantProfile> KnownProfiles = GetBuiltInProfiles();

    private static FrozenDictionary<int, IthmbVariantProfile> GetBuiltInProfiles() =>
        new Dictionary<int, IthmbVariantProfile>
        {
            [1007] = new(1007, 480, 864, IthmbEncoding.Rgb565, 480 * 864 * 2),
            [1009] = new(1009, 42, 30, IthmbEncoding.Rgb565, 42 * 30 * 2),
            // iPod Photo 4G full-screen
            [1013] = new(1013, 220, 176, IthmbEncoding.Rgb565, 220 * 176 * 2),
            [1015] = new(1015, 130, 88, IthmbEncoding.Rgb565, 130 * 88 * 2),
            [1019] = new(1019, 720, 480, IthmbEncoding.Yuv422, 720 * 480 * 2, IsInterlaced: true),
            [1020] = new(1020, 176, 220, IthmbEncoding.Rgb565, 176 * 220 * 2, SwapsDimensions: true),
            [1023] = new(1023, 176, 132, IthmbEncoding.Rgb565, 176 * 132 * 2),
            // iPod Classic 5G/6G full-screen
            [1024] = new(1024, 320, 240, IthmbEncoding.Rgb565, 320 * 240 * 2),
            // iPod Classic thumbnail
            [1036] = new(1036, 50, 41, IthmbEncoding.Rgb565, 50 * 41 * 2),
            // iPod Classic 6G square photo thumbnail
            [1066] = new(1066, 64, 64, IthmbEncoding.Rgb565, 64 * 64 * 2),
            // iPod Classic 6G / nano 3G: 12-bit YCbCr 4:2:0 packed into 2 Bpp frame
            [1067] = new(1067, 720, 480, IthmbEncoding.Ycbcr420, 720 * 480 * 2, IsPadded: true),
            // iPod Nano 4G photo thumbnails
            [1079] = new(1079, 80, 80, IthmbEncoding.Rgb565, 80 * 80 * 2),
            [1083] = new(1083, 240, 320, IthmbEncoding.Rgb565, 240 * 320 * 2),
            // iPod Nano 5G photo
            [1087] = new(1087, 384, 384, IthmbEncoding.Rgb565, 384 * 384 * 2),
            // iPhone 1G/2G, iPod Touch 1G/2G full-screen
            [3008] = new(3008, 640, 480, IthmbEncoding.Rgb565, 640 * 480 * 2),
        }.ToFrozenDictionary();

    private static bool _profilesLoaded;

    // Cached host function pointers (set once during init, eliminates pointer chase per call)
    private static delegate* unmanaged[Cdecl]<void*, int> _isCanceledFn;
    private static delegate* unmanaged[Cdecl]<int, IGStringRef, void> _logFn;

    // ------------------------------ Static plugin state ------------------------------
    private static volatile IGPluginApi* _pluginApi;
    private static IGCodecApi* _codecApi;
    private static IGHostApi* _hostApi;

    private static char* _bufPluginId, _bufPluginName, _bufVersion;
    private static char* _bufCodecId, _bufCodecName;
    private static char** _bufExtensions;
    private static IGStringRef* _extArray;

    private static readonly object _initLock = new();
    private static readonly ConcurrentDictionary<nint, byte> _liveBuffers = new();

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
    internal static int CodecCanHandleSignature(byte* signature, int length)
    {
        if (signature == null || length < 8) return 0;
        var span = new ReadOnlySpan<byte>(signature, Math.Min(length, 256));
        // SIMD-accelerated search for JPEG SOI marker (FF D8)
        int soi = span.IndexOf(JpegSoiMarker);
        if (soi < 0) return 0;
        // Verify JFIF or Exif within 128 bytes of SOI (must have room for the probe)
        int scanEnd = Math.Min(soi + 128, span.Length);
        int probeLen = scanEnd - soi - 2;
        if (probeLen <= 0) return 0;
        var probe = span.Slice(soi + 2, probeLen);
        return probe.IndexOf(JfifMarker) >= 0 || probe.IndexOf(ExifMarker) >= 0 ? 1 : 0;
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
        if (!_liveBuffers.TryRemove(key, out _)) return;
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

        // Load external profiles on first decode (deferred from init to avoid I/O in GetApi)
        if (!Volatile.Read(ref _profilesLoaded)) { LoadExternalProfiles(); Volatile.Write(ref _profilesLoaded, true); }

        // Check file size before reading
        long fileSize;
        try { fileSize = new FileInfo(path).Length; }
        catch { return IGStatus.IoError; }
        if (fileSize > 100L * 1024 * 1024)
        {
            Log(4, $"ITHMB: file too large ({fileSize} bytes)");
            return IGStatus.DecodeFailed;
        }

        // Read a header buffer for JPEG scan (4 MB or file size, whichever is smaller)
        // This avoids reading the entire file into memory for the common JPEG-embedded path.
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            int peekSize = (int)Math.Min(fileSize, 4L * 1024 * 1024);
            byte[] peek = new byte[peekSize];
            fs.ReadExactly(peek, 0, peekSize);

            if (TryFindJpegSlice(peek, out var jpegOffset, out var jpegLength, cancellation))
            {
                // If JPEG extends beyond peek buffer, read tail to find true EOI
                if (jpegOffset + jpegLength > peekSize)
                {
                    long tailSize = fileSize - (jpegOffset + 2);
                    byte[] tail = new byte[tailSize > 0 ? (int)Math.Min(tailSize, 100L * 1024 * 1024) : 0];
                    if (tail.Length > 0)
                    {
                        fs.Seek(jpegOffset + 2, SeekOrigin.Begin);
                        fs.ReadExactly(tail, 0, tail.Length);
                        int eoiRel = tail.AsSpan().IndexOf(JpegEoiMarker);
                        if (eoiRel >= 0)
                        {
                            // Use the actual file position, not the peek offset
                            long actualEoiPos = jpegOffset + 2L + eoiRel + 2;
                            jpegLength = (int)(actualEoiPos - jpegOffset);
                        }
                        else
                        {
                            // No EOI found — use rest of file
                            jpegLength = (int)(fileSize - jpegOffset);
                        }
                    }
                }

                // Always read the JPEG slice from the FileStream (not the peek buffer)
                byte[] jpegSlice = new byte[jpegLength];
                fs.Seek(jpegOffset, SeekOrigin.Begin);
                int bytesRead = fs.ReadAtLeast(jpegSlice, jpegLength, throwOnEndOfStream: false);
                if (bytesRead < jpegLength) { Log(4, $"ITHMB: truncated JPEG read ({bytesRead}/{jpegLength})"); return IGStatus.DecodeFailed; }
                return DecodeJpegSlice(jpegSlice, 0, jpegLength, (int)fileSize,
                    cancellation, outInfo, outBuf);
            }

            // No embedded JPEG found — read full file for raw profile fallback
            byte[] fileBytes = new byte[(int)fileSize];
            fs.Seek(0, SeekOrigin.Begin);
            fs.ReadExactly(fileBytes, 0, (int)fileSize);

            int prefix = ReadInt32BigEndian(fileBytes, 0);
            if (KnownProfiles.TryGetValue(prefix, out var profile))
            {
                return DecodeRawProfile(fileBytes, profile, cancellation, outInfo, outBuf);
            }

            Log(4, $"ITHMB: '{Path.GetFileName(path)}' no embedded JPEG found, unknown profile prefix {prefix}");
            return IGStatus.DecodeFailed;
        }
        catch (IOException ex) { Log(4, $"ITHMB: read failed '{path}' ({ex.Message})"); return IGStatus.IoError; }
        catch { return IGStatus.Internal; }
    }

    // ------------------------------ JPEG extraction ------------------------------
    internal static bool TryFindJpegSlice(byte[] data, out int offset, out int length, void* cancellation)
    {
        offset = 0; length = 0;
        int i = 0;
        while (i <= data.Length - JpegSoiMarker.Length)
        {
            // SIMD-accelerated search for FF D8
            int soi = data.AsSpan(i).IndexOf(JpegSoiMarker);
            if (soi < 0) return false;
            i += soi;

            // Periodic cancellation check (every 64KB)
            if ((i & 0xFFFF) == 0 && IsCanceled(cancellation)) return false;

            // Verify JFIF or Exif within 128 bytes of SOI
            int scanEnd = Math.Min(i + 128, data.Length);
            int jfifOff = IndexOf(data, JfifMarker, i, scanEnd);
            int exifOff = IndexOf(data, ExifMarker, i, scanEnd);
            if (jfifOff < 0 && exifOff < 0) { i += JpegSoiMarker.Length; continue; }

            offset = i;
            // SIMD-accelerated search for FF D9 after SOI
            int eoiRel = data.AsSpan(offset + JpegSoiMarker.Length).IndexOf(JpegEoiMarker);
            if (eoiRel >= 0)
            {
                length = (offset + JpegSoiMarker.Length + eoiRel + JpegEoiMarker.Length) - offset;
                return true;
            }
            // No EOI found — treat rest of file as the JPEG payload
            length = data.Length - offset;
            return true;
        }
        return false;
    }

    private static IGStatus DecodeJpegSlice(byte[] data, int offset, int length, int fileSize,
        void* cancellation, IGImageInfo* outInfo, IGPixelBuffer* outBuf)
    {
        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        if (offset < 0 || length <= 0 || offset + length > data.Length)
            return IGStatus.DecodeFailed;

        // Extract JPEG slice and decode via StbImageSharp (MIT, ~200KB, Native AOT compatible)
        var jpegSlice = new byte[length];
        Buffer.BlockCopy(data, offset, jpegSlice, 0, length);
        ImageResult result;
        try
        {
            result = ImageResult.FromMemory(jpegSlice, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex)
        {
            Log(4, $"ITHMB: JPEG decode failed ({ex.Message})");
            return IGStatus.DecodeFailed;
        }

        if (result == null || result.Width <= 0 || result.Height <= 0)
            return IGStatus.DecodeFailed;

        int w = result.Width, h = result.Height;
        int hasAlpha = 0; // stb_image always outputs alpha channel (RGBA) — we treat it as opaque
        FillImageInfo(outInfo, w, h, hasAlpha, ReadExifOrientation(data, offset, length), fileSize);

        if (outBuf == null) return IGStatus.OK; // metadata-only
        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        // Allocate native BGRA buffer and convert RGBA→BGRA
        var allocStatus = AllocateBgraBuffer(w, h, out var stride, out var pixels);
        if (allocStatus != IGStatus.OK) return allocStatus;

        try
        {
            var srcData = result.Data;
            for (int i = 0; i < w * h; i++)
            {
                int si = i * 4;
                pixels[i * 4 + 0] = srcData[si + 2]; // B = R
                pixels[i * 4 + 1] = srcData[si + 1]; // G = G
                pixels[i * 4 + 2] = srcData[si + 0]; // R = B
                pixels[i * 4 + 3] = srcData[si + 3]; // A = A
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
        _liveBuffers.TryAdd((nint)pixels, 0);
        return IGStatus.OK;
    }

    // ------------------------------ Raw profile decoding ------------------------------
    private static IGStatus DecodeRawProfile(byte[] data, IthmbVariantProfile profile,
        void* cancellation, IGImageInfo* outInfo, IGPixelBuffer* outBuf)
    {
        int w = profile.Width, h = profile.Height;
        if (profile.SwapsDimensions) (w, h) = (h, w);
        int frameSize = profile.FrameByteLength;

        if (data.Length < 4 + frameSize) { Log(4, "ITHMB: raw file too small for profile"); return IGStatus.DecodeFailed; }

        int fileSize = data.Length; // actual file bytes read (available before FillImageInfo)
        FillImageInfo(outInfo, w, h, hasAlpha: 0, orientation: 1, fileSize: fileSize);

        if (outBuf == null) return IGStatus.OK;
        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        var allocStatus = AllocateBgraBuffer(w, h, out var stride, out var pixels);
        if (allocStatus != IGStatus.OK) return allocStatus;

        try
        {
            var raw = data.AsSpan(4);
            // For padded profiles, trim to the valid pixel data portion
            if (profile.IsPadded)
            {
                int validSize = w * h * 3 / 2;
                if (raw.Length > validSize) raw = raw[..validSize];
            }
            switch (profile.Encoding)
            {
                case IthmbEncoding.Rgb565:
                    DecodeRgb565(raw, pixels, w, h, profile.LittleEndian);
                    break;
                case IthmbEncoding.Yuv422:
                    if (profile.IsInterlaced)
                        DecodeYuv422Interlaced(raw, pixels, w, h);
                    else
                        DecodeYuv422(raw, pixels, w, h);
                    break;
                case IthmbEncoding.Ycbcr420:
                    DecodeYcbcr420(raw, pixels, w, h);
                    break;
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
        _liveBuffers.TryAdd((nint)pixels, 0);
        return IGStatus.OK;
    }


    // ------------------------------ Helpers ------------------------------

    /// <summary>Populates an IGImageInfo with common defaults.</summary>
    private static void FillImageInfo(IGImageInfo* info, int w, int h, int hasAlpha, int orientation, long fileSize = -1)
    {
        info->Width = w;
        info->Height = h;
        info->PixelFormat = (int)IGPixelFormat.Bgra8Unorm;
        info->HasAlpha = hasAlpha;
        info->HdrTransferFn = (int)IGHdrTransferFn.None;
        info->ColorSpace = (int)IGColorSpace.Srgb;
        info->Orientation = orientation;
        info->FrameCount = 1;
        info->FileSizeBytes = fileSize;
        info->IccProfileData = null;
        info->IccProfileSize = 0;
    }

    /// <summary>Allocates a BGRA8 pixel buffer; returns OOM status on failure.</summary>
    private static IGStatus AllocateBgraBuffer(int w, int h, out ulong stride, out byte* pixels)
    {
        stride = (ulong)w * 4UL;
        ulong size = stride * (ulong)h;
        if (size > int.MaxValue) { pixels = null; return IGStatus.OutOfMemory; }
        pixels = (byte*)NativeMemory.AllocZeroed((nuint)size);
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
        int len = end - start;
        return len <= 0 ? -1 : haystack.AsSpan(start, len).IndexOf(needle);
    }

    /// <summary>
    /// Reads the EXIF Orientation tag (0x0112) from a JPEG slice.
    /// Returns 1-8 on success, or 1 (normal) if not found.
    /// </summary>
    internal static int ReadExifOrientation(byte[] data, int jpegOffset, int jpegLength)
    {
        int end = jpegOffset + jpegLength;
        var jpeg = data.AsSpan(jpegOffset, jpegLength);

        // SIMD-accelerated search for APP1 marker (FF E1)
        int app1Rel = jpeg.IndexOf(App1Marker);
        if (app1Rel < 0) return 1;
        int app1Start = jpegOffset + app1Rel;

        // APP1 segment: FF E1 len_len (big-endian 16-bit length including self)
        if (app1Start + 4 >= end) return 1;
        int segEnd = app1Start + 2 + ReadU16BE(data, app1Start + 2);
        if (segEnd > end) return 1;

        // Look for "Exif\0\0" header within APP1
        int exifOff = app1Start + 4;
        if (exifOff + 6 > end) return 1;
        if (data[exifOff] != 'E' || data[exifOff + 1] != 'x' ||
            data[exifOff + 2] != 'i' || data[exifOff + 3] != 'f' ||
            data[exifOff + 4] != 0 || data[exifOff + 5] != 0) return 1;

        // TIFF header: "II" (little-endian) or "MM" (big-endian)
        int tiffStart = exifOff + 6;
        if (tiffStart + 8 > end) return 1;
        bool le = data[tiffStart] == 'I' && data[tiffStart + 1] == 'I';
        bool be = data[tiffStart] == 'M' && data[tiffStart + 1] == 'M';
        if (!le && !be) return 1;

        // TIFF magic: 0x002A
        if ((le ? ReadU16LE(data, tiffStart + 2) : ReadU16BE(data, tiffStart + 2)) != 0x002A) return 1;

        // IFD0 offset
        int ifdOff = tiffStart + 4;
        int ifdPos = tiffStart + (int)(le ? ReadU32LE(data, ifdOff) : ReadU32BE(data, ifdOff));
        if (ifdPos <= tiffStart || ifdPos + 2 > end) return 1;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCanceled(void* cancellation)
    {
        // Use cached function pointer (set during init). Avoids chasing _hostApi->Core->{fn} per call.
        return cancellation != null && _isCanceledFn != null && _isCanceledFn(cancellation) != 0;
    }

    private static void Log(int level, string message)
    {
        if (_logFn == null) return;
        fixed (char* p = message) _logFn(level, new IGStringRef { Data = p, Length = message.Length });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IGStringRef MakeStringRef(char* data, int len) => new() { Data = data, Length = len };

    // ------------------------------ External profiles.json ------------------------------

    /// <summary>
    /// Looks for a profiles.json sidecar file next to the plugin DLL.
    /// If found, parses it and merges entries into KnownProfiles (external overrides built-in).
    /// Safe for Native AOT: uses a minimal manual JSON parser (no reflection).
    /// </summary>
    private static void LoadExternalProfiles()
    {
        string? jsonPath = null;
        try
        {
            // Look relative to the app base directory (works in Native AOT)
            string baseDir = AppContext.BaseDirectory;
            jsonPath = Path.Combine(baseDir, "profiles.json");
            if (!File.Exists(jsonPath))
            {
                // Fallback: current working directory
                jsonPath = Path.Combine(Environment.CurrentDirectory, "profiles.json");
                if (!File.Exists(jsonPath)) return;
            }
        }
        catch { return; }
        if (jsonPath == null) return;

        string json;
        try { json = File.ReadAllText(jsonPath); }
        catch { return; }

        if (string.IsNullOrWhiteSpace(json)) return;

        var external = new Dictionary<int, IthmbVariantProfile>();
        try
        {
            ParseProfilesJson(json, external);
        }
        catch { return; }

        if (external.Count == 0) return;

        // Merge: start with built-in, override with external, rebuild
        var merged = new Dictionary<int, IthmbVariantProfile>();
        foreach (var kv in GetBuiltInProfiles()) merged[kv.Key] = kv.Value;
        foreach (var kv in external) merged[kv.Key] = kv.Value;
        KnownProfiles = merged.ToFrozenDictionary();
    }

    /// <summary>Minimal AOT-safe JSON parser for the profiles.json schema.</summary>
    private static void ParseProfilesJson(string json, Dictionary<int, IthmbVariantProfile> output)
    {
        int pos = 0;
        SkipWhitespace(json, ref pos);
        if (pos >= json.Length || json[pos] != '[') return;
        pos++; // skip '['

        while (pos < json.Length)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) break;
            if (json[pos] == ']') { pos++; break; }

            // Parse object
            if (json[pos] != '{') return;
            pos++; // skip '{'

            int prefix = 0, width = 0, height = 0, frameBytes = 0;
            string encoding = "rgb565";
            bool swapsDim = false, le = true, padded = false, interlaced = false;

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] == '}') { pos++; break; }

                // Read key
                string? key = ParseJsonString(json, ref pos);
                if (key == null) return;
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] != ':') return;
                pos++; // skip ':'
                SkipWhitespace(json, ref pos);

                // Read value (type depends on key)
                switch (key)
                {
                    case "prefix": prefix = ParseJsonInt(json, ref pos); break;
                    case "width": width = ParseJsonInt(json, ref pos); break;
                    case "height": height = ParseJsonInt(json, ref pos); break;
                    case "frameBytes": frameBytes = ParseJsonInt(json, ref pos); break;
                    case "encoding": encoding = ParseJsonString(json, ref pos) ?? "rgb565"; break;
                    case "swapsDimensions": swapsDim = ParseJsonBool(json, ref pos); break;
                    case "littleEndian": le = ParseJsonBool(json, ref pos); break;
                    case "isPadded": padded = ParseJsonBool(json, ref pos); break;
                    case "isInterlaced": interlaced = ParseJsonBool(json, ref pos); break;
                    default: SkipJsonValue(json, ref pos); break;
                }

                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ',') pos++;
            }

            if (prefix > 0 && width > 0 && height > 0 && frameBytes > 0)
            {
                var enc = string.Equals(encoding, "yuv422", StringComparison.OrdinalIgnoreCase) ? IthmbEncoding.Yuv422
                    : string.Equals(encoding, "ycbcr420", StringComparison.OrdinalIgnoreCase) ? IthmbEncoding.Ycbcr420
                    : IthmbEncoding.Rgb565;
                output[prefix] = new IthmbVariantProfile(prefix, width, height, enc, frameBytes,
                    SwapsDimensions: swapsDim, LittleEndian: le, IsPadded: padded, IsInterlaced: interlaced);
            }

            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ',') pos++;
        }
    }

    private static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            char c = s[pos];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { pos++; continue; }
            // Skip // line comments (useful for profiles.json documentation)
            if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            break;
        }
    }

    private static string? ParseJsonString(string s, ref int pos)
    {
        if (pos >= s.Length || s[pos] != '"') return null;
        pos++; // skip opening quote
        int start = pos;
        while (pos < s.Length && s[pos] != '"') pos++;
        if (pos >= s.Length) return null;
        string result = s[start..pos];
        pos++; // skip closing quote
        return result;
    }

    private static int ParseJsonInt(string s, ref int pos)
    {
        int sign = 1, val = 0;
        if (pos < s.Length && s[pos] == '-') { sign = -1; pos++; }
        while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
        {
            val = val * 10 + (s[pos] - '0');
            pos++;
        }
        return sign * val;
    }

    private static bool ParseJsonBool(string s, ref int pos)
    {
        if (pos + 4 <= s.Length && s[pos..(pos + 4)] == "true") { pos += 4; return true; }
        if (pos + 5 <= s.Length && s[pos..(pos + 5)] == "false") { pos += 5; return false; }
        return false; // default
    }

    private static void SkipJsonValue(string s, ref int pos)
    {
        if (pos >= s.Length) return;
        if (s[pos] == '"') { ParseJsonString(s, ref pos); return; }
        if (s[pos] == '{' || s[pos] == '[')
        {
            int depth = 1;
            pos++;
            while (pos < s.Length && depth > 0)
            {
                if (s[pos] == '{' || s[pos] == '[') depth++;
                else if (s[pos] == '}' || s[pos] == ']') depth--;
                pos++;
            }
            return;
        }
        // number or boolean
        while (pos < s.Length && s[pos] != ',' && s[pos] != '}' && s[pos] != ']' && !char.IsWhiteSpace(s[pos]))
            pos++;
    }

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

        // Cache host function pointers for fast-path access
        if (_hostApi != null && _hostApi->Core != null)
        {
            _isCanceledFn = _hostApi->Core->IsCancellationRequested;
            _logFn = _hostApi->Core->Log;
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
