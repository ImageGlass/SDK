/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Text.Json;

namespace ImageGlass.SDK.Tools;

/// <summary>
/// Wire-format envelope for all host vs tool messages.
/// Each message is serialized as a single JSON line terminated by <c>\n</c>.
/// </summary>
public sealed class ToolMessage
{
    /// <summary>
    /// Message type identifier (ALL_CAP_SNAKE_CASE).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Correlates a plugin request with the host's response.
    /// Null for one-way events.
    /// </summary>
    public int? RequestId { get; init; }

    /// <summary>
    /// Type-specific payload data.
    /// </summary>
    public JsonElement? Payload { get; init; }
}
