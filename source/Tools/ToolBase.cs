/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
namespace ImageGlass.SDK.Tools;

/// <summary>
/// Base class for non-hosted external tools.
/// Handles named-pipe connection, message dispatch, and lifecycle.
/// Tool authors subclass this and override <c>OnXxx</c> methods.
/// </summary>
public abstract class ToolBase : IDisposable
{
    private ToolClient? _client;

    /// <summary>
    /// When <c>true</c>, the SDK writes detailed IPC lifecycle traces
    /// (pipe connect, every received message, dispatch enter/exit/failure)
    /// to <see cref="DebugLog"/>. Use this to diagnose tools that appear
    /// to "do nothing" — e.g. wire-format mismatches, swallowed exceptions
    /// in event handlers, or pipe-connection failures.
    /// <para>Set BEFORE calling <see cref="RunAsync"/>.</para>
    /// </summary>
    public bool EnableDebug { get; set; }

    /// <summary>
    /// Sink that receives debug trace lines when <see cref="EnableDebug"/>
    /// is <c>true</c>. Typical implementations append to a log file or
    /// forward to <c>Console.WriteLine</c>. Exceptions thrown by the sink
    /// are swallowed so logging cannot crash the tool.
    /// </summary>
    public Action<string>? DebugLog { get; set; }

    /// <summary>
    /// Writes a trace line to <see cref="DebugLog"/> when
    /// <see cref="EnableDebug"/> is enabled.
    /// </summary>
    internal void Trace(string msg)
    {
        if (!EnableDebug) return;
        try { DebugLog?.Invoke(msg); } catch { }
    }

    /// <summary>
    /// Unique tool identifier. Must match the <c>ToolId</c> registered in <c>igconfig.json</c>.
    /// </summary>
    public abstract string ToolId { get; }

    /// <summary>
    /// The tool client providing host API access.
    /// </summary>
    private protected ToolClient Client => _client
        ?? throw new InvalidOperationException("Tool not initialized.");

    /// <summary>
    /// Host API proxy — viewer, photo info, dialogs, etc.
    /// </summary>
    public IToolHostProxy HostApi => Client.HostApi;

    /// <summary>
    /// Data directory for this tool (set by host during INIT).
    /// Tools can store local caches or state here.
    /// </summary>
    public string DataDirectory { get; internal set; } = string.Empty;

    /// <summary>
    /// Current theme information (updated by host events).
    /// </summary>
    public ThemeInfo? CurrentTheme { get; internal set; }


    #region Lifecycle hooks

    /// <summary>
    /// Called after connection established and settings loaded.
    /// </summary>
    protected internal virtual Task OnInitializedAsync() => Task.CompletedTask;

    /// <summary>
    /// Called when the host requests tool execution.
    /// </summary>
    protected internal virtual Task OnExecuteAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Called when the host requests the tool to shut down.
    /// </summary>
    protected internal virtual Task OnShutdownAsync() => Task.CompletedTask;

    #endregion


    #region Event hooks

    /// <summary>
    /// Called when the current photo changes or is unloaded.
    /// </summary>
    protected internal virtual void OnPhotoChanged(PhotoChangedEventArgs e) { }

    /// <summary>
    /// Called when theme changes.
    /// </summary>
    protected internal virtual void OnThemeChanged(ThemeInfo theme) { }

    /// <summary>
    /// Called when the viewer color profile changes.
    /// </summary>
    protected internal virtual void OnColorProfileChanged() { }

    /// <summary>
    /// Called when language changes.
    /// </summary>
    protected internal virtual void OnLanguageChanged(LanguageChangedEventArgs e) { }

    /// <summary>
    /// Called when cursor moves over viewer (only if subscribed).
    /// </summary>
    protected internal virtual void OnPointerMoved(PointerEventArgs e) { }

    /// <summary>
    /// Called when click on viewer (only if subscribed).
    /// </summary>
    protected internal virtual void OnPointerPressed(PointerEventArgs e) { }

    /// <summary>
    /// Called when selection changes (only if subscribed).
    /// </summary>
    protected internal virtual void OnSelectionChanged(SelectionEventArgs? e) { }

    /// <summary>
    /// Called when animation frame changes (only if subscribed).
    /// </summary>
    protected internal virtual void OnFrameChanged(int frameIndex) { }

    #endregion


    #region Entry point

    /// <summary>
    /// Main entry point. Call from <c>Program.Main(args)</c>.
    /// Connects to host, runs message loop until shutdown.
    /// </summary>
    public async Task RunAsync(string[] args)
    {
        var pipeName = ParsePipeNameFromArgs(args);
        if (string.IsNullOrEmpty(pipeName))
        {
            throw new ArgumentException(
                "Missing --pipe argument. This tool must be launched by ImageGlass.");
        }

        _client = new ToolClient(pipeName, this);
        await _client.ConnectAndRunAsync().ConfigureAwait(false);
    }

    private static string? ParsePipeNameFromArgs(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--pipe", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    #endregion


    public virtual void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }
}
