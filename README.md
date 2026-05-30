# ImageGlass SDK

The official development kit for extending **[ImageGlass](https://imageglass.org) 10**. `ImageGlass.SDK` is a single .NET 10 class library that defines the public contracts for the two ways you can extend ImageGlass:

- **Plugins** — native, in-process **image codecs** that teach ImageGlass to decode new image formats. They are loaded directly into the host process through a versioned C ABI.
- **Tools** — out-of-process **external programs** that ImageGlass launches and drives over a named pipe. Tools observe and manipulate the current photo, selection, viewer, and theme.

The two surfaces are independent — pick the one that matches what you want to build.

## Installation

Add a reference to `ImageGlass.SDK` in your project (the assembly produced by this repository). The library targets `net10.0` and depends only on [SkiaSharp](https://github.com/mono/SkiaSharp). It is AOT- and trim-compatible, so your plugin or tool can publish with Native AOT.

## Building this repository

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```pwsh
dotnet build source/ImageGlass.SDK.slnx
```

Supported platforms: `x64`, `ARM64`, `AnyCPU`.

## Building a Codec Plugin

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

- **ABI version** — encoded as `MAJOR * 1_000_000 + MINOR * 1_000 + PATCH` (`IGNativeAbi.IG_PLUGIN_ABI_VERSION`). The host rejects plugins whose **major** version does not match.
- **Memory ownership** — the plugin allocates pixel/animation buffers; the host calls back into the plugin's `FreePixelBuffer` / `FreeAnimationInfo` to release them. `FreePixelBuffer` **must be thread-safe** — it may be invoked from any thread when the host disposes the image.
- **Animation** — decoded animation frames must be **fully composed RGBA at full canvas size**. The host does not perform sub-rect composition or disposal/blend replay, so codecs like GIF/APNG must composite internally.
- **Cancellation** — long operations receive an opaque cancellation token; poll `IGHostCoreApi.IsCancellationRequested` and return `IGStatus.Canceled` when set.

## Building a Tool

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

Set `EnableDebug = true` and provide a `DebugLog` sink before calling `RunAsync` to trace pipe connection and message dispatch when a tool appears to "do nothing".

## License

[MIT](LICENSE) © 2026 Duong Dieu Phap
