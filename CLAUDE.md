# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`ImageGlass.SDK` is a single .NET 10 class library (`net10.0`, C# `Preview`) that defines the two public extension contracts for **ImageGlass 10**. It contains *contracts and helper base classes only* — no host implementation, no concrete plugins/tools. ImageGlass is the host; third parties consume this DLL to build extensions. There are no tests in this repo.

The two extension surfaces are independent and must not be conflated:

- **Plugins** (`source/Plugins/`, namespace `ImageGlass.SDK.Plugins`) — *native, in-process* image codecs loaded via `NativeLibrary.Load`. Communication is a hand-rolled **C ABI** (function-pointer tables), not .NET interfaces.
- **Tools** (`source/Tools/`, namespace `ImageGlass.SDK.Tools`) — *out-of-process* external programs the host launches and talks to over a **named pipe** using newline-delimited JSON.

## Build

```pwsh
dotnet build source/ImageGlass.SDK.slnx        # or restore/pack as needed
```
Platforms: `x64;ARM64;AnyCPU`. Requires the .NET 10 SDK. Only dependency is `SkiaSharp`. There is no separate lint step and no test project.

## AOT / trimming constraints (applies to ALL changes)

The library sets `IsAotCompatible=true` and `EnableTrimAnalyzer=true` because plugins/tools may publish Native AOT. Consequences you must respect:

- **No runtime-reflection JSON.** All serialization goes through source-generated contexts: `ToolJsonContext` (Tools) and `PluginJsonContext` (Plugins). Any new type that crosses the wire must be added as a `[JsonSerializable(...)]` entry there, or it will fail at runtime in AOT.
- When you must call reflection-y APIs, follow the existing pattern of `[UnconditionalSuppressMessage("AOT"/"Trimming", ...)]` with a justification pointing at the source-gen context (see `Tools/Internal/ToolClient.cs`).

## Plugin ABI architecture (the hard part)

Native codec plugins talk to the host through versioned, `[StructLayout(LayoutKind.Sequential)]` `unsafe struct`s full of `delegate* unmanaged[Cdecl]<...>` pointers. The flow:

1. Host calls the plugin's single C export `ig_plugin_get_api` (`IGNativeAbi.ENTRY_POINT_NAME`), passing the host ABI version and an `IGHostApi*`.
2. Plugin returns an `IGPluginApi*` (identity + `GetCodec`/`Initialize`/`Shutdown`/`SelfTest`).
3. Per codec, `IGCodecApi` exposes `GetCapability`, extension/signature matching, `LoadMetadata`, `DecodeStaticRaster`, animation entry points, and the buffer-freeing callbacks.

Rules baked into the contract — preserve them when editing:

- **ABI versioning:** `IG_PLUGIN_ABI_VERSION` is `MAJOR*1_000_000 + MINOR*1_000 + PATCH`. A **major** bump is a breaking change and the host rejects mismatched plugins. Adding fields to the end of an existing struct is a minor change; reordering/inserting/removing fields is breaking. `StructSize` fields exist so the host can validate layout — keep them.
- **Memory ownership:** whoever allocates frees. The plugin allocates pixel/animation buffers and the host calls back into the plugin's `FreePixelBuffer`/`FreeAnimationInfo`. `FreePixelBuffer` **must be thread-safe** — SkiaSharp may invoke the release delegate from any thread when an `SKImage` is disposed.
- **Animation:** decoded animation frames must be **fully composed RGBA at full canvas size**. The host does not do sub-rect composition or disposal/blend replay — GIF/APNG plugins composite internally.
- **Cancellation:** is an opaque `void*` token; plugins poll `IGHostCoreApi.IsCancellationRequested` and return `IGStatus.Canceled`.

A native plugin ships as a folder with `igplugin.json` (`PluginManifest.FILE_NAME`) describing id/name/`Executable` (the shared lib) and an optional `SupportedExtensions` override.

## Tool IPC architecture

Tools subclass `ToolBase` and call `RunAsync(args)` from `Program.Main`. The host launches the tool process with a `--pipe <name>` argument; `ToolClient` connects the `NamedPipeClientStream` and runs a single read loop.

- **Wire format:** one `ToolMessage` JSON object per line. `WriteIndented` is intentionally `false` in `ToolJsonContext` — indented output would break newline framing. Don't change that.
- **Request/response correlation:** outgoing requests get an incrementing `RequestId`; responses are matched via a `ConcurrentDictionary<int, TaskCompletionSource>`. Messages with no/unknown `RequestId` are treated as fire-and-forget **events** and dispatched without blocking the read loop (so handlers can call back into `HostApi` without deadlocking).
- **Two directions:** host→tool messages map to `ToolBase.OnXxx` lifecycle/event hooks (`OnInitializedAsync`, `OnExecuteAsync`, `OnPhotoChanged`, etc.); tool→host calls go through `IToolHostProxy` (`HostApi`). All message names are the `MessageTypes` constants — add to both `MessageTypes` and the dispatch `switch` when introducing a new message.
- Real-time events (pointer/selection/frame) are opt-in via `IToolHostProxy.SubscribeEventsAsync`.
- Large pixel data uses a **memory-mapped file**, not the pipe (`GetPixelBufferAsync`); single-pixel reads go over the pipe. Tools must release acquired buffers.
- A tool registers with the host via `igconfig.json`; its `ToolId` must match `ToolBase.ToolId`.

## Conventions

- Every source file starts with the MIT license header block.
- Public API is heavily XML-doc'd, including the C signature of each function pointer — keep docs in sync when changing ABI structs.
