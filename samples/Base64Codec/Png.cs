/*
ImageGlass Base64 Sample Codec Plugin
Copyright (C) 2026 DUONG DIEU PHAP
MIT License
*/
using System.Buffers.Binary;
using System.IO.Compression;

namespace Base64Codec;

/// <summary>
/// Minimal PNG reader and writer on the BCL's zlib, so the sample needs no imaging library.
/// </summary>
internal static class Png
{
    public static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly uint[] CrcTable = CreateCrcTable();

    public static bool HasSignature(ReadOnlySpan<byte> data) => data.StartsWith(Signature);

    public static void WriteChunk(Stream s, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        s.Write(word);
        s.Write(type);
        s.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, UpdateCrc(UpdateCrc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF);
        s.Write(word);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}


/// <summary>
/// Reads any standard PNG (all color types and bit depths, tRNS, Adam7) into BGRA8 straight alpha.
/// </summary>
internal sealed unsafe class PngDecoder
{
    // Adam7 passes as (x0, y0, dx, dy).
    private static ReadOnlySpan<byte> Adam7 => [0, 0, 8, 8, 4, 0, 8, 8, 0, 4, 4, 8, 2, 0, 4, 4, 0, 2, 2, 4, 1, 0, 2, 2, 0, 1, 1, 2];

    private readonly int _bitDepth;
    private readonly int _colorType;
    private readonly int _channels;
    private readonly bool _interlaced;
    private readonly byte[]? _palette;
    private readonly byte[]? _trns;
    private readonly byte[] _idat;

    public int Width { get; }
    public int Height { get; }
    public bool HasAlpha => _colorType is 4 or 6 || _trns is not null;


    /// <summary>
    /// Parses the chunk layout without inflating pixels; throws <see cref="InvalidDataException"/> on a malformed file.
    /// </summary>
    public PngDecoder(ReadOnlySpan<byte> png)
    {
        if (!Png.HasSignature(png)) throw new InvalidDataException("Not a PNG file.");

        using var idat = new MemoryStream();
        var pos = Png.Signature.Length;
        var sawHeader = false;

        while (true)
        {
            if (png.Length - pos < 12) throw new InvalidDataException("Truncated chunk.");
            var len = BinaryPrimitives.ReadInt32BigEndian(png[pos..]);
            if (len < 0 || len > png.Length - pos - 12) throw new InvalidDataException("Bad chunk length.");

            var type = png.Slice(pos + 4, 4);
            var data = png.Slice(pos + 8, len);
            pos += 12 + len;

            if (!sawHeader)
            {
                if (!type.SequenceEqual("IHDR"u8) || len != 13) throw new InvalidDataException("IHDR must come first.");
                Width = BinaryPrimitives.ReadInt32BigEndian(data);
                Height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                _bitDepth = data[8];
                _colorType = data[9];
                if (data[10] != 0 || data[11] != 0 || data[12] > 1) throw new InvalidDataException("Unknown compression, filter or interlace method.");
                _interlaced = data[12] == 1;
                sawHeader = true;
            }
            else if (type.SequenceEqual("PLTE"u8)) _palette = data.ToArray();
            else if (type.SequenceEqual("tRNS"u8) && _colorType is 0 or 2 or 3) _trns = data.ToArray();
            else if (type.SequenceEqual("IDAT"u8)) idat.Write(data);
            else if (type.SequenceEqual("IEND"u8)) break;
        }

        _channels = _colorType switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        var validDepth = _colorType switch
        {
            0 => _bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => _bitDepth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => _bitDepth is 8 or 16,
            _ => false,
        };

        if (Width <= 0 || Height <= 0) throw new InvalidDataException("Bad image size.");
        if (!validDepth) throw new InvalidDataException($"Bad color type {_colorType} / bit depth {_bitDepth}.");
        if (_colorType == 3 && _palette is null) throw new InvalidDataException("Palette image without PLTE.");
        if (idat.Length == 0) throw new InvalidDataException("No image data.");

        _idat = idat.ToArray();
    }


