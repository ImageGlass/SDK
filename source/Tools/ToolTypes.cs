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
    /// <summary>Unique identifier of the tool being initialized.</summary>
    public string ToolId { get; init; } = string.Empty;

    /// <summary>Per-tool data directory for caches or local state.</summary>
    public string DataDirectory { get; init; } = string.Empty;

    /// <summary>Name of the named pipe used for host communication.</summary>
    public string PipeName { get; init; } = string.Empty;

    /// <summary>Current theme information at initialization time.</summary>
    public ThemeInfo? ThemeInfo { get; init; }
}


/// <summary>
/// Flags that control which real-time events a tool receives.
/// </summary>
public sealed class ToolEventSubscriptions
{
    /// <summary>Receive <see cref="MessageTypes.POINTER_MOVED"/> events.</summary>
    public bool PointerMoved { get; init; }

    /// <summary>Receive <see cref="MessageTypes.POINTER_PRESSED"/> events.</summary>
    public bool PointerPressed { get; init; }

    /// <summary>Receive <see cref="MessageTypes.SELECTION_CHANGED"/> events.</summary>
    public bool SelectionChanged { get; init; }

    /// <summary>Receive <see cref="MessageTypes.FRAME_CHANGED"/> events.</summary>
    public bool FrameChanged { get; init; }
}



#region Photo Info

/// <summary>
/// Detailed metadata for the current photo.
/// </summary>
public sealed class ToolPhotoMetadata
{
    /// <summary>Absolute path to the photo file.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>File name including extension.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>File extension including the leading dot (e.g. ".jpg").</summary>
    public string FileExtension { get; init; } = string.Empty;

    /// <summary>Absolute path to the containing folder.</summary>
    public string FolderPath { get; init; } = string.Empty;

    /// <summary>Name of the containing folder.</summary>
    public string FolderName { get; init; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSizeInBytes { get; init; }

    /// <summary>File creation time, in UTC.</summary>
    public DateTime FileCreationTimeUtc { get; init; }

    /// <summary>File last-write time, in UTC.</summary>
    public DateTime FileLastWriteTimeUtc { get; init; }

    /// <summary>Original (decoded) image width in pixels, before any viewer scaling.</summary>
    public int OriginalWidth { get; init; }

    /// <summary>Original (decoded) image height in pixels, before any viewer scaling.</summary>
    public int OriginalHeight { get; init; }

    /// <summary>Current image width in pixels as presented by the viewer.</summary>
    public int Width { get; init; }

    /// <summary>Current image height in pixels as presented by the viewer.</summary>
    public int Height { get; init; }

    /// <summary>Image format name (e.g. "JPEG", "PNG").</summary>
    public string? Format { get; init; }

    /// <summary>Number of frames; 1 for static images.</summary>
    public int FrameCount { get; init; } = 1;

    /// <summary>Whether the image can be animated.</summary>
    public bool CanAnimate { get; init; }

    /// <summary>Whether the image carries an alpha channel.</summary>
    public bool HasAlpha { get; init; }

    /// <summary>Color space name, if known.</summary>
    public string? ColorSpace { get; init; }

    /// <summary>Embedded color profile name, if any.</summary>
    public string? ColorProfileName { get; init; }

    /// <summary>EXIF rating as a percentage (0–100).</summary>
    public int ExifRatingPercent { get; init; }

    /// <summary>EXIF original capture date/time.</summary>
    public DateTime? ExifDateTimeOriginal { get; init; }

    /// <summary>EXIF image description.</summary>
    public string? ExifImageDescription { get; init; }

    /// <summary>EXIF camera model.</summary>
    public string? ExifModel { get; init; }

    /// <summary>EXIF artist / author.</summary>
    public string? ExifArtist { get; init; }

    /// <summary>EXIF copyright.</summary>
    public string? ExifCopyright { get; init; }

    /// <summary>EXIF software that produced the image.</summary>
    public string? ExifSoftware { get; init; }

    /// <summary>EXIF exposure time in seconds.</summary>
    public float? ExifExposureTime { get; init; }

    /// <summary>EXIF aperture (F-number).</summary>
    public float? ExifFNumber { get; init; }

    /// <summary>EXIF ISO speed rating.</summary>
    public int? ExifISOSpeed { get; init; }

    /// <summary>EXIF focal length in millimeters.</summary>
    public float? ExifFocalLength { get; init; }
}


/// <summary>
/// An item in the photo list.
/// </summary>
public sealed class ToolPhotoListItem
{
    /// <summary>Absolute path to the photo file.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Image width in pixels, if known.</summary>
    public int? Width { get; init; }

    /// <summary>Image height in pixels, if known.</summary>
    public int? Height { get; init; }

    /// <summary>Image format name, if known.</summary>
    public string? Format { get; init; }
}


/// <summary>
/// Photo collection with current selection index.
/// </summary>
public sealed class ToolPhotoList
{
    /// <summary>All photos in the current collection.</summary>
    public ToolPhotoListItem[] Photos { get; init; } = [];

    /// <summary>Index of the currently displayed photo, or -1 if none.</summary>
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
    /// <summary>Absolute path to the new photo, or null if the photo was unloaded.</summary>
    public string? FilePath { get; init; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Image format name, if known.</summary>
    public string? Format { get; init; }

    /// <summary>Number of frames; 1 for static images.</summary>
    public int FrameCount { get; init; } = 1;

    /// <summary>Whether the image can be animated.</summary>
    public bool CanAnimate { get; init; }
}


/// <summary>
/// Pointer event data in source and client coordinates.
/// </summary>
public sealed class PointerEventArgs
{
    /// <summary>Pointer X in source image coordinates.</summary>
    public float SourceX { get; init; }

