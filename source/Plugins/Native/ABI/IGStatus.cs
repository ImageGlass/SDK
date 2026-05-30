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
    OK = 0,
    Unsupported = 1,
    Canceled = 2,
    InvalidArg = 3,
    DecodeFailed = 4,
    OutOfMemory = 5,
    Internal = 6,
    NotImplemented = 7,
    IoError = 8,
}
