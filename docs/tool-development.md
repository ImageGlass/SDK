# Building an External Tool

This guide walks you through building an **external tool** for ImageGlass 10 — an
out-of-process program the host launches and talks to over a named pipe. A tool reacts to
the user (read the pixel under the cursor, follow photo navigation, inspect the current
photo, drive the viewer, run host commands) instead of teaching the host a new image format.

We'll build it end to end using the [`ConsoleColorPicker`](../samples/ConsoleColorPicker/)
sample: a console app that connects to the host, logs metadata of the current photo, follows
photo navigation, and prints the RGBA value of any pixel the user clicks in the viewer.

> **New format, not a feature?** If you want to *decode an image format ImageGlass can't
> open*, you want a **Plugin**, not a tool — a native in-process codec. See
> [codec-plugin-development.md](codec-plugin-development.md).

## Contents

- [How a tool works](#how-a-tool-works)
- [Prerequisites](#prerequisites)
- [Step 1 — Create the project](#step-1--create-the-project)
- [Step 2 — Subclass ToolBase](#step-2--subclass-toolbase)
- [Step 3 — Start it from Main](#step-3--start-it-from-main)
- [Step 4 — React to lifecycle events](#step-4--react-to-lifecycle-events)
- [Step 5 — Call back into the host](#step-5--call-back-into-the-host)
- [Step 6 — Subscribe to real-time events](#step-6--subscribe-to-real-time-events)
- [Step 7 — Read the pixel under a click](#step-7--read-the-pixel-under-a-click)
- [Step 8 — Register the tool in igconfig.json](#step-8--register-the-tool-in-igconfigjson)
- [Step 9 — Build, run, and debug](#step-9--build-run-and-debug)
- [Working with the full pixel buffer](#working-with-the-full-pixel-buffer)
- [Threading & async rules](#threading--async-rules)
- [Host API reference](#host-api-reference)


## How a tool works

A tool is a normal program (any language can speak the protocol, but the SDK gives you a
ready-made C# base class). ImageGlass launches your executable with a `--pipe <name>`
argument, and the two sides exchange **newline-delimited JSON** over a named pipe:

```
   ImageGlass host                          Your tool (.exe)
   ───────────────                          ────────────────
   launch tool.exe --pipe ig_abc123 ──────▶ process starts
                                            ToolBase.RunAsync(args)
                                            connects the pipe
   {"Type":"INIT", …} ───────────────────▶ OnInitializedAsync()
   {"Type":"EXECUTE"} ────────────────────▶ OnExecuteAsync(ct)
   {"Type":"PHOTO_CHANGED", …} ───────────▶ OnPhotoChanged(e)
                          ◀──────────────── HostApi.GetPhotoMetadataAsync()  (request)
   {"Type":"GET_PHOTO_METADATA", …} ──────▶ (response, matched by RequestId)
   {"Type":"POINTER_PRESSED", …} ─────────▶ OnPointerPressed(e)
                          ◀──────────────── HostApi.ReadPixelAsync(x, y)
   {"Type":"SHUTDOWN"} ───────────────────▶ OnShutdownAsync()
```

Two directions:

- **Host → tool** messages arrive as `OnXxx` overrides on your `ToolBase` subclass
  (`OnInitializedAsync`, `OnExecuteAsync`, `OnPhotoChanged`, `OnPointerPressed`, …).
- **Tool → host** calls go through `HostApi` (an `IToolHostProxy`): read pixels, query photo
  metadata and the photo list, get/set the selection, run named ImageGlass API methods, read
  theme info.

The SDK hides the pipe, the JSON framing, and request/response correlation. You override
event hooks and `await` `HostApi` methods — it reads like ordinary async C#.


## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A reference to the `ImageGlass.SDK` package
- A working ImageGlass 10 install to launch the tool (it **must** be launched by the host —
  it needs the `--pipe` argument)


## Step 1 — Create the project

A tool is just a console executable that references the SDK — see
[`ConsoleColorPicker.csproj`](../samples/ConsoleColorPicker/ConsoleColorPicker.csproj):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>Preview</LangVersion>
    <Platforms>x64;ARM64</Platforms>
    <RootNamespace>ConsoleColorPicker</RootNamespace>
    <AssemblyName>ConsoleColorPicker</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ImageGlass.SDK" Version="*" />
  </ItemGroup>
</Project>
```

> The sample uses a `<ProjectReference>` to the SDK source because it lives in this repo;
> in your own tool use the `<PackageReference>` shown above.

Unlike a plugin, a tool has **no AOT requirements** — it's an ordinary process. (You *can*
publish it with Native AOT if you want a smaller, faster-starting binary; the SDK is
AOT-compatible. It's optional.)


## Step 2 — Subclass ToolBase

All the protocol machinery lives in [`ToolBase`](../source/Tools/ToolBase.cs). You subclass
it, give it a `ToolId`, and override the hooks you care about. Here's the skeleton from
[`ConsoleColorPickerTool.cs`](../samples/ConsoleColorPicker/ConsoleColorPickerTool.cs):

```csharp
using ImageGlass.SDK.Tools;

namespace ConsoleColorPicker;

internal sealed class ConsoleColorPickerTool : ToolBase
{
    // Must match the "ToolId" in igconfig.json (Step 8).
    public override string ToolId => "Tool_ConsoleColorPicker";

    protected override Task OnInitializedAsync() { /* … */ return Task.CompletedTask; }
    protected override Task OnExecuteAsync(CancellationToken ct) { /* … */ return Task.CompletedTask; }
    protected override void OnPhotoChanged(PhotoChangedEventArgs e) { /* … */ }
    protected override void OnPointerPressed(PointerEventArgs e) { /* … */ }
    protected override Task OnShutdownAsync() { /* … */ return Task.CompletedTask; }
}
```

`ToolBase` also exposes a few properties you'll use:

- `HostApi` (`IToolHostProxy`) — your channel back to the host (Step 5).
- `DataDirectory` — a per-tool folder the host assigns for caches/local state.
- `CurrentTheme` — the latest `ThemeInfo`, kept up to date by the host.


## Step 3 — Start it from Main

Your `Main` constructs the tool and calls `RunAsync(args)`. That parses `--pipe <name>` from
the arguments, connects the pipe, and runs the message loop until the host sends `SHUTDOWN`
— see [`Program.cs`](../samples/ConsoleColorPicker/Program.cs):

```csharp
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Log.Init();   // a file logger — see "Where does output go?" below
        Log.Write($"Main() entered. args = [{string.Join(", ", args)}]");

        try
        {
            using var tool = new ConsoleColorPickerTool
            {
                EnableDebug = args.Contains("--debug"),  // trace IPC lifecycle…
                DebugLog    = Log.Write,                 // …to this sink
            };
            await tool.RunAsync(args).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write($"FATAL: {ex}");
            return 1;
        }
    }
}
```

> `RunAsync` throws `ArgumentException` if there's no `--pipe` argument — which is exactly
> what happens if you double-click the exe instead of letting ImageGlass launch it. That's
> expected; the tool is only meaningful as a host-launched child.

**Where does output go?** ImageGlass is a GUI process, so a child launched with
`UseShellExecute=false` inherits no console — `Console.WriteLine` goes nowhere. The sample
therefore writes every line to a **log file** next to the executable
(`ConsoleColorPicker.log`) and, on Windows, also tries `AllocConsole` so you can watch live.
For your own tool, log to a file (or your own UI) — don't rely on stdout being visible.


## Step 4 — React to lifecycle events

The host drives your tool through `OnXxx` hooks. The three lifecycle hooks:

| Hook | When | Notes |
| --- | --- | --- |
| `OnInitializedAsync()` | Once, right after the pipe connects | `DataDirectory` and `CurrentTheme` are populated. Good place to subscribe to events (Step 6). |
| `OnExecuteAsync(ct)` | The user invokes the tool's primary action (hotkey/menu) | This is "the user ran my tool." |
| `OnShutdownAsync()` | The host is disconnecting | Last chance to flush/clean up. |

And the event hooks (no subscription needed) — `OnPhotoChanged`, `OnThemeChanged`,
`OnColorProfileChanged`, `OnLanguageChanged`. Real-time pointer/selection/frame hooks need a
subscription (Step 6).

The sample logs photo metadata both when the tool is run and whenever the user navigates to
a new photo:

```csharp
protected override async Task OnExecuteAsync(CancellationToken ct)
{
    Log.Write("[EXECUTE] User opened the tool.");
    await PrintCurrentPhotoAsync().ConfigureAwait(false);
}

protected override void OnPhotoChanged(PhotoChangedEventArgs e)
{
    // The event itself carries quick info — no host round-trip needed.
    Log.Write("[PHOTO CHANGED]");
    if (string.IsNullOrEmpty(e.FilePath)) { Log.Write("  (no photo loaded)"); return; }

    Log.Write($"  File:   {e.FilePath}");
    Log.Write($"  Size:   {e.Width} x {e.Height} px");
    Log.Write($"  Format: {e.Format ?? "(unknown)"}");
    Log.Write($"  Frames: {e.FrameCount}{(e.CanAnimate ? " (animated)" : "")}");

    // Want richer metadata? Fetch it off the dispatch thread (see Step 5 + Threading).
    _ = Task.Run(async () =>
    {
        try { await PrintCurrentPhotoAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Write($"  Failed to read metadata: {ex}"); }
    });
}
```

Notice the split: `OnPhotoChanged` is a **synchronous, `void` event hook** that already
carries the essentials in its `PhotoChangedEventArgs`. When you want *more* than the event
provides, you make an async host call — and you do it off the dispatch thread (covered next).


## Step 5 — Call back into the host

`HostApi` (an [`IToolHostProxy`](../source/Tools/IToolHostProxy.cs)) is how the tool asks the
host for things. Every method is async and returns a deserialized result. The sample's
metadata printer calls `GetPhotoMetadataAsync`:

```csharp
private async Task PrintCurrentPhotoAsync()
{
    ToolPhotoMetadata? meta = await HostApi.GetPhotoMetadataAsync().ConfigureAwait(false);
    if (meta is null) { Log.Write("  (no photo loaded)"); return; }

    Log.Write($"  File:    {meta.FilePath}");
    Log.Write($"  Size:    {meta.Width} x {meta.Height} px");
    Log.Write($"  Format:  {meta.Format ?? "(unknown)"}");
    Log.Write($"  Frames:  {meta.FrameCount}{(meta.CanAnimate ? " (animated)" : "")}");
    Log.Write($"  Alpha:   {(meta.HasAlpha ? "yes" : "no")}");
    Log.Write($"  Bytes:   {meta.FileSizeInBytes:N0}");
    if (!string.IsNullOrEmpty(meta.ColorProfileName))
        Log.Write($"  Profile: {meta.ColorProfileName}");
}
```

`ToolPhotoMetadata` is rich — dimensions (current *and* original), format, frame count,
alpha, color profile, and a full set of EXIF fields (camera model, exposure, ISO, focal
length, capture date, rating, …). See [`ToolTypes.cs`](../source/Tools/ToolTypes.cs).

What else `HostApi` can do:

| Call | Purpose |
| --- | --- |
| `ReadPixelAsync(x, y)` | Fast single-pixel read at source coordinates (pipe only). |
| `GetPixelBufferAsync(selectionOnly)` | Full pixel buffer via a memory-mapped file (see below). |
| `ReleasePixelBufferAsync(buffer)` | Release a buffer acquired above. |
| `GetPhotoMetadataAsync()` | Metadata of the current photo. |
| `GetPhotoListAsync()` | All photos in the collection + the current index. |
| `GetSourceSizeAsync()` | Source image dimensions. |
| `GetSelectionAsync()` / `SetSelectionAsync(rect)` | Get/set the selection rectangle (`null` clears it). |
| `EnableSelectionAsync(enable)` | Toggle selection mode in the viewer. |
| `RunApiAsync(apiName, argument?)` | Invoke a named ImageGlass API method (drive the host). |
| `GetThemeInfoAsync()` | Current theme (dark mode, accent/bg/fg colors). |
| `SubscribeEventsAsync(subscriptions)` | Opt into real-time events (Step 6). |


## Step 6 — Subscribe to real-time events

Pointer, selection, and frame events are **opt-in** — they fire constantly, so you only get
them after you ask. Subscribe once in `OnInitializedAsync`:

```csharp
protected override async Task OnInitializedAsync()
{
    Log.Write("ConsoleColorPicker — connected to ImageGlass");
    Log.Write($"DataDirectory: {DataDirectory}");

    await HostApi.SubscribeEventsAsync(new ToolEventSubscriptions
    {
        PointerPressed = true,   // we want clicks
        // PointerMoved   = true,
        // SelectionChanged = true,
        // FrameChanged   = true,
    }).ConfigureAwait(false);
}
```

Each flag enables a hook:

| Subscription flag | Fires hook |
| --- | --- |
| `PointerMoved` | `OnPointerMoved(PointerEventArgs e)` |
| `PointerPressed` | `OnPointerPressed(PointerEventArgs e)` |
| `SelectionChanged` | `OnSelectionChanged(SelectionEventArgs? e)` |
| `FrameChanged` | `OnFrameChanged(int frameIndex)` |

Without the matching subscription, these hooks never fire even if you override them.


## Step 7 — Read the pixel under a click

Now the payoff. When the user clicks, `OnPointerPressed` fires with both source-image and
client coordinates. The sample rounds the **source** coordinates and asks the host for the
pixel color:

```csharp
protected override void OnPointerPressed(PointerEventArgs e)
{
    var x = (int)Math.Round(e.SourceX);   // source-image coordinates
    var y = (int)Math.Round(e.SourceY);

    // Event hooks are void — never `await` inside them directly. Hop to a Task
    // so the host call doesn't block the read loop (see Threading & async rules).
    _ = Task.Run(async () =>
    {
        try
        {
            ToolColor color = await HostApi.ReadPixelAsync(x, y).ConfigureAwait(false);
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
            Log.Write($"[CLICK] ({x}, {y})  RGBA = ({color.R}, {color.G}, {color.B}, {color.A})  {hex}");
        }
        catch (Exception ex)
        {
            Log.Write($"[CLICK] Failed to read pixel at ({x}, {y}): {ex}");
        }
    });
}
```

`PointerEventArgs` gives you `SourceX/SourceY` (image pixels — what you want for
`ReadPixelAsync`), `ClientX/ClientY` (viewer coordinates), and `Button`. `ReadPixelAsync`
returns a `ToolColor(R, G, B, A)`. That's the whole color picker.


## Step 8 — Register the tool in igconfig.json

ImageGlass discovers tools through an `igconfig.json` entry under `Tools`. The `ToolId`
**must** match your `ToolBase.ToolId`:

```jsonc
"Tools": [
  {
    "ToolId": "Tool_ConsoleColorPicker",        // must equal ConsoleColorPickerTool.ToolId
    "ToolName": "Console Color Picker",
    "Executable": "C:\\path\\to\\ConsoleColorPicker.exe",
    "Arguments": "",
    "IsIntegrated": true,                       // REQUIRED for SDK tools — see below
    "Hotkeys": [ "K" ]                          // run on pressing K; [] for no shortcut
  }
]
```

Two fields decide whether your tool is wired up at all:

- **`IsIntegrated`** — set it `true`. That's what makes this an *SDK tool*: the host launches
  the process with `--pipe <name>` and wires up the two-way `HostApi` proxy. With it `false`
  (or omitted), ImageGlass treats the entry as a plain external program — it just launches
  the exe with its arguments and gives it **no pipe**, so `ToolBase`/`HostApi` won't work.
- **`Hotkeys`** — an array of key-combination strings (`["Alt+1"]`, `["K"]`). Pressing one
  runs the tool (triggering `OnExecuteAsync`). Use `[]` for no shortcut.

### Passing the current file with the `<file>` macro

In `Arguments` you can use the `<file>` macro. ImageGlass replaces it with the **full path of
the currently viewed image** when it launches the tool:

```jsonc
{
  "ToolId": "Tool_MyTool",
  "Executable": "C:\\path\\to\\MyTool.exe",
  "Arguments": "--input \"<file>\"",   // expands to: --input "C:\Photos\my image.png"
  "IsIntegrated": true,
  "Hotkeys": ["Alt+1"]
}
```

`<file>` expands **without quotes**, so wrap it yourself (`"<file>"`) when the path may
contain spaces. The expanded value arrives in your tool's `args` (the `string[]` passed to
`Main` / `RunAsync`).


## Step 9 — Build, run, and debug

Build the tool:

```pwsh
dotnet build samples/ConsoleColorPicker/ConsoleColorPicker.csproj -c Debug
```

The exe lands in `bin/Debug/net10.0/ConsoleColorPicker.exe`. Point your `igconfig.json`
entry's `Executable` at it, restart ImageGlass, and press the hotkey (`K`) or open a photo.

**When a tool "does nothing," turn on tracing.** Set `EnableDebug = true` and provide a
`DebugLog` sink before calling `RunAsync` — the SDK then logs pipe connection, every received
message, and dispatch enter/exit/failure. This surfaces the usual culprits: a wire-format
mismatch, a swallowed exception in an event handler, or a failed pipe connection.

```csharp
using var tool = new ConsoleColorPickerTool
{
    EnableDebug = args.Contains("--debug"),
    DebugLog    = Log.Write,   // append to a file, or Console.WriteLine
};
```

Common gotchas:

| Symptom | Likely cause |
| --- | --- |
| Tool exits immediately with a fatal error | Launched directly instead of by ImageGlass — no `--pipe` argument. |
| Tool runs but nothing happens on click | You didn't `SubscribeEventsAsync(... PointerPressed = true ...)`. |
| `ToolId` "not found" / tool never launches | `ToolId` in code ≠ `ToolId` in `igconfig.json`, or `IsIntegrated` isn't `true`. |
| No visible output | GUI host gives no console — log to a file (the sample writes `*.log`). |
| Event handler seems to hang the tool | You `await`ed a host call directly inside a `void` event hook — hop to `Task.Run` first. |


## Working with the full pixel buffer

`ReadPixelAsync` is perfect for one pixel, but for whole-image work (histograms, analysis,
exporting) you want the entire buffer. That would be huge over the pipe, so the host shares
it through a **memory-mapped file** instead. `GetPixelBufferAsync` returns a
[`PixelBuffer`](../source/Tools/PixelBuffer.cs) you must dispose:

```csharp
PixelBuffer? buf = await HostApi.GetPixelBufferAsync(selectionOnly: false).ConfigureAwait(false);
if (buf is not null)
{
    try
    {
        // Zero-copy read of the mapped memory…
        ReadOnlySpan<byte> pixels = buf.GetPixels();   // Stride * Height bytes
        // …or wrap it as an SKBitmap (valid only while `buf` is alive):
        using SKBitmap bmp = buf.ToSKBitmap();
        // buf.Width / buf.Height / buf.Stride / buf.ColorType describe the layout.
    }
    finally
    {
        buf.Dispose();   // releases the mapped view
        await HostApi.ReleasePixelBufferAsync(buf).ConfigureAwait(false);   // tells the host to drop the MMF
    }
}
```

Pass `selectionOnly: true` to get just the pixels inside the current selection rectangle.
Always dispose the `PixelBuffer` **and** call `ReleasePixelBufferAsync` so the host can free
the shared file.


## Threading & async rules

The SDK runs a single read loop on the pipe. Understanding how it dispatches keeps you out
of trouble:

- **Lifecycle hooks (`OnInitializedAsync`, `OnExecuteAsync`, `OnShutdownAsync`) are `async
  Task`** — you can `await` host calls in them directly.
- **Event hooks (`OnPhotoChanged`, `OnPointerPressed`, `OnSelectionChanged`, …) are
  synchronous `void`.** Don't `await` a host call inside them on the dispatch path — offload
  to `Task.Run` (as the sample does for both `OnPhotoChanged`'s rich-metadata fetch and
  `OnPointerPressed`'s `ReadPixelAsync`). This keeps the read loop free to deliver the
  response your call is waiting on.
- **Always wrap fire-and-forget work in try/catch.** An unhandled exception escaping an
  `async void`-style continuation can tear down the process. Every `Task.Run` in the sample
  has a `catch` that just logs.
- Request/response correlation is automatic: each `HostApi` call gets an incrementing
  `RequestId` and the matching reply resolves your `Task`. Fire-and-forget host→tool events
  carry no `RequestId` and never block the loop.


## Host API reference

Everything below lives in the `ImageGlass.SDK.Tools` namespace.

**Base class & host proxy**
- [`ToolBase`](../source/Tools/ToolBase.cs) — subclass this; `ToolId`, `HostApi`,
  `DataDirectory`, `CurrentTheme`, `EnableDebug`/`DebugLog`, `RunAsync`, and all `OnXxx` hooks.
- [`IToolHostProxy`](../source/Tools/IToolHostProxy.cs) — the `HostApi` surface (pixels,
  photo info, selection, theming, `RunApiAsync`, `SubscribeEventsAsync`).

**Event args & subscriptions**
- [`ToolEventSubscriptions`](../source/Tools/ToolTypes.cs) — opt into pointer/selection/frame
  events.
- `PhotoChangedEventArgs`, `PointerEventArgs`, `SelectionEventArgs`,
  `LanguageChangedEventArgs`, `ThemeInfo` — all in [`ToolTypes.cs`](../source/Tools/ToolTypes.cs).

**Data types**
- `ToolColor`, `ToolRect` — small value types ([`ToolTypes.cs`](../source/Tools/ToolTypes.cs)).
- `ToolPhotoMetadata`, `ToolPhotoList`, `ToolPhotoListItem` — photo info.
- [`PixelBuffer`](../source/Tools/PixelBuffer.cs) — the memory-mapped full-image buffer.

**Protocol (advanced)**
- [`MessageTypes`](../source/Tools/MessageTypes.cs) — the wire message-name constants.
- [`ToolMessage`](../source/Tools/ToolMessage.cs) — one JSON object per line on the pipe.

**Full sample:** [`samples/ConsoleColorPicker`](../samples/ConsoleColorPicker/)
