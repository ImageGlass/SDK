/*
ImageGlass.SDK – ImageGlass 10 Plugins Development Kit
Copyright (C) 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org
MIT License
*/
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;

namespace ImageGlass.SDK.Tools;

/// <summary>
/// Manages the named-pipe connection to the ImageGlass host.
/// Sends requests, receives events, and routes messages to tool callbacks.
/// </summary>
internal sealed class ToolClient : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly Lock _writeLock = new();
    private int _nextRequestId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ToolMessage>> _pending = new();
    private readonly ToolBase _tool;
    private readonly CancellationTokenSource _cts = new();

    public IToolHostProxy HostApi { get; }


    internal ToolClient(string pipeName, ToolBase tool)
    {
        _tool = tool;
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        HostApi = new ToolHostProxy(this);
    }



    /// <summary>
    /// Connect to host pipe and run the message loop until shutdown.
    /// </summary>
    public async Task ConnectAndRunAsync()
    {
        _tool.Trace("ToolClient: connecting to pipe...");
        try
        {
            await _pipe.ConnectAsync(5000, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _tool.Trace($"ToolClient: ConnectAsync failed: {ex}");
            throw;
        }
        _tool.Trace($"ToolClient: connected. IsConnected={_pipe.IsConnected}");

        _reader = new StreamReader(_pipe, leaveOpen: true);
        _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await _reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _tool.Trace($"ToolClient: ReadLineAsync threw: {ex}");
                    throw;
                }
                if (line is null)
                {
                    _tool.Trace("ToolClient: pipe closed, exiting loop.");
                    break;
                }

                ToolMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize(line, ToolJsonContext.Default.ToolMessage);
                }
                catch (Exception ex)
                {
                    _tool.Trace($"ToolClient: malformed JSON ({line.Length} chars): {ex.Message}");
                    continue;
                }
                if (msg is null) continue;
                _tool.Trace($"ToolClient: recv Type={msg.Type} RequestId={msg.RequestId}");

                if (msg.RequestId.HasValue && _pending.TryRemove(msg.RequestId.Value, out var tcs))
                {
                    tcs.SetResult(msg);
                }
                else
                {
                    // Fire-and-forget: don't block the read loop so that
                    // event handlers (e.g. OnExecuteAsync) can call HostApi
                    // methods and receive responses without deadlocking.
                    _ = DispatchEventAsync(msg);
                }
            }
        }
        catch (OperationCanceledException) { _tool.Trace("ToolClient: loop cancelled."); }
        _tool.Trace("ToolClient: ConnectAndRunAsync exiting.");
    }


    /// <summary>
    /// Send a request and wait for the correlated response.
    /// </summary>
    internal async Task<ToolMessage> SendRequestAsync(string type, object? payload = null)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<ToolMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        var jsonPayload = payload is null
            ? null
            : (JsonElement?)JsonSerializer.SerializeToElement(payload, payload.GetType(), ToolJsonContext.Default);

        var msg = new ToolMessage { Type = type, RequestId = requestId, Payload = jsonPayload };
        var json = JsonSerializer.Serialize(msg, ToolJsonContext.Default.ToolMessage);

        lock (_writeLock)
        {
            _writer!.WriteLine(json);
        }

        return await tcs.Task.ConfigureAwait(false);
    }


    /// <summary>
    /// Send a one-way event (no response expected).
    /// </summary>
    internal void SendEvent(string type, object? payload = null)
    {
        var jsonPayload = payload is null
            ? null
            : (JsonElement?)JsonSerializer.SerializeToElement(payload, payload.GetType(), ToolJsonContext.Default);

        var msg = new ToolMessage { Type = type, Payload = jsonPayload };
        var json = JsonSerializer.Serialize(msg, ToolJsonContext.Default.ToolMessage);

        lock (_writeLock)
        {
            _writer!.WriteLine(json);
        }
    }


    private async Task DispatchEventAsync(ToolMessage msg)
    {
        _tool.Trace($"Dispatch enter: {msg.Type}");
        try
        {
            switch (msg.Type)
            {
                case MessageTypes.INIT:
                    var init = Deserialize<ToolInitPayload>(msg.Payload);
                    if (init is not null)
                    {
                        _tool.DataDirectory = init.DataDirectory;
                        _tool.CurrentTheme = init.ThemeInfo;
                    }
                    await _tool.OnInitializedAsync().ConfigureAwait(false);
                    break;

                case MessageTypes.PHOTO_CHANGED:
                    var photo = Deserialize<PhotoChangedEventArgs>(msg.Payload);
                    _tool.OnPhotoChanged(photo ?? new PhotoChangedEventArgs());
                    break;

                case MessageTypes.THEME_CHANGED:
                    var theme = Deserialize<ThemeInfo>(msg.Payload);
                    if (theme is not null) _tool.CurrentTheme = theme;
                    _tool.OnThemeChanged(theme ?? new ThemeInfo());
                    break;

                case MessageTypes.COLOR_PROFILE_CHANGED:
                    _tool.OnColorProfileChanged();
                    break;

                case MessageTypes.LANGUAGE_CHANGED:
                    var lang = Deserialize<LanguageChangedEventArgs>(msg.Payload);
                    _tool.OnLanguageChanged(lang ?? new LanguageChangedEventArgs());
                    break;

                case MessageTypes.POINTER_MOVED:
                    var ptrMoved = Deserialize<PointerEventArgs>(msg.Payload);
                    if (ptrMoved is not null) _tool.OnPointerMoved(ptrMoved);
                    break;

                case MessageTypes.POINTER_PRESSED:
                    var ptrPressed = Deserialize<PointerEventArgs>(msg.Payload);
                    if (ptrPressed is not null) _tool.OnPointerPressed(ptrPressed);
                    break;

                case MessageTypes.SELECTION_CHANGED:
                    var sel = Deserialize<SelectionEventArgs>(msg.Payload);
                    _tool.OnSelectionChanged(sel);
                    break;

                case MessageTypes.FRAME_CHANGED:
                    var frame = Deserialize<FrameChangedPayload>(msg.Payload);
                    _tool.OnFrameChanged(frame?.FrameIndex ?? 0);
                    break;

                case MessageTypes.EXECUTE:
                    await _tool.OnExecuteAsync(_cts.Token).ConfigureAwait(false);
                    break;

                case MessageTypes.SHUTDOWN:
                    await _tool.OnShutdownAsync().ConfigureAwait(false);
                    _cts.Cancel();
                    break;
            }
        }
        catch (Exception ex)
        {
            _tool.Trace($"Dispatch FAILED for {msg.Type}: {ex}");
            try
            {
                Console.Error.WriteLine($"[{_tool.ToolId}] {msg.Type} failed: {ex}");
            }
            catch { }
        }
        _tool.Trace($"Dispatch exit: {msg.Type}");
    }


    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Serializer uses source-generated ToolJsonContext options")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Serializer uses source-generated ToolJsonContext options")]
    private static T? Deserialize<T>(JsonElement? element) where T : class
    {
        if (element is null) return null;
        return element.Value.Deserialize<T>(ToolJsonContext.Default.Options);
    }


    public void Dispose()
    {
        _cts.Cancel();
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _pipe.Dispose(); } catch { }
        _cts.Dispose();
    }

}