    /// <summary>
    /// Decodes into a caller-owned BGRA8 buffer of <see cref="Width"/> x <see cref="Height"/> pixels.
    /// </summary>
    public void DecodeTo(byte* dst, int dstStride)
    {
        var bitsPerPixel = _channels * _bitDepth;
        // Filters reference the byte one whole pixel back, or one byte back below 8 bits per pixel.
        var filterStep = Math.Max(1, bitsPerPixel / 8);
        var passCount = _interlaced ? 7 : 1;

        long rawSize = 0;
        for (var p = 0; p < passCount; p++)
        {
            var (_, _, w, h, _, _) = Pass(p);
            if (w > 0 && h > 0) rawSize += h * (1 + ((long)w * bitsPerPixel + 7) / 8);
        }
        if (rawSize > Array.MaxLength) throw new OutOfMemoryException("PNG is too large to inflate in one buffer.");

        var raw = new byte[rawSize];
        using (var z = new ZLibStream(new MemoryStream(_idat), CompressionMode.Decompress))
        {
            z.ReadExactly(raw);
        }

        var offset = 0;
        for (var p = 0; p < passCount; p++)
        {
            var (x0, y0, w, h, dx, dy) = Pass(p);
            if (w == 0 || h == 0) continue;

            var rowBytes = (int)(((long)w * bitsPerPixel + 7) / 8);
            var prev = Span<byte>.Empty;

            for (var j = 0; j < h; j++)
            {
                var row = raw.AsSpan(offset + 1, rowBytes);
                Unfilter(raw[offset], row, prev, filterStep);
                WriteRow(row, w, dst + (long)(y0 + j * dy) * dstStride, x0, dx);
                prev = row;
                offset += 1 + rowBytes;
            }
        }
    }


    private (int X0, int Y0, int W, int H, int Dx, int Dy) Pass(int p)
    {
        if (!_interlaced) return (0, 0, Width, Height, 1, 1);

        int x0 = Adam7[p * 4], y0 = Adam7[p * 4 + 1], dx = Adam7[p * 4 + 2], dy = Adam7[p * 4 + 3];
        var w = Width > x0 ? (Width - x0 + dx - 1) / dx : 0;
        var h = Height > y0 ? (Height - y0 + dy - 1) / dy : 0;
        return (x0, y0, w, h, dx, dy);
    }


    private static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> prev, int step)
    {
        // The row above the first one is all zeros.
        var hasPrev = !prev.IsEmpty;

        switch (filter)
        {
            case 0:
                return;
            case 1:
                for (var i = step; i < row.Length; i++) row[i] += row[i - step];
                return;
            case 2:
                if (hasPrev) for (var i = 0; i < row.Length; i++) row[i] += prev[i];
                return;
            case 3:
                for (var i = 0; i < row.Length; i++)
                {
                    int left = i >= step ? row[i - step] : 0, up = hasPrev ? prev[i] : 0;
                    row[i] += (byte)((left + up) >> 1);
                }
                return;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    int left = i >= step ? row[i - step] : 0;
                    int up = hasPrev ? prev[i] : 0;
                    int upLeft = hasPrev && i >= step ? prev[i - step] : 0;
                    row[i] += (byte)Paeth(left, up, upLeft);
                }
                return;
            default:
                throw new InvalidDataException($"Bad filter type {filter}.");
        }
    }


    internal static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }


    private void WriteRow(ReadOnlySpan<byte> row, int count, byte* dstRow, int x0, int dx)
    {
        for (var i = 0; i < count; i++)
        {
            int r, g, b, a = 255;

            switch (_colorType)
            {
                case 0:
                    var v = Sample(row, i);
                    r = g = b = To8(v);
                    if (_trns is { Length: >= 2 } && v == BinaryPrimitives.ReadUInt16BigEndian(_trns)) a = 0;
                    break;

                case 2:
                    int rv = Sample(row, i * 3), gv = Sample(row, i * 3 + 1), bv = Sample(row, i * 3 + 2);
                    (r, g, b) = (To8(rv), To8(gv), To8(bv));
                    if (_trns is { Length: >= 6 }
                        && rv == BinaryPrimitives.ReadUInt16BigEndian(_trns)
                        && gv == BinaryPrimitives.ReadUInt16BigEndian(_trns.AsSpan(2))
                        && bv == BinaryPrimitives.ReadUInt16BigEndian(_trns.AsSpan(4))) a = 0;
                    break;

                case 3:
                    var idx = Sample(row, i);
                    if (idx * 3 + 2 < _palette!.Length) (r, g, b) = (_palette[idx * 3], _palette[idx * 3 + 1], _palette[idx * 3 + 2]);
                    else (r, g, b) = (0, 0, 0);
                    if (_trns is not null && idx < _trns.Length) a = _trns[idx];
                    break;

                case 4:
                    r = g = b = To8(Sample(row, i * 2));
                    a = To8(Sample(row, i * 2 + 1));
                    break;

                default:
                    (r, g, b, a) = (To8(Sample(row, i * 4)), To8(Sample(row, i * 4 + 1)), To8(Sample(row, i * 4 + 2)), To8(Sample(row, i * 4 + 3)));
                    break;
            }

            var px = dstRow + (long)(x0 + i * dx) * 4;
            px[0] = (byte)b;
            px[1] = (byte)g;
            px[2] = (byte)r;
            px[3] = (byte)a;
        }
    }


    private int Sample(ReadOnlySpan<byte> row, int index)
    {
        switch (_bitDepth)
        {
            case 8: return row[index];
            case 16: return (row[index * 2] << 8) | row[index * 2 + 1];
            default:
                // Sub-byte samples are packed from the high bit down.
                var bit = index * _bitDepth;
                return (row[bit >> 3] >> (8 - _bitDepth - (bit & 7))) & ((1 << _bitDepth) - 1);
        }
    }


    private int To8(int v) => _bitDepth switch
    {
        8 => v,
        16 => (v * 255 + 32767) / 65535,
        _ => v * 255 / ((1 << _bitDepth) - 1),
    };
}


