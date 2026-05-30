/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using SkiaSharp;
using System.IO.MemoryMappedFiles;

namespace ImageGlass.SDK.Tools;

/// <summary>
/// Cross-process pixel data backed by a memory-mapped file.
/// Provides safe read-only access to raw image pixels shared by the host.
/// </summary>
public sealed class PixelBuffer : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private bool _pointerAcquired;

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public string ColorType { get; }
    public string FilePath { get; }

    internal PixelBuffer(string mmfPath, int width, int height, int stride, string colorType)
    {
        FilePath = mmfPath;
        Width = width;
        Height = height;
        Stride = stride;
        ColorType = colorType;

        _mmf = MemoryMappedFile.CreateFromFile(mmfPath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _mmf.CreateViewAccessor(0, (long)stride * height, MemoryMappedFileAccess.Read);
    }


    /// <summary>
    /// Gets the raw pixel data as a read-only span.
    /// </summary>
    public unsafe ReadOnlySpan<byte> GetPixels()
    {
        byte* ptr = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _pointerAcquired = true;
        return new ReadOnlySpan<byte>(ptr + _view.PointerOffset, Stride * Height);
    }


    /// <summary>
    /// Creates an <see cref="SKBitmap"/> that reads directly from the mapped memory (no copy).
    /// The returned bitmap is only valid while this <see cref="PixelBuffer"/> is alive.
    /// </summary>
    public unsafe SKBitmap ToSKBitmap()
    {
        byte* ptr = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _pointerAcquired = true;

        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap();
        bitmap.InstallPixels(info, (nint)(ptr + _view.PointerOffset), Stride);
        return bitmap;
    }


    public void Dispose()
    {
        if (_pointerAcquired)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointerAcquired = false;
        }
        _view.Dispose();
        _mmf.Dispose();
    }
}