    /// <summary>Pointer Y in source image coordinates.</summary>
    public float SourceY { get; init; }

    /// <summary>Pointer X in viewer client coordinates.</summary>
    public float ClientX { get; init; }

    /// <summary>Pointer Y in viewer client coordinates.</summary>
    public float ClientY { get; init; }

    /// <summary>Mouse button associated with the event (e.g. "Left"), if any.</summary>
    public string? Button { get; init; }
}


/// <summary>
/// Selection event data.
/// </summary>
public sealed class SelectionEventArgs
{
    /// <summary>Left edge of the selection in source image coordinates.</summary>
    public float X { get; init; }

    /// <summary>Top edge of the selection in source image coordinates.</summary>
    public float Y { get; init; }

    /// <summary>Selection width in source pixels.</summary>
    public float Width { get; init; }

    /// <summary>Selection height in source pixels.</summary>
    public float Height { get; init; }
}


/// <summary>
/// Theme information.
/// </summary>
public sealed class ThemeInfo
{
    /// <summary>Whether the host is using a dark theme.</summary>
    public bool IsDarkMode { get; init; }

    /// <summary>Accent color as a hex string (e.g. "#0078D4"), if available.</summary>
    public string? AccentColor { get; init; }

    /// <summary>Background color as a hex string, if available.</summary>
    public string? BackgroundColor { get; init; }

    /// <summary>Foreground (text) color as a hex string, if available.</summary>
    public string? ForegroundColor { get; init; }
}

#endregion


#region Request/Response Payloads

/// <summary>Request payload for <see cref="MessageTypes.READ_PIXEL"/>.</summary>
public sealed class ReadPixelRequest
{
    /// <summary>X coordinate in source image pixels.</summary>
    public int X { get; init; }

    /// <summary>Y coordinate in source image pixels.</summary>
    public int Y { get; init; }
}


/// <summary>Response payload for <see cref="MessageTypes.READ_PIXEL"/>.</summary>
public sealed class ReadPixelResponse
{
    /// <summary>Red channel (0–255).</summary>
    public byte R { get; init; }

    /// <summary>Green channel (0–255).</summary>
    public byte G { get; init; }

    /// <summary>Blue channel (0–255).</summary>
    public byte B { get; init; }

    /// <summary>Alpha channel (0–255).</summary>
    public byte A { get; init; }
}


/// <summary>Request payload for <see cref="MessageTypes.GET_PIXEL_BUFFER"/>.</summary>
public sealed class GetPixelBufferRequest
{
    /// <summary>When true, return only the pixels within the current selection.</summary>
    public bool SelectionOnly { get; init; }
}


/// <summary>Response payload for <see cref="MessageTypes.GET_PIXEL_BUFFER"/>.</summary>
public sealed class GetPixelBufferResponse
{
    /// <summary>Path of the memory-mapped file holding the pixel data.</summary>
    public string MmfPath { get; init; } = string.Empty;

    /// <summary>Buffer width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Buffer height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Number of bytes per pixel row.</summary>
    public int Stride { get; init; }

    /// <summary>Skia color type name describing the pixel layout.</summary>
    public string ColorType { get; init; } = string.Empty;
}


/// <summary>Request payload for <see cref="MessageTypes.RELEASE_PIXEL_BUFFER"/>.</summary>
public sealed class ReleasePixelBufferRequest
{
    /// <summary>Path of the memory-mapped file to release.</summary>
    public string MmfPath { get; init; } = string.Empty;
}


/// <summary>Request payload for <see cref="MessageTypes.RUN_API"/>.</summary>
public sealed class RunApiRequest
{
    /// <summary>Name of the ImageGlass API method to invoke.</summary>
    public string ApiName { get; init; } = string.Empty;

    /// <summary>Optional argument passed to the API method.</summary>
    public string? Argument { get; init; }
}


/// <summary>Response payload for <see cref="MessageTypes.RUN_API"/>.</summary>
public sealed class RunApiResponse
{
    /// <summary>Whether the API call succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }
}


/// <summary>Response payload for <see cref="MessageTypes.GET_SOURCE_SIZE"/>.</summary>
public sealed class SourceSizeResponse
{
    /// <summary>Source image width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Source image height in pixels.</summary>
    public int Height { get; init; }
}


/// <summary>Request payload for <see cref="MessageTypes.SET_SELECTION"/>. Null fields clear the selection.</summary>
public sealed class SetSelectionRequest
{
    /// <summary>Left edge in source coordinates, or null to clear.</summary>
    public float? X { get; init; }

    /// <summary>Top edge in source coordinates, or null to clear.</summary>
    public float? Y { get; init; }

    /// <summary>Selection width in source pixels, or null to clear.</summary>
    public float? Width { get; init; }

    /// <summary>Selection height in source pixels, or null to clear.</summary>
    public float? Height { get; init; }
}


/// <summary>Request payload for <see cref="MessageTypes.ENABLE_SELECTION"/>.</summary>
public sealed class EnableSelectionRequest
{
    /// <summary>Whether selection mode should be enabled.</summary>
    public bool Enable { get; init; }
}


/// <summary>Payload for <see cref="MessageTypes.FRAME_CHANGED"/>.</summary>
public sealed class FrameChangedPayload
{
    /// <summary>Zero-based index of the current animation frame.</summary>
    public int FrameIndex { get; init; }
}

#endregion