/// <summary>
/// Writes BGRA8 straight-alpha pixels as an 8-bit PNG: RGB when fully opaque, RGBA otherwise.
/// </summary>
internal static unsafe class PngEncoder
{
    public static byte[] Encode(byte* bgra, int width, int height, int stride, CompressionLevel level)
    {
        var opaque = IsOpaque(bgra, width, height, stride);
        var channels = opaque ? 3 : 4;
        var rowBytes = (long)width * channels;
        if (rowBytes + 1 > Array.MaxLength) throw new OutOfMemoryException("Row is too wide for one buffer.");

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, level, leaveOpen: true))
        {
            var prev = new byte[rowBytes];
            var cur = new byte[rowBytes];
            var best = new byte[rowBytes + 1];
            var trial = new byte[rowBytes + 1];

            for (var y = 0; y < height; y++)
            {
                var src = bgra + (long)y * stride;
                for (var x = 0; x < width; x++)
                {
                    var o = x * channels;
                    cur[o] = src[x * 4 + 2];
                    cur[o + 1] = src[x * 4 + 1];
                    cur[o + 2] = src[x * 4];
                    if (!opaque) cur[o + 3] = src[x * 4 + 3];
                }

                // Same heuristic as libpng: keep the filter whose residuals have the smallest absolute sum.
                var bestScore = long.MaxValue;
                for (byte f = 0; f <= 4; f++)
                {
                    trial[0] = f;
                    var score = Filter(f, cur, prev, channels, trial.AsSpan(1));
                    if (score < bestScore) (bestScore, best, trial) = (score, trial, best);
                }

                z.Write(best);
                (prev, cur) = (cur, prev);
            }
        }

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;
        ihdr[9] = (byte)(opaque ? 2 : 6);

        using var png = new MemoryStream();
        png.Write(Png.Signature);
        Png.WriteChunk(png, "IHDR"u8, ihdr);
        Png.WriteChunk(png, "IDAT"u8, idat.GetBuffer().AsSpan(0, (int)idat.Length));
        Png.WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }


    private static bool IsOpaque(byte* bgra, int width, int height, int stride)
    {
        for (var y = 0; y < height; y++)
        {
            var row = bgra + (long)y * stride;
            for (var x = 0; x < width; x++)
            {
                if (row[x * 4 + 3] != 255) return false;
            }
        }
        return true;
    }


    private static long Filter(byte filter, ReadOnlySpan<byte> cur, ReadOnlySpan<byte> prev, int step, Span<byte> dst)
    {
        long score = 0;
        for (var i = 0; i < cur.Length; i++)
        {
            int left = i >= step ? cur[i - step] : 0, up = prev[i], upLeft = i >= step ? prev[i - step] : 0;
            var res = (byte)(filter switch
            {
                0 => cur[i],
                1 => cur[i] - left,
                2 => cur[i] - up,
                3 => cur[i] - ((left + up) >> 1),
                _ => cur[i] - PngDecoder.Paeth(left, up, upLeft),
            });
            dst[i] = res;
            score += Math.Abs((int)(sbyte)res);
        }
        return score;
    }
}
