/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Tools;

/// <summary>
/// ALL_CAP_SNAKE_CASE constants for the IPC message protocol.
/// </summary>
public static class MessageTypes
{
    // Host -> Tool events

    /// <summary>Host → tool: sent once after connection to deliver initial settings.</summary>
    public const string INIT = "INIT";

    /// <summary>Host → tool: the current photo changed or was unloaded.</summary>
    public const string PHOTO_CHANGED = "PHOTO_CHANGED";

    /// <summary>Host → tool: the application theme changed.</summary>
    public const string THEME_CHANGED = "THEME_CHANGED";

    /// <summary>Host → tool: the UI language changed.</summary>
    public const string LANGUAGE_CHANGED = "LANGUAGE_CHANGED";

    /// <summary>Host → tool: the viewer color profile changed.</summary>
    public const string COLOR_PROFILE_CHANGED = "COLOR_PROFILE_CHANGED";

    /// <summary>Host → tool: the pointer moved over the viewer (requires subscription).</summary>
    public const string POINTER_MOVED = "POINTER_MOVED";

    /// <summary>Host → tool: the pointer was pressed on the viewer (requires subscription).</summary>
    public const string POINTER_PRESSED = "POINTER_PRESSED";

    /// <summary>Host → tool: the selection rectangle changed (requires subscription).</summary>
    public const string SELECTION_CHANGED = "SELECTION_CHANGED";

    /// <summary>Host → tool: the current animation frame changed (requires subscription).</summary>
    public const string FRAME_CHANGED = "FRAME_CHANGED";

    /// <summary>Host → tool: the tool should shut down and disconnect.</summary>
    public const string SHUTDOWN = "SHUTDOWN";

    /// <summary>Host → tool: the user invoked the tool's primary action.</summary>
    public const string EXECUTE = "EXECUTE";


    // Tool -> Host requests / events

    /// <summary>Tool → host: read a single pixel color at source coordinates.</summary>
    public const string READ_PIXEL = "READ_PIXEL";

    /// <summary>Tool → host: acquire the full pixel buffer via a memory-mapped file.</summary>
    public const string GET_PIXEL_BUFFER = "GET_PIXEL_BUFFER";

    /// <summary>Tool → host: release a previously acquired pixel buffer.</summary>
    public const string RELEASE_PIXEL_BUFFER = "RELEASE_PIXEL_BUFFER";

    /// <summary>Tool → host: get the source image dimensions.</summary>
    public const string GET_SOURCE_SIZE = "GET_SOURCE_SIZE";

    /// <summary>Tool → host: get the current selection rectangle.</summary>
    public const string GET_SELECTION = "GET_SELECTION";

    /// <summary>Tool → host: set or clear the selection rectangle.</summary>
    public const string SET_SELECTION = "SET_SELECTION";

    /// <summary>Tool → host: enable or disable selection mode on the viewer.</summary>
    public const string ENABLE_SELECTION = "ENABLE_SELECTION";

    /// <summary>Tool → host: declare which real-time events the tool wants to receive.</summary>
    public const string SUBSCRIBE_EVENTS = "SUBSCRIBE_EVENTS";

    /// <summary>Tool → host: invoke a named ImageGlass API method.</summary>
    public const string RUN_API = "RUN_API";

    /// <summary>Tool → host: get metadata for the current photo.</summary>
    public const string GET_PHOTO_METADATA = "GET_PHOTO_METADATA";

    /// <summary>Tool → host: get the list of photos in the current collection.</summary>
    public const string GET_PHOTO_LIST = "GET_PHOTO_LIST";

    /// <summary>Tool → host: get the current theme information.</summary>
    public const string GET_THEME_INFO = "GET_THEME_INFO";
}
