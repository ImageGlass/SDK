/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.IO.MemoryMappedFiles;

namespace ImageGlass.SDK.Tools;

/// <summary>
/// Read-only image pixels the host shares through a memory-mapped file.
/// </summary>
public sealed unsafe class PixelBuffer : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _scan0;
    private bool _disposed;

    /// <summary>Buffer width in pixels.</summary>
    public int Width { get; }

    /// <summary>Buffer height in pixels.</summary>
    public int Height { get; }

    /// <summary>Number of bytes per pixel row, including any padding.</summary>
    public int Stride { get; }

    /// <summary>Skia color type name describing the pixel layout (e.g. "Bgra8888").</summary>
    public string ColorType { get; }

    /// <summary>Path of the backing memory-mapped file.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Address of the first pixel row (rows are <see cref="Stride"/> bytes apart); valid until disposed.
    /// </summary>
    public nint Scan0
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (nint)_scan0;
        }
    }

    internal PixelBuffer(string mmfPath, int width, int height, int stride, string colorType)
    {
        FilePath = mmfPath;
        Width = width;
        Height = height;
        Stride = stride;
        ColorType = colorType;

        _mmf = MemoryMappedFile.CreateFromFile(mmfPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _mmf.CreateViewAccessor(0, (long)stride * height, MemoryMappedFileAccess.Read);

        // Every AcquirePointer needs its own ReleasePointer, so acquire once here and release in Dispose.
        byte* ptr = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _scan0 = ptr + _view.PointerOffset;
    }


    /// <summary>
    /// Gets the raw pixel data as a read-only span (no copy).
    /// </summary>
    public ReadOnlySpan<byte> GetPixels()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ReadOnlySpan<byte>(_scan0, Stride * Height);
    }


    /// <summary>
    /// Releases the mapped view and the underlying memory-mapped file.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _mmf.Dispose();
    }
}
