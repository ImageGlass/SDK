# ImageGlass SDK

[![Nuget](https://img.shields.io/nuget/dt/ImageGlass.SDK?color=%2300a8d6&logo=nuget)](https://www.nuget.org/packages/ImageGlass.SDK)


The official development kit for extending **[ImageGlass](https://imageglass.org) 10**. `ImageGlass.SDK` is a single .NET 10 class library that defines the public contracts for the two ways you can extend ImageGlass.


## What you can build

The two extension surfaces are independent – pick the one that matches what you want to build. Each has a step-by-step guide that builds a real sample from scratch.

| You want to… | Build… | How it runs | Step-by-step guide |
| --- | --- | --- | --- |
| Add support for an **image format ImageGlass can't open** yet (a new or proprietary codec) | **Plugin** | Native, in-process codec loaded through a versioned C ABI | [Building a Native Codec Plugin](docs/codec-plugin-development.md) → covers the C ABI, decode pipeline, memory ownership, animation, and AOT publishing (builds [Base64Codec](samples/Base64Codec/)) |
| **Add a feature** that reacts to the user – read pixels under the cursor, inspect the current photo, drive the viewer, run host commands | **Tool** | Out-of-process program ImageGlass launches and drives over a named pipe | [Building an External Tool](docs/tool-development.md) → covers lifecycle hooks, the `HostApi`, real-time events, reading pixels, and registration (builds [ConsoleColorPicker](samples/ConsoleColorPicker/)) |

A few concrete examples of what the SDK makes possible:

- A codec that decodes a custom or rare image format and renders it like any built-in format – still or animated.
- A color picker that reports the RGBA value under the user's click.
- A batch/automation tool that watches photo navigation and runs actions against the current image, selection, or theme.

See [Samples](#samples) for working, end-to-end examples, and the guides above for the full walkthroughs. The sections further down are a quick orientation.


## 🫟 Community plugins and tools showcase
Explore or download existing extensions built by the community:
| Showcase | Description |
| -------- | --------- |
| [ImageGlass Plugins](https://github.com/ImageGlass/SDK/blob/main/showcase/plugins.md) | Native plugins to extend ImageGlass capabilities. |
| [ImageGlass Tools](https://github.com/ImageGlass/SDK/blob/main/showcase/tools.md) | External applications integrated with ImageGlass features. |


## Installation

Install the package from [NuGet](https://www.nuget.org/packages/ImageGlass.SDK):

```pwsh
dotnet add package ImageGlass.SDK
```

The library targets `net10.0` and depends only on [SkiaSharp](https://github.com/mono/SkiaSharp). It is AOT- and trim-compatible, so your plugin or tool can publish with Native AOT.


## Building this repository

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```pwsh
dotnet build source/ImageGlass.SDK.slnx
```

Supported platforms: `x64`, `ARM64`, `AnyCPU`.


## Samples

The [`samples/`](samples/) folder contains complete, runnable projects – the fastest way to see each extension surface end to end. Each sample has its own README with build, install, and registration steps.

| Sample | Surface | What it shows |
| --- | --- | --- |
| [Base64Codec](samples/Base64Codec/) | Plugin (codec) | A tiny cross-platform native codec that adds `.b64` support – reads a base64-encoded image, decodes it with SkiaSharp into a 32bpp BGRA buffer, and manages pixel-buffer memory through the ABI. Publishes as a Native AOT shared library. |
| [ConsoleColorPicker](samples/ConsoleColorPicker/) | Tool | A console tool that connects over the named pipe, reads metadata of the current photo, follows photo navigation, and logs the RGBA value of the pixel the user clicks in the viewer via opt-in pointer events. |


## 🟢 Building a Codec Plugin

> 📖 **Full walkthrough:** [docs/codec-plugin-development.md](docs/codec-plugin-development.md) builds the [Base64Codec](samples/Base64Codec/) sample step by step. The summary below is just an orientation.

Plugins run **in-process** and communicate with the host through function-pointer tables (a C ABI), so they can be written in any language that can export a C entry point and produce a native shared library.

A plugin ships as a folder containing the native library plus an `igplugin.json` manifest:

```jsonc
{
  "id": "Plugin_MyCodec",
  "name": "My Codec",
  "version": "1.0.0",
  "author": "You",
  "kind": "Codec",
  "executable": "MyCodec.dll",        // MyCodec.so / MyCodec.dylib on other platforms
  "supportedExtensions": ".foo;.bar"   // optional; overrides the plugin-reported list
}
```

The host loads the library and calls the single required export:

```c
const IGPluginApi* ig_plugin_get_api(int hostAbiVersion, const IGHostApi* hostApi);
```

It returns an `IGPluginApi` table (plugin identity + `GetCodec`/`Initialize`/`Shutdown`/`SelfTest`). Each codec then exposes an `IGCodecApi` table: capability reporting, extension/signature matching, `LoadMetadata`, `DecodeStaticRaster`, and optional animation entry points.

Key contracts to honor:

- **ABI version** – encoded as `MAJOR * 1_000_000 + MINOR * 1_000 + PATCH` (`IGNativeAbi.IG_PLUGIN_ABI_VERSION`). The host rejects plugins whose **major** version does not match.
- **Memory ownership** – the plugin allocates pixel/animation buffers; the host calls back into the plugin's `FreePixelBuffer` / `FreeAnimationInfo` to release them. `FreePixelBuffer` **must be thread-safe** – it may be invoked from any thread when the host disposes the image.
- **Animation** – decoded animation frames must be **fully composed RGBA at full canvas size**. The host does not perform sub-rect composition or disposal/blend replay, so codecs like GIF/APNG must composite internally.
- **Cancellation** – long operations receive an opaque cancellation token; poll `IGHostCoreApi.IsCancellationRequested` and return `IGStatus.Canceled` when set.

### Installing the plugin

After you compile and publish the plugin with Native AOT, copy its **entire output folder**
into the `_plugins` folder of ImageGlass's config directory:

| Platform | Config directory |
| --- | --- |
| Windows | `%LocalAppData%\ImageGlass` |
| Linux | `~/.local/share/ImageGlass` |
| macOS | `/Users/<username>/Library/Application Support/ImageGlass` |

Make sure the `igplugin.json` manifest is located inside that folder, for example:
`configdir/_plugins/my_codec/igplugin.json`. ImageGlass scans `_plugins` on launch,
discovers the manifest, and loads the codec.

## 🟢 Building a Tool

> 📖 **Full walkthrough:** [docs/tool-development.md](docs/tool-development.md) builds the [ConsoleColorPicker](samples/ConsoleColorPicker/) sample step by step. The summary below is just an orientation.

Tools run **out-of-process**. Subclass `ToolBase`, then start it from your program's entry point:

```csharp
using ImageGlass.SDK.Tools;

public sealed class MyTool : ToolBase
{
    public override string ToolId => "MyTool";   // must match igconfig.json

    protected override Task OnExecuteAsync(CancellationToken ct)
    {
        // Triggered by the host. Talk back through HostApi.
        return Task.CompletedTask;
    }

    protected override void OnPhotoChanged(PhotoChangedEventArgs e)
    {
        // React to the user navigating to another photo.
    }
}

public static class Program
{
    public static Task Main(string[] args) => new MyTool().RunAsync(args);
}
```

ImageGlass launches the tool with a `--pipe <name>` argument; `RunAsync` connects to the host and runs the message loop until shutdown.

- **Host → tool** events arrive as `OnXxx` overrides (`OnInitializedAsync`, `OnExecuteAsync`, `OnPhotoChanged`, `OnThemeChanged`, `OnSelectionChanged`, …).
- **Tool → host** calls go through `HostApi` (`IToolHostProxy`): read pixels, get the full pixel buffer (via a memory-mapped file), query photo metadata and the photo list, get/set the selection, run named ImageGlass API methods, and read theme info.
- Real-time pointer/selection/frame events are opt-in via `HostApi.SubscribeEventsAsync`.
- Register the tool with ImageGlass through an `igconfig.json` entry whose `ToolId` matches your `ToolBase.ToolId`.

### Passing the current file to the tool

In the `Arguments` field of the `igconfig.json` entry you can use the `<file>` macro. ImageGlass replaces it with the **full path of the currently viewed image** when it launches the tool:

```jsonc
"Tools": [
  {
    "ToolId": "Tool_MyTool",
    "ToolName": "My Tool",
    "Executable": "C:\\path\\to\\MyTool.exe",
    "Arguments": "--input \"<file>\"",   // expands to: --input "C:\Photos\my image.png"
    "IsIntegrated": true,
    "Hotkeys": ["Alt+1"]
  }
]
```

- **`IsIntegrated`** – `true` makes this an SDK tool: ImageGlass launches the process with the `--pipe <name>` argument and wires up the two-way `HostApi` proxy, so the tool can use `ToolBase`/`HostApi` to talk to the host. Set it `false` (or omit it) for a plain external program that is just launched with its arguments and gets no pipe connection.
- **`Hotkeys`** – an array of key-combination strings that run the tool when pressed in ImageGlass (e.g. `["Alt+1"]`, `["K"]`). Use an empty array `[]` if you don't want a shortcut.

`<file>` expands to the path **without quotes** – wrap it yourself as `"<file>"` when the path may contain spaces. The expanded value arrives in your tool's `args` (the `string[]` passed to `Main` / `RunAsync`).

Set `EnableDebug = true` and provide a `DebugLog` sink before calling `RunAsync` to trace pipe connection and message dispatch when a tool appears to "do nothing".


## License

[MIT](LICENSE) © 2026 Duong Dieu Phap
