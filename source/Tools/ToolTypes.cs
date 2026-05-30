/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Tools;


/// <summary>
/// A single pixel color.
/// </summary>
public readonly record struct ToolColor(byte R, byte G, byte B, byte A);

/// <summary>
/// A rectangle in source image coordinates.
/// </summary>
public readonly record struct ToolRect(float X, float Y, float Width, float Height);


/// <summary>
/// Data sent with <see cref="MessageTypes.INIT"/>.
/// </summary>
public sealed class ToolInitPayload
{
    public string ToolId { get; init; } = string.Empty;
    public string DataDirectory { get; init; } = string.Empty;
    public string PipeName { get; init; } = string.Empty;
    public ThemeInfo? ThemeInfo { get; init; }
}


/// <summary>
/// Flags that control which real-time events a tool receives.
/// </summary>
public sealed class ToolEventSubscriptions
{
    public bool PointerMoved { get; init; }
    public bool PointerPressed { get; init; }
    public bool SelectionChanged { get; init; }
    public bool FrameChanged { get; init; }
}



#region Photo Info

/// <summary>
/// Detailed metadata for the current photo.
/// </summary>
public sealed class ToolPhotoMetadata
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;
    public long FileSizeInBytes { get; init; }
    public DateTime FileCreationTimeUtc { get; init; }
    public DateTime FileLastWriteTimeUtc { get; init; }

    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? Format { get; init; }
    public int FrameCount { get; init; } = 1;
    public bool CanAnimate { get; init; }
    public bool HasAlpha { get; init; }

    public string? ColorSpace { get; init; }
    public string? ColorProfileName { get; init; }

    public int ExifRatingPercent { get; init; }
    public DateTime? ExifDateTimeOriginal { get; init; }
    public string? ExifImageDescription { get; init; }
    public string? ExifModel { get; init; }
    public string? ExifArtist { get; init; }
    public string? ExifCopyright { get; init; }
    public string? ExifSoftware { get; init; }
    public float? ExifExposureTime { get; init; }
    public float? ExifFNumber { get; init; }
    public int? ExifISOSpeed { get; init; }
    public float? ExifFocalLength { get; init; }
}


/// <summary>
/// An item in the photo list.
/// </summary>
public sealed class ToolPhotoListItem
{
    public string FilePath { get; init; } = string.Empty;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Format { get; init; }
}


/// <summary>
/// Photo collection with current selection index.
/// </summary>
public sealed class ToolPhotoList
{
    public ToolPhotoListItem[] Photos { get; init; } = [];
    public int CurrentIndex { get; init; } = -1;
}

#endregion


#region Events

/// <summary>
/// Language changed event data.
/// </summary>
public sealed class LanguageChangedEventArgs
{
    /// <summary>
    /// BCP 47 language code (e.g. "en-US", "vi-VN").
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// English name of the language (e.g. "Vietnamese").
    /// </summary>
    public string EnglishName { get; init; } = string.Empty;

    /// <summary>
    /// Local name of the language (e.g. "Tiếng Việt").
    /// </summary>
    public string LocalName { get; init; } = string.Empty;
}


/// <summary>
/// Photo changed event data.
/// </summary>
public sealed class PhotoChangedEventArgs
{
    public string? FilePath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? Format { get; init; }
    public int FrameCount { get; init; } = 1;
    public bool CanAnimate { get; init; }
}


/// <summary>
/// Pointer event data in source and client coordinates.
/// </summary>
public sealed class PointerEventArgs
{
    public float SourceX { get; init; }
    public float SourceY { get; init; }
    public float ClientX { get; init; }
    public float ClientY { get; init; }
    public string? Button { get; init; }
}


/// <summary>
/// Selection event data.
/// </summary>
public sealed class SelectionEventArgs
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
}


/// <summary>
/// Theme information.
/// </summary>
public sealed class ThemeInfo
{
    public bool IsDarkMode { get; init; }
    public string? AccentColor { get; init; }
    public string? BackgroundColor { get; init; }
    public string? ForegroundColor { get; init; }
}

#endregion


#region Request/Response Payloads

public sealed class ReadPixelRequest
{
    public int X { get; init; }
    public int Y { get; init; }
}


public sealed class ReadPixelResponse
{
    public byte R { get; init; }
    public byte G { get; init; }
    public byte B { get; init; }
    public byte A { get; init; }
}


public sealed class GetPixelBufferRequest
{
    public bool SelectionOnly { get; init; }
}


public sealed class GetPixelBufferResponse
{
    public string MmfPath { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int Stride { get; init; }
    public string ColorType { get; init; } = string.Empty;
}


public sealed class ReleasePixelBufferRequest
{
    public string MmfPath { get; init; } = string.Empty;
}


public sealed class RunApiRequest
{
    public string ApiName { get; init; } = string.Empty;
    public string? Argument { get; init; }
}


public sealed class RunApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }
}


public sealed class SourceSizeResponse
{
    public int Width { get; init; }
    public int Height { get; init; }
}


public sealed class SetSelectionRequest
{
    public float? X { get; init; }
    public float? Y { get; init; }
    public float? Width { get; init; }
    public float? Height { get; init; }
}


public sealed class EnableSelectionRequest
{
    public bool Enable { get; init; }
}


public sealed class FrameChangedPayload
{
    public int FrameIndex { get; init; }
}

#endregion
