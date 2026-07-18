/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageGlass.SDK.Tools;

/// <summary>
/// Source-generated JSON serialization context for tool IPC message types.
/// Used by external tools and the host's <c>ToolPipeServer</c> over named-pipe IPC.
/// AOT-safe – no runtime reflection.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    // Must stay false: the tool IPC protocol is newline-delimited JSON
    // (one message per line). Indented output would break the framing
    // because StreamReader.ReadLine would treat each indented line as a
    // separate, malformed message.
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolMessage))]
[JsonSerializable(typeof(ToolInitPayload))]
[JsonSerializable(typeof(ReadPixelRequest))]
[JsonSerializable(typeof(ReadPixelResponse))]
[JsonSerializable(typeof(GetPixelBufferRequest))]
[JsonSerializable(typeof(GetPixelBufferResponse))]
[JsonSerializable(typeof(ReleasePixelBufferRequest))]
[JsonSerializable(typeof(RunApiRequest))]
[JsonSerializable(typeof(RunApiResponse))]
[JsonSerializable(typeof(SourceSizeResponse))]
[JsonSerializable(typeof(SetSelectionRequest))]
[JsonSerializable(typeof(EnableSelectionRequest))]
[JsonSerializable(typeof(FrameChangedPayload))]
[JsonSerializable(typeof(PhotoChangedEventArgs))]
[JsonSerializable(typeof(LanguageChangedEventArgs))]
[JsonSerializable(typeof(PointerEventArgs))]
[JsonSerializable(typeof(SelectionEventArgs))]
[JsonSerializable(typeof(ThemeInfo))]
[JsonSerializable(typeof(ToolEventSubscriptions))]
[JsonSerializable(typeof(ToolPhotoMetadata))]
[JsonSerializable(typeof(ToolPhotoList))]
[JsonSerializable(typeof(ToolPhotoListItem))]
[JsonSerializable(typeof(ToolColor))]
[JsonSerializable(typeof(ToolRect))]
[JsonSerializable(typeof(JsonElement))]
public partial class ToolJsonContext : JsonSerializerContext;
