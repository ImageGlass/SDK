/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Plugins;

/// <summary>
/// Status codes returned across the native ABI. Values are stable; never reorder or repurpose.
/// </summary>
public enum IGStatus : int
{
    /// <summary>The operation completed successfully.</summary>
    OK = 0,

    /// <summary>The requested operation or format is not supported by this codec.</summary>
    Unsupported = 1,

    /// <summary>The operation was canceled via the host cancellation token.</summary>
    Canceled = 2,

    /// <summary>One or more arguments were invalid (e.g. out-of-range frame index).</summary>
    InvalidArg = 3,

    /// <summary>Decoding failed (e.g. corrupt or truncated input).</summary>
    DecodeFailed = 4,

    /// <summary>A memory allocation failed.</summary>
    OutOfMemory = 5,

    /// <summary>An unexpected internal error occurred inside the plugin.</summary>
    Internal = 6,

    /// <summary>The called entry point is not implemented.</summary>
    NotImplemented = 7,

    /// <summary>A file or stream I/O error occurred.</summary>
    IoError = 8,
}
