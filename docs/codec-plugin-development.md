# Building a Native Codec Plugin

This guide walks you through building a **native codec plugin** for ImageGlass 10 — an
in-process image decoder that teaches the host how to open a format it doesn't support
yet. We'll build it end to end using the [`Base64Codec`](../samples/Base64Codec/) sample,
which adds support for `.b64` files (text files holding a base64-encoded image).

By the end you'll understand the C ABI, the decode pipeline, memory ownership, and how to
publish and install a plugin.

> **Tool, not plugin?** If you want to *react to the user* (read the pixel under the
> cursor, follow photo navigation, drive the viewer) rather than *decode a new format*,
> you want a **Tool**, not a plugin. See [tool-development.md](tool-development.md).

## Contents

- [How a plugin works](#how-a-plugin-works)
- [Prerequisites](#prerequisites)
- [Step 1 — Create the project](#step-1--create-the-project)
- [Step 2 — Export the entry point](#step-2--export-the-entry-point)
- [Step 3 — Advertise the codec's capabilities](#step-3--advertise-the-codecs-capabilities)
- [Step 4 — Match files by extension](#step-4--match-files-by-extension)
- [Step 5 — Load metadata](#step-5--load-metadata)
- [Step 6 — Decode pixels](#step-6--decode-pixels)
- [Step 7 — Free the buffer (thread-safe!)](#step-7--free-the-buffer-thread-safe)
- [Step 8 — Honor cancellation](#step-8--honor-cancellation)
- [Step 9 — Write the manifest](#step-9--write-the-manifest)
- [Step 10 — Publish as a Native AOT shared library](#step-10--publish-as-a-native-aot-shared-library)
- [Step 11 — Install and test](#step-11--install-and-test)
- [Animated formats](#animated-formats)
- [Rules you must not break](#rules-you-must-not-break)
- [Troubleshooting](#troubleshooting)
- [API reference](#api-reference)


## How a plugin works

A plugin is a **native shared library** (`.dll` / `.so` / `.dylib`) that ImageGlass loads
in-process via `NativeLibrary.Load`. There are no .NET interfaces across the boundary —
the host and plugin talk through a hand-rolled **C ABI**: `[StructLayout(LayoutKind.Sequential)]`
structs full of `delegate* unmanaged[Cdecl]<...>` function pointers.

The handshake is three layers deep:

```
1. Host calls your single C export:
   const IGPluginApi* ig_plugin_get_api(int hostAbiVersion, const IGHostApi* hostApi)

2. You return an IGPluginApi table:
   identity + GetCodec / Initialize / Shutdown / SelfTest

3. For each codec, GetCodec hands back an IGCodecApi table:
   GetCapability, CanHandleExtension, CanHandleSignature,
   LoadMetadata, DecodeStaticRaster, FreePixelBuffer (+ animation entry points)
```

```
   ImageGlass host                         Your plugin (.dll)
   ───────────────                         ──────────────────
   NativeLibrary.Load ───────────────────▶ loads
   ig_plugin_get_api(hostAbi, hostApi) ──▶ returns IGPluginApi*
   pluginApi->GetCodec(0, &codec) ───────▶ returns IGCodecApi*
   codec->CanHandleExtension(".b64") ────▶ returns 1
   codec->LoadMetadata(path, &info) ─────▶ fills IGImageInfo
   codec->DecodeStaticRaster(path, …) ───▶ allocates IGPixelBuffer
   …displays the image…
   codec->FreePixelBuffer(buf) ──────────▶ frees the allocation
```

Because the surface is just C function pointers, a plugin can be written in **any language
that can export a C entry point and produce a native shared library**. This guide uses C#
with Native AOT because the SDK ships the struct definitions for you, but the contract is
language-neutral.


## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The native AOT toolchain for your platform (a C compiler/linker — on Windows the
  "Desktop development with C++" workload; on Linux/macOS `clang` and friends)
- A reference to the `ImageGlass.SDK` package (it provides every `IG*` struct used below)


## Step 1 — Create the project

A codec plugin is a C# project that publishes as a **native shared library**. The critical
properties (`PublishAot`, `NativeLib`, `SelfContained`) turn a normal class library into a
`.dll`/`.so`/`.dylib` with a real C export — see [`Base64Codec.csproj`](../samples/Base64Codec/Base64Codec.csproj):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>Preview</LangVersion>
    <Platforms>x64;ARM64</Platforms>

    <!-- Native shared library produced by Native AOT. -->
    <OutputType>Library</OutputType>
    <PublishAot>true</PublishAot>
    <NativeLib>Shared</NativeLib>
    <SelfContained>true</SelfContained>

    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DisableRuntimeMarshalling>true</DisableRuntimeMarshalling>

    <!-- Resulting binary name MUST match the manifest's "executable" field. -->
    <AssemblyName>Base64Codec</AssemblyName>
    <RootNamespace>Base64Codec</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ImageGlass.SDK" Version="*" />
  </ItemGroup>

  <ItemGroup>
    <!-- Manifest must sit next to the published .dll so the host can discover the plugin. -->
    <None Update="igplugin.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </None>
  </ItemGroup>
</Project>
```

> The sample uses a `<ProjectReference>` to the SDK source because it lives inside this
> repo. In your own plugin use the `<PackageReference Include="ImageGlass.SDK" />` shown
> above instead.

Why each flag matters:

| Property | Why |
| --- | --- |
| `PublishAot` + `NativeLib=Shared` | Emits a native library with a C export instead of a managed assembly. |
| `SelfContained` | Bundles the runtime so the host doesn't need a matching .NET install. |
| `AllowUnsafeBlocks` | The ABI is all pointers and `unsafe` code. |
| `DisableRuntimeMarshalling` | Required so the function-pointer signatures pass blittable structs straight through with no marshalling layer. |
| `AssemblyName` | Becomes the library filename — it **must** match `executable` in the manifest. |

Everything below lives in a single `static unsafe` class. The host never instantiates
anything; it only calls your exported functions.


## Step 2 — Export the entry point

Every plugin exports exactly one C function. Its name is fixed by the SDK as
`IGNativeAbi.ENTRY_POINT_NAME` (`"ig_plugin_get_api"`). Mark it with
`[UnmanagedCallersOnly]` and the Cdecl calling convention so it becomes a real C export:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImageGlass.SDK.Plugins;
using SkiaSharp;

namespace Base64Codec;

internal static unsafe class Base64CodecPlugin
{
    private static IGPluginApi* _pluginApi;
    private static IGCodecApi* _codecApi;
    private static IGHostApi* _hostApi;

    [UnmanagedCallersOnly(EntryPoint = IGNativeAbi.ENTRY_POINT_NAME, CallConvs = [typeof(CallConvCdecl)])]
    public static IGPluginApi* GetApi(int hostAbiVersion, IGHostApi* hostApi)
    {
        // Major-version mismatch: refuse to load. The host rejects a null return.
        if (hostAbiVersion / 1_000_000 != IGNativeAbi.IG_PLUGIN_ABI_MAJOR) return null;
        if (hostApi == null) return null;

        if (_pluginApi != null) return _pluginApi;   // idempotent
        _hostApi = hostApi;                            // stash the host table for later

        InitStrings();      // allocate the UTF-16 string buffers (Step 3)
        InitCodecApi();     // wire up the IGCodecApi function pointers
        InitPluginApi();    // wire up the IGPluginApi function pointers
        return _pluginApi;
    }
}
```

Three things to internalize here:

1. **ABI version check.** `IG_PLUGIN_ABI_VERSION` is encoded as
   `MAJOR * 1_000_000 + MINOR * 1_000 + PATCH`. The host passes *its* version in; if your
   **major** differs, return `null` and the host skips you cleanly. (See
   [Rules you must not break](#rules-you-must-not-break).)

2. **Everything you return must outlive the call.** The host keeps the `IGPluginApi*` and
   `IGCodecApi*` for the entire session. The sample allocates them once with
   `NativeMemory.AllocZeroed` as process-lifetime blocks and never frees them — that's
   correct, not a leak.

3. **Stash `hostApi`.** It's how you log and poll for cancellation later (Steps 7–8).

The plugin table itself is just identity plus four function pointers:

```csharp
private static void InitPluginApi()
{
    _pluginApi = (IGPluginApi*)NativeMemory.AllocZeroed((nuint)sizeof(IGPluginApi));
    _pluginApi->StructSize = sizeof(IGPluginApi);          // lets the host validate layout
    _pluginApi->AbiVersion = IGNativeAbi.IG_PLUGIN_ABI_VERSION;
    _pluginApi->Info = new IGPluginInfo
    {
        PluginId = MakeStringRef(_bufPluginId, PluginIdString.Length),
        Name     = MakeStringRef(_bufPluginName, PluginNameString.Length),
        Version  = MakeStringRef(_bufVersion, VersionString.Length),
        AbiVersion = IGNativeAbi.IG_PLUGIN_ABI_VERSION,
        CodecCount = 1,                                     // we ship one codec
    };
    _pluginApi->GetCodec   = &OnGetCodec;
    _pluginApi->Initialize = &OnInitialize;   // optional one-time init, return IGStatus.OK
    _pluginApi->Shutdown   = &OnShutdown;      // optional cleanup at host shutdown
    _pluginApi->SelfTest   = null;             // optional; null = not provided
}

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static IGStatus OnGetCodec(int index, IGCodecApi** outCodecApi)
{
    if (outCodecApi == null) return IGStatus.InvalidArg;
    if (index != 0) { *outCodecApi = null; return IGStatus.InvalidArg; }  // we only have codec 0
    *outCodecApi = _codecApi;
    return IGStatus.OK;
}
```

> **Strings cross the ABI as `IGStringRef`** — a non-owning `(char* Data, int Length)`
> slice of UTF-16. The sample pre-allocates every string it hands to the host into
> process-lifetime native buffers (`InitStrings` / `AllocUtf16`) because those strings must
> stay valid for as long as the host might read them. Don't hand the host a pointer into a
> managed string or a stack buffer.


## Step 3 — Advertise the codec's capabilities

`GetCapability` tells the host what this codec can do and which extensions it owns. The
host uses it both when probing a file and when choosing between competing codecs:

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static IGStatus CodecGetCapability(IGCodecCapability* outCap)
{
    if (outCap == null) return IGStatus.InvalidArg;
    *outCap = new IGCodecCapability
    {
        CodecId   = MakeStringRef(_bufCodecId, CodecIdString.Length),   // "plugin.base64.codec"
        CodecName = MakeStringRef(_bufCodecName, CodecNameString.Length),

        // Higher number wins. No built-in decodes ".b64", so this wins regardless.
        // (Priority > 100 is clamped for built-in-listed extensions; see below.)
        MetadataPriority = 200,
        DecodePriority   = 200,

        SupportsMetadata      = 1,   // we implement LoadMetadata
        SupportsStaticRaster  = 1,   // we implement DecodeStaticRaster
        SupportsColorProfiles = 0,   // we don't extract ICC profiles
        SupportsAnimation     = 0,   // static images only

        ExtensionCount = SupportedExtensions.Length,
        Extensions     = _extArray,  // IGStringRef[] pre-allocated in InitStrings()
    };
    return IGStatus.OK;
}
```

**Codec selection is priority-based, with a guard for built-in formats.** When several
codecs (yours, plus built-ins) report they can handle a file, the host picks the highest
`DecodePriority` (or `MetadataPriority` for metadata loads). For a **brand-new extension**
nobody else handles, any positive priority wins. But if you claim an extension already in the
host's built-in format list, the host **clamps your priority below the built-in ceiling (100)**
so a built-in decoder always wins it, however high you report. Overriding a built-in decoder
is not automatic: the user (or an admin config) must grant that plugin the override-built-ins
trust flag. Aim plugins at formats the built-ins do not already decode.

The capability flags must match reality: if you set `SupportsAnimation = 1` you **must**
provide all three animation function pointers, or the host downgrades the flag to 0.


## Step 4 — Match files by extension

The host asks each codec whether it recognizes a file. There are two probes:

- `CanHandleExtension(IGStringRef ext)` — match by file extension (lowercase, leading dot).
- `CanHandleSignature(byte* sig, int len)` — optional content sniffing (magic bytes). May be `null`.

A `.b64` file is just text with no reliable magic number, so the sample matches by
extension only and leaves `CanHandleSignature` null:

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static int CodecCanHandleExtension(IGStringRef ext)
{
    if (ext.Data == null || ext.Length <= 0) return 0;
    var s = new ReadOnlySpan<char>(ext.Data, ext.Length);
    foreach (var supported in SupportedExtensions)   // [".b64"]
    {
        if (s.Equals(supported, StringComparison.OrdinalIgnoreCase)) return 1;
    }
    return 0;
}
```

Return `1` for a match, `0` otherwise. If you *can* sniff content (e.g. a format with a
4-byte magic signature), implementing `CanHandleSignature` makes your codec robust to files
with the wrong or missing extension. Wire it in `InitCodecApi`; leave it `null` to fall back
to extension matching:

```csharp
private static void InitCodecApi()
{
    _codecApi = (IGCodecApi*)NativeMemory.AllocZeroed((nuint)sizeof(IGCodecApi));
    _codecApi->GetCapability       = &CodecGetCapability;
    _codecApi->CanHandleExtension  = &CodecCanHandleExtension;
    _codecApi->CanHandleSignature  = null;   // no reliable magic for base64 text
    _codecApi->LoadMetadata        = &CodecLoadMetadata;
    _codecApi->DecodeStaticRaster  = &CodecDecodeStaticRaster;
    _codecApi->FreePixelBuffer     = &CodecFreePixelBuffer;

    // Static-image-only codec: leave the animation entry points null.
    _codecApi->GetAnimationInfo    = null;
    _codecApi->FreeAnimationInfo   = null;
    _codecApi->DecodeAnimationFrame = null;
}
```


## Step 5 — Load metadata

Before decoding pixels the host wants the basics: dimensions, pixel format, alpha, frame
count, color space. `LoadMetadata` fills an `IGImageInfo` without allocating any pixels —
it should be cheap.

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static IGStatus CodecLoadMetadata(IGStringRef filePath, IGImageInfo* outInfo, void* cancellation)
{
    if (outInfo == null) return IGStatus.InvalidArg;
    *outInfo = default;
    // Reuse the shared pipeline with no pixel buffer → metadata-only path.
    return DecodeInternal(filePath, cancellation, outInfo, outBuf: null);
}
```

The sample shares one `DecodeInternal` for both metadata and pixels; when `outBuf` is
`null` it fills the info struct and returns early:

```csharp
outInfo->Width        = w;
outInfo->Height       = h;
outInfo->PixelFormat  = (int)IGPixelFormat.Bgra8Unorm;
outInfo->HasAlpha     = srcInfo.AlphaType == SKAlphaType.Opaque ? 0 : 1;
outInfo->HdrTransferFn = (int)IGHdrTransferFn.None;   // SDR
outInfo->ColorSpace   = (int)IGColorSpace.Srgb;
outInfo->Orientation  = 1;        // EXIF orientation, 1..8; 0 = unknown
outInfo->FrameCount   = 1;        // >= 1; multi-frame codecs report the real count
outInfo->FileSizeBytes = -1;      // -1 = unknown
outInfo->IccProfileData = null;   // optional raw ICC bytes; null = use ColorSpace
outInfo->IccProfileSize = 0;

if (outBuf == null) return IGStatus.OK;   // metadata-only path is done here
```

Notes:

- **`PixelFormat`** must be one of `IGPixelFormat` (`Bgra8Unorm`, `Rgba8Unorm`,
  `Rgba16Unorm`, `RgbaFloat16`). The sample decodes everything to `Bgra8Unorm`.
- **Color management:** set `ColorSpace` to one of `IGColorSpace`, or — for arbitrary
  profiles like ProPhoto RGB — point `IccProfileData`/`IccProfileSize` at the raw ICC bytes
  and the host builds the color space from them. The plugin keeps ownership; the host reads
  the bytes synchronously inside this call.
- **`FrameCount`** drives whether the host treats this as multi-frame. Report the real count.


## Step 6 — Decode pixels

`DecodeStaticRaster` is the heart of the codec: read the file, produce pixels, hand the
host a buffer **you allocated**.

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static IGStatus CodecDecodeStaticRaster(IGStringRef filePath, int frameIndex,
                                                IGPixelBuffer* outBuf, void* cancellation)
{
    if (outBuf == null) return IGStatus.InvalidArg;
    *outBuf = default;
    if (frameIndex != 0) return IGStatus.InvalidArg;   // .b64 holds a single still image

    IGImageInfo info = default;
    return DecodeInternal(filePath, cancellation, &info, outBuf);
}
```

The interesting part is producing the buffer. The sample lets SkiaSharp decode the embedded
image bytes **straight into a native, host-facing buffer** it allocated with
`NativeMemory.Alloc`:

```csharp
// 1) Read the .b64 text and base64-decode it back to the original image bytes.
var text = File.ReadAllText(managedPath);
byte[] imageBytes = DecodeBase64Payload(text);     // strips an optional data: URI prefix

// 2) Decode those bytes with SkiaSharp.
using var data  = SKData.CreateCopy(imageBytes);
using var codec = SKCodec.Create(data);
if (codec == null) return IGStatus.DecodeFailed;

var srcInfo = codec.Info;
int w = srcInfo.Width, h = srcInfo.Height;
if (w <= 0 || h <= 0) return IGStatus.DecodeFailed;

// 3) Allocate a native BGRA8 (premultiplied) buffer the host will own.
ulong stride = (ulong)w * 4UL;
ulong size   = stride * (ulong)h;
if (size > int.MaxValue) return IGStatus.OutOfMemory;

var pixels = (byte*)NativeMemory.Alloc((nuint)size);
if (pixels == null) return IGStatus.OutOfMemory;

var dstInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
var result  = codec.GetPixels(dstInfo, (nint)pixels);

// IncompleteInput still yields a usable (partially-decoded) image.
if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
{
    NativeMemory.Free(pixels);
    return IGStatus.DecodeFailed;
}

// 4) Describe the buffer for the host.
outBuf->Data           = pixels;
outBuf->Width          = w;
outBuf->Height         = h;
outBuf->Stride         = (int)stride;
outBuf->PixelFormat    = (int)IGPixelFormat.Bgra8Unorm;
outBuf->ReleaseContext = pixels;   // opaque cookie your free callback uses (Step 7)

// 5) Record the allocation so FreePixelBuffer can find it.
lock (_bufLock) { _liveBuffers[(nint)pixels] = (nint)pixels; }
return IGStatus.OK;
```

Key points:

- **You allocate, the host owns until it calls you back.** Fill every field of
  `IGPixelBuffer`. `Stride` must be at least `Width * bytesPerPixel`.
- **`ReleaseContext`** is an opaque cookie the host hands back verbatim to your
  `FreePixelBuffer`. Use it to identify exactly what to free. The sample also keeps a
  `Dictionary<nint, nint>` keyed by the pixel pointer as bookkeeping.
- **Return the right status on failure**, and **free anything you allocated before
  returning a failure** — the host won't call `FreePixelBuffer` for a call that didn't
  return `OK`. The full `IGStatus` set: `OK`, `Unsupported`, `Canceled`, `InvalidArg`,
  `DecodeFailed`, `OutOfMemory`, `Internal`, `NotImplemented`, `IoError`.


## Step 7 — Free the buffer (thread-safe!)

The host calls `FreePixelBuffer` to release a buffer you returned. **This must be
thread-safe.** ImageGlass hands your pixels to SkiaSharp via
`SKImage.FromPixels(..., releaseDelegate, ctx)`, and Skia may invoke the release delegate
**from any thread** when the `SKImage` is disposed.

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static void CodecFreePixelBuffer(IGPixelBuffer* buf)
{
    if (buf == null || buf->Data == null) return;
    nint key = (nint)buf->Data;
    nint pixels;
    lock (_bufLock)
    {
        if (!_liveBuffers.Remove(key, out pixels)) return;   // unknown / double-free guard
    }
    NativeMemory.Free((void*)pixels);
    buf->Data = null;
    buf->ReleaseContext = null;
}
```

`NativeMemory.Free`, `free()`, and `CoTaskMemFree` are all thread-safe; the lock here
protects the bookkeeping dictionary, not the free itself. The double-free guard (remove
from the map first, bail if absent) is cheap insurance.

> **The cardinal memory rule: whoever allocates, frees.** The plugin allocates pixel and
> animation buffers; the host calls back into your `FreePixelBuffer` / `FreeAnimationInfo`
> to release them. Never free a buffer the host gave you, and never expect the host to
> `free()` a pointer with an allocator it doesn't know about — that's why you free your own.


## Step 8 — Honor cancellation

Long decodes receive an **opaque cancellation token** (`void* cancellation`). You can't
inspect it — you poll the host through `IGHostCoreApi.IsCancellationRequested` and bail with
`IGStatus.Canceled` when it returns non-zero. Check it at coarse boundaries (after I/O,
before a big allocation, between frames) — not in a tight per-pixel loop.

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static bool IsCanceled(void* cancellation)
{
    if (cancellation == null || _hostApi == null || _hostApi->Core == null) return false;
    var fn = _hostApi->Core->IsCancellationRequested;
    if (fn == null) return false;
    return fn(cancellation) != 0;
}

// …used in the decode pipeline:
if (IsCanceled(cancellation)) return IGStatus.Canceled;
```

The same `IGHostCoreApi` table gives you a host log channel — handy when a decode misbehaves
in the field:

```csharp
private static void Log(int level, string message)   // 0=trace 1=debug 2=info 3=warn 4=error
{
    if (_hostApi == null || _hostApi->Core == null) return;
    var fn = _hostApi->Core->Log;
    if (fn == null) return;
    fixed (char* pMsg = message)
        fn(level, new IGStringRef { Data = pMsg, Length = message.Length });
}
```


## Step 9 — Write the manifest

A plugin ships as a **folder** containing the native library and an `igplugin.json` manifest
([`PluginManifest`](../source/Plugins/PluginManifest.cs)). The host scans for this file to
discover the plugin — see [`igplugin.json`](../samples/Base64Codec/igplugin.json):

```jsonc
{
  "id": "Plugin_SampleBase64Codec",        // required, unique
  "name": "Base64 Codec (sample)",         // required, shown in menus
  "description": "Decodes a .b64 base64-encoded image via SkiaSharp.",
  "version": "1.0.0",
  "author": "Duong Dieu Phap",
  "website": "https://imageglass.org",
  "kind": "Codec",                         // defaults to "Codec" if omitted
  "executable": "Base64Codec.dll"          // required; the native lib filename
}
```

Required fields are `id`, `name`, and `executable`. A few rules:

- **`executable` must match your `AssemblyName`** and is the filename only, relative to the
  plugin folder (`MyCodec.dll` / `libMyCodec.so` / `MyCodec.dylib`). The host rejects an
  absolute/rooted path, any `..`, or a subfolder, and requires the platform native-lib
  extension: keep the library directly in the plugin folder.
- **Optional `supportedExtensions`** (semicolon-separated, e.g. `".foo;.bar"`) *overrides*
  the extensions your codec reports through `IGCodecCapability`. Omit it to use the
  plugin-reported list. This lets a user widen or restrict a codec's scope without a rebuild.


## Step 10 — Publish as a Native AOT shared library

Publish for each platform/architecture you want to support. AOT publish emits the native
library next to its dependencies:

```pwsh
# Windows x64
dotnet publish samples/Base64Codec/Base64Codec.csproj `
    -c Release -r win-x64 -p:Platform=x64 `
    -o samples/Base64Codec/bin/publish/win-x64
```

```bash
# Linux x64
dotnet publish samples/Base64Codec/Base64Codec.csproj \
    -c Release -r linux-x64 -p:Platform=x64 \
    -o samples/Base64Codec/bin/publish/linux-x64

# macOS Apple Silicon
dotnet publish samples/Base64Codec/Base64Codec.csproj \
    -c Release -r osx-arm64 -p:Platform=ARM64 \
    -o samples/Base64Codec/bin/publish/osx-arm64
```

The output folder contains `Base64Codec.dll` (or `.so`/`.dylib`), the copied
`igplugin.json`, and any native dependency the AOT publish emitted (for this sample,
`libSkiaSharp`).


## Step 11 — Install and test

Copy the **entire published folder** into the `_plugins` directory of ImageGlass's
**config directory**. The config directory depends on your platform:

| Platform | Config directory |
| --- | --- |
| Windows | `%LocalAppData%\ImageGlass_10` |
| Linux | `~/.local/share/ImageGlass_10` |
| macOS | `/Users/<username>/Library/Application Support/ImageGlass_10` |

The plugin folder goes under `_plugins`, and the `igplugin.json` manifest **must** sit in
that folder — e.g. `configdir/_plugins/my_codec/igplugin.json`. For this sample on Windows:

```text
%LocalAppData%\ImageGlass_10\_plugins\Base64Codec\
    igplugin.json           # the manifest — must be here
    Base64Codec.dll
    libSkiaSharp.dll        # the native dependency emitted by AOT publish
```

On next launch ImageGlass scans `_plugins` and discovers the manifest. **A newly installed
plugin does not load automatically:** it appears in **Settings > Plugins** as untrusted and you
must enable it there. Enabling pins the library's SHA-256; only then does the host load the
DLL, call `ig_plugin_get_api`, and register `plugin.base64.codec` for `.b64`. If you later
rebuild or replace the DLL, its hash no longer matches and the host prompts you to re-enable it.

Make a test file from any image and open it:

```pwsh
[Convert]::ToBase64String([IO.File]::ReadAllBytes("photo.png")) `
    | Set-Content -NoNewline test.b64
```

Open `test.b64` in ImageGlass — it renders as the original image. If it doesn't, see
[Troubleshooting](#troubleshooting).


## Animated formats

The sample is static-only, but the ABI fully supports animation. To add it:

1. Set `SupportsAnimation = 1` in `GetCapability`.
2. Provide **all three** animation function pointers in `InitCodecApi` (the host downgrades
   the flag to 0 if any is null):
   - `GetAnimationInfo(path, IGAnimationInfo* out, cancellation)` — fill `FrameCount`,
     `LoopCount` (0 = infinite), and allocate the `Frames` array (one
     `IGAnimationFrameInfo` per frame, with `DurationMs` and `HasAlpha`). The host releases
     it via `FreeAnimationInfo`.
   - `DecodeAnimationFrame(path, frameIndex, IGPixelBuffer* out, cancellation)` — same
     contract as `DecodeStaticRaster`, freed by the same `FreePixelBuffer`.
   - `FreeAnimationInfo(IGAnimationInfo* info)` — release the `Frames` allocation.

> **Critical animation rule: every decoded frame must be a fully composed RGBA image at the
> full canvas size.** The host does **not** do sub-rect composition or disposal/blend
> replay. Codecs whose native frame stream is sub-rect (GIF, APNG) must composite each frame
> against the previous ones *internally* before returning it, honoring the format's disposal
> rules. The `IGAnimationFrameInfo` struct intentionally has no sub-rect/blend/disposal
> fields — that work is yours.


## Rules you must not break

These are baked into the contract. Violating them is how a plugin "loads but crashes the
host" or "works on my machine but not in production."

- **ABI versioning.** `IG_PLUGIN_ABI_VERSION = MAJOR * 1_000_000 + MINOR * 1_000 + PATCH`.
  The host **rejects** plugins whose **major** version differs. Adding fields to the *end* of
  an existing struct is a minor (backward-compatible) change; **reordering, inserting, or
  removing fields is breaking**. The `StructSize` fields exist so the host can validate
  layout — always set them.
- **Memory ownership.** Whoever allocates frees. You allocate pixel/animation buffers; the
  host calls your `FreePixelBuffer` / `FreeAnimationInfo` to release them.
- **`FreePixelBuffer` must be thread-safe** — Skia may call it from any thread on dispose.
- **Animation frames are fully composed RGBA at full canvas size.** No host-side compositing.
- **Cancellation** is an opaque `void*`; poll `IGHostCoreApi.IsCancellationRequested` and
  return `IGStatus.Canceled`.
- **Strings handed to the host (`IGStringRef`) must stay valid long enough** — capability
  strings and extensions for the plugin's lifetime; the sample pre-allocates them as
  process-lifetime buffers.
- **Free your own allocations on the failure path.** The host only calls `FreePixelBuffer`
  for calls that returned `IGStatus.OK`.
- **Discovery is not trust.** A newly installed plugin does not run until the user enables it
  in Settings > Plugins; enabling pins the library's SHA-256, and changing the file revokes it.


## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| Plugin silently doesn't load | Not yet enabled in **Settings > Plugins** (new plugins load only after you trust them), or the DLL changed since you trusted it (hash mismatch: re-enable), or `ig_plugin_get_api` returned `null` (major ABI mismatch), or `executable` doesn't match the library filename. |
| Plugin loaded once, then never again | It hard-crashed the host during a previous load and was quarantined. Fix the crash, then clear `{ConfigDir}/_plugins/_quarantine/`. |
| Host loads it but the file won't open | `CanHandleExtension` returned 0 for that extension, or a built-in codec out-bid your `DecodePriority`. Raise the priority or check the extension string (lowercase, leading dot). |
| Crash on close / intermittent crash | `FreePixelBuffer` isn't thread-safe, or you freed a buffer you'd already freed. Use the remove-from-map-first guard. |
| Garbled / shifted pixels | Wrong `Stride` or `PixelFormat`. `Stride` must be ≥ `Width * bytesPerPixel` and match the actual buffer layout. |
| Animation flickers / ghosts | Frames aren't fully composed at full canvas size — you're emitting raw sub-rects. Composite internally. |
| Works in `dotnet run`, not when published | You're testing the managed assembly, not the AOT-published native library. Always test the published `.dll`/`.so`/`.dylib`. |

Use the host log channel (`IGHostCoreApi.Log`) liberally during development — it's your
only window into a plugin running inside the host process.


## API reference

Every type below lives in the `ImageGlass.SDK.Plugins` namespace.

**Entry point & versioning**
- [`IGNativeAbi`](../source/Plugins/Native/ABI/IGNativeAbi.cs) — `ENTRY_POINT_NAME`,
  `IG_PLUGIN_ABI_VERSION`, `IG_PLUGIN_ABI_MAJOR`.

**API tables (function-pointer structs)**
- [`IGHostApi`](../source/Plugins/Native/API/IGHostApi.cs) — top-level host table (→ `Core`).
- [`IGHostCoreApi`](../source/Plugins/Native/API/IGHostCoreApi.cs) — `Log`, `Alloc`/`Free`,
  `IsCancellationRequested`, `GetConfigDirectory`.
- [`IGPluginApi`](../source/Plugins/Native/API/IGPluginApi.cs) — `GetCodec`, `Initialize`,
  `Shutdown`, `SelfTest`.
- [`IGCodecApi`](../source/Plugins/Native/API/IGCodecApi.cs) — `GetCapability`,
  `CanHandleExtension`, `CanHandleSignature`, `LoadMetadata`, `DecodeStaticRaster`,
  `FreePixelBuffer`, and the animation trio.

**Data structs & enums**
- [`IGCodecCapability`](../source/Plugins/Native/ABI/IGCodecCapability.cs)
- [`IGImageInfo`](../source/Plugins/Native/ABI/IGImageInfo.cs)
- [`IGPixelBuffer`](../source/Plugins/Native/ABI/IGPixelBuffer.cs)
- [`IGAnimationInfo`](../source/Plugins/Native/ABI/IGAnimationInfo.cs) /
  [`IGAnimationFrameInfo`](../source/Plugins/Native/ABI/IGAnimationFrameInfo.cs)
- [`IGStringRef`](../source/Plugins/Native/ABI/IGStringRef.cs)
- [`IGStatus`](../source/Plugins/Native/ABI/IGStatus.cs),
  [`IGPixelFormat`](../source/Plugins/Native/ABI/IGPixelFormat.cs),
  [`IGColorSpace`](../source/Plugins/Native/ABI/IGColorSpace.cs),
  [`IGHdrTransferFn`](../source/Plugins/Native/ABI/IGHdrTransferFn.cs),
  [`IGPluginKind`](../source/Plugins/Native/ABI/IGPluginKind.cs)

**Manifest**
- [`PluginManifest`](../source/Plugins/PluginManifest.cs) — the `igplugin.json` schema.

**Full sample:** [`samples/Base64Codec`](../samples/Base64Codec/)
