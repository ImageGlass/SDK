# Building a Native Codec Plugin

This guide walks you through building a **native codec plugin** for ImageGlass 10 – an
in-process image decoder that teaches the host how to open a format it doesn't support
yet. We'll build it end to end using the [`Base64Codec`](../samples/Base64Codec/) sample,
which adds support for `.b64` files (text files holding a base64-encoded image).

By the end you'll understand the C ABI, the decode pipeline, memory ownership, and how to
publish and install a plugin.

> **Tool, not plugin?** If you want to *react to the user* (read the pixel under the
> cursor, follow photo navigation, drive the viewer) rather than *decode a new format*,
> you want a **Tool**, not a plugin. See [tool-development.md](tool-development.md).

## Contents

- [Building a Native Codec Plugin](#building-a-native-codec-plugin)
  - [Contents](#contents)
  - [How a plugin works](#how-a-plugin-works)
  - [Prerequisites](#prerequisites)
  - [Step 1 – Create the project](#step-1--create-the-project)
  - [Step 2 – Export the entry point](#step-2--export-the-entry-point)
  - [Step 3 – Advertise the codec's capabilities](#step-3--advertise-the-codecs-capabilities)
  - [Step 4 – Match files by extension](#step-4--match-files-by-extension)
  - [Step 5 – Load metadata](#step-5--load-metadata)
  - [Step 6 – Decode pixels](#step-6--decode-pixels)
  - [Step 7 – Free the buffer (thread-safe!)](#step-7--free-the-buffer-thread-safe)
  - [Step 8 – Honor cancellation](#step-8--honor-cancellation)
  - [Step 9 – Write the manifest](#step-9--write-the-manifest)
  - [Step 10 – Publish as a Native AOT shared library](#step-10--publish-as-a-native-aot-shared-library)
  - [Step 11 – Install and test](#step-11--install-and-test)
  - [Step 12 – Package a `.igplugin.zip`](#step-12--package-a-igpluginzip)
  - [Animated formats](#animated-formats)
  - [Encoding](#encoding)
  - [Rules you must not break](#rules-you-must-not-break)
  - [Troubleshooting](#troubleshooting)
  - [API reference](#api-reference)


## How a plugin works

A plugin is a **native shared library** (`.dll` / `.so` / `.dylib`) that ImageGlass loads
in-process via `NativeLibrary.Load`. There are no .NET interfaces across the boundary –
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
   LoadMetadata, DecodeStaticRaster, FreePixelBuffer
   (+ animation decode entry points, + encode entry points)
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

Saving reverses the flow: the host resolves an encoder the same priority-based way, hands it
pixels plus a destination path, and moves the result into place. See [Encoding](#encoding).

Because the surface is just C function pointers, a plugin can be written in **any language
that can export a C entry point and produce a native shared library**. This guide uses C#
with Native AOT because the SDK ships the struct definitions for you, but the contract is
language-neutral: [`ig_plugin_abi.h`](ig_plugin_abi.h) is the canonical C declaration of every
struct and table, with fixed-width types throughout, and is `bindgen`-friendly for Rust. Note
two traps it spells out that the C# definitions hide: `IGStringRef.Data` is `char16_t*`
(**not** `char*`, since C# `char` is a 16-bit UTF-16 code unit) and `IGImageInfo.FileSizeBytes`
is `int64_t` (**not** `long`, which is 32-bit on Windows LLP64).


## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- The native AOT toolchain for your platform (a C compiler/linker – on Windows the
  "Desktop development with C++" workload; on Linux/macOS `clang` and friends)
- A reference to the `ImageGlass.SDK` package (it provides every `IG*` struct used below)


## Step 1 – Create the project

A codec plugin is a C# project that publishes as a **native shared library**. The critical
properties (`PublishAot`, `NativeLib`, `SelfContained`) turn a normal class library into a
`.dll`/`.so`/`.dylib` with a real C export – see [`Base64Codec.csproj`](../samples/Base64Codec/Base64Codec.csproj):

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
| `AssemblyName` | Becomes the library filename – it **must** match `executable` in the manifest. |

Everything below lives in a single `static unsafe` class. The host never instantiates
anything; it only calls your exported functions.


## Step 2 – Export the entry point

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

        try
        {
            InitStrings();      // allocate the UTF-16 string buffers (Step 3)
            InitCodecApi();     // wire up the IGCodecApi function pointers
            InitPluginApi();    // wire up the IGPluginApi function pointers
        }
        catch
        {
            return null;        // never let an exception cross the boundary
        }
        return _pluginApi;
    }
}
```

Four things to internalize here:

1. **ABI version check.** `IG_PLUGIN_ABI_VERSION` is encoded as
   `MAJOR * 1_000_000 + MINOR * 1_000 + PATCH`. The host passes *its* version in; if your
   **major** differs, return `null` and the host skips you cleanly. (See
   [Rules you must not break](#rules-you-must-not-break).)

2. **Everything you return must outlive the call.** The host keeps the `IGPluginApi*` and
   `IGCodecApi*` for the entire session. The sample allocates them once with
   `NativeMemory.AllocZeroed` as process-lifetime blocks and never frees them – that's
   correct, not a leak.

3. **Stash `hostApi`.** It's how you log and poll for cancellation later (Steps 7–8).

4. **No exception may escape an `[UnmanagedCallersOnly]` method.** This applies to *every*
   entry point you hand the host, not just this one. The frame between you and the host is
   native, so the exception cannot be caught there: on .NET it fails fast and takes
   ImageGlass down with it – no dialog, no log, no chance for the host to blame your plugin.
   Wrap each entry point's body and convert the failure into an `IGStatus`:

   ```csharp
   [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
   private static IGStatus CodecDecodeStaticRaster(IGStringRef filePath, int frameIndex,
       IGPixelBuffer* outBuf, void* cancellation)
   {
       if (outBuf == null) return IGStatus.InvalidArg;
       *outBuf = default;

       IGImageInfo info = default;
       try
       {
           return DecodeInternal(filePath, cancellation, &info, outBuf);
       }
       catch (OutOfMemoryException)
       {
           return IGStatus.OutOfMemory;
       }
       catch (Exception ex)
       {
           Log(4, $"MyCodec: decode failed. {ex}");   // log ex, not ex.Message
           return IGStatus.Internal;
       }
   }
   ```

   Log the whole exception rather than `ex.Message`: the common startup failure is a
   `TypeInitializationException` whose *inner* exception names the real problem (a missing
   native dependency, for instance). For `void` entry points (`FreePixelBuffer`,
   `FreeAnimationInfo`, `Shutdown`) there is no status to return, so swallow – those can run
   on the GC finalizer thread, where there is nobody to report to anyway.

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

> **Strings cross the ABI as `IGStringRef`** – a non-owning `(char* Data, int Length)`
> slice of UTF-16. The sample pre-allocates every string it hands to the host into
> process-lifetime native buffers (`InitStrings` / `AllocUtf16`) because those strings must
> stay valid for as long as the host might read them. Don't hand the host a pointer into a
> managed string or a stack buffer.


## Step 3 – Advertise the codec's capabilities

`GetCapability` tells the host what this codec can do and which extensions it owns. The
host uses it both when probing a file and when choosing between competing codecs.

**You allocate the capability struct and return a pointer to it.** Allocate it once, for the
lifetime of the plugin, rather than per call. Do not fill a host-supplied buffer: the host
cannot tell you how large its allocation is beforehand, so a plugin built against a larger
struct would write past it.

```csharp
// Allocated once at startup and never freed (see InitCapability in the sample).
private static IGCodecCapability* _capability;

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static IGStatus CodecGetCapability(IGCodecCapability** outCap)
{
    if (outCap == null) return IGStatus.InvalidArg;
    *outCap = _capability;
    return IGStatus.OK;
}

private static void InitCapability()
{
    _capability = (IGCodecCapability*)NativeMemory.AllocZeroed((nuint)sizeof(IGCodecCapability));
    _capability->StructSize = sizeof(IGCodecCapability);   // REQUIRED, see below
    _capability->CodecId    = MakeStringRef(_bufCodecId, CodecIdString.Length);
    _capability->CodecName  = MakeStringRef(_bufCodecName, CodecNameString.Length);

    // Higher number wins. No built-in handles ".b64", so this wins regardless.
    _capability->MetadataPriority = 200;
    _capability->DecodePriority   = 200;
    _capability->EncodePriority   = 200;

    _capability->SupportsMetadata             = 1;   // we implement LoadMetadata
    _capability->SupportsColorProfiles        = 0;   // we don't extract ICC profiles
    _capability->SupportsStaticRasterDecoding = 1;   // we implement DecodeStaticRaster
    _capability->SupportsAnimationDecoding    = 0;   // not a timeline format
    _capability->SupportsStaticRasterEncoding = 1;   // we implement EncodeStaticRaster
    _capability->SupportsMultiFrameEncoding   = 0;   // no multi-frame session

    _capability->DecodeExtensionCount = DecodeExtensions.Length;
    _capability->DecodeExtensions     = _decExtArray;   // IGStringRef[], plugin lifetime
    _capability->EncodeExtensionCount = EncodeExtensions.Length;
    _capability->EncodeExtensions     = _encExtArray;
}
```

**Always set `StructSize`.** It must equal `sizeof(IGCodecCapability)` as your build sees it.
The host reads no field beyond it and rejects the codec outright if the value is outside the
range it understands. Allocating with a zero-filling allocator (`NativeMemory.AllocZeroed`,
`calloc`) means a forgotten `StructSize` reads as 0 and is refused cleanly instead of being
undefined behavior. The same rule applies to `IGCodecApi.StructSize` and
`IGPluginApi.StructSize`.

**Capabilities are a 2x2, and each quadrant is independent:**

|                   | Decode                        | Encode                         |
|-------------------|-------------------------------|--------------------------------|
| **Static raster** | `SupportsStaticRasterDecoding`| `SupportsStaticRasterEncoding` |
| **Multi-frame**   | `SupportsAnimationDecoding`   | `SupportsMultiFrameEncoding`   |

Implement any subset. Read-only, write-only, and read/write codecs are all valid; a codec
that implements no quadrant is not registered at all. `SupportsMetadata` and
`SupportsColorProfiles` are decode-side only.

**Flags must match reality, and the host verifies:**

- `SupportsAnimationDecoding = 1` requires all three of `GetAnimationInfo`,
  `FreeAnimationInfo`, `DecodeAnimationFrame`.
- `SupportsStaticRasterEncoding = 1` requires `EncodeStaticRaster` **and a non-empty
  `EncodeExtensions` list**. An empty encode list never means "encodes everything".
- `SupportsMultiFrameEncoding = 1` requires all three of `BeginEncodeMultiFrame`,
  `EncodeFrame`, `EndEncodeMultiFrame`.

Any of these unmet and the host silently downgrades the flag to 0, so a partly wired codec
degrades instead of crashing.

**Two extension lists, and they are the only declaration of your formats.** `igplugin.json`
carries no extension list at all: what a codec can read and write is declared here, in code.
The user can narrow either set per plugin in ImageGlass's plugin settings, so ship the full
set you actually support and let them switch off what they do not want.

**Codec selection is priority-based, and trust is the only gate.** When several codecs (yours,
plus built-ins) can handle a file, the host picks the highest `DecodePriority`, or
`MetadataPriority` for metadata loads, or `EncodePriority` when saving. Enabling a plugin is
an explicit act of trust, so an enabled plugin's priority is honored as reported, even for a
format the host has a built-in codec for. Ties go to the codec registered first, and built-ins
register first, so report a strictly higher number to take a format over.

That makes claiming a common extension a real responsibility: at `DecodePriority` 200 you
become the decoder for every such file the user opens. Prefer formats the built-ins do not
handle, and where you do overlap, expect users to untick the extensions they would rather
leave to the built-in codec.


## Step 4 – Match files by extension

The host asks each codec whether it recognizes a file. There are two probes:

- `CanHandleExtension(IGStringRef ext)` – match by file extension (lowercase, leading dot).
- `CanHandleSignature(byte* sig, int len)` – optional content sniffing (magic bytes). May be `null`.

A `.b64` file is just text with no reliable magic number, so the sample matches by
extension only and leaves `CanHandleSignature` null:

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static int CodecCanHandleExtension(IGStringRef ext)
{
    if (ext.Data == null || ext.Length <= 0) return 0;
    var s = new ReadOnlySpan<char>(ext.Data, ext.Length);
    foreach (var supported in DecodeExtensions)   // [".b64"]
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
    _codecApi->StructSize          = sizeof(IGCodecApi);   // REQUIRED
    _codecApi->GetCapability       = &CodecGetCapability;
    _codecApi->CanHandleExtension  = &CodecCanHandleExtension;
    _codecApi->CanHandleSignature  = null;   // no reliable magic for base64 text
    _codecApi->LoadMetadata        = &CodecLoadMetadata;
    _codecApi->DecodeStaticRaster  = &CodecDecodeStaticRaster;
    _codecApi->FreePixelBuffer     = &CodecFreePixelBuffer;

    // Static-image-only codec: leave the animation decode entry points null.
    _codecApi->GetAnimationInfo    = null;
    _codecApi->FreeAnimationInfo   = null;
    _codecApi->DecodeAnimationFrame = null;

    // Single-frame encoding only; no multi-frame session (see "Encoding").
    _codecApi->EncodeStaticRaster     = &CodecEncodeStaticRaster;
    _codecApi->BeginEncodeMultiFrame  = null;
    _codecApi->EncodeFrame            = null;
    _codecApi->EndEncodeMultiFrame    = null;

    // Optional; null means the host decodes full size and downscales itself.
    _codecApi->DecodeStaticRasterScaled = null;
}
```

### Scaled decode (optional, ABI 1.2.0)

Gallery thumbnails and viewer previews ask for a small image, and without
`DecodeStaticRasterScaled` every one of them costs a full-resolution decode plus a
full-resolution buffer the host immediately shrinks. Implement it when the format can decode
below full size cheaply (JPEG DCT scales, an embedded thumbnail, a progressive/pyramidal
container):

```csharp
_codecApi->DecodeStaticRasterScaled = &CodecDecodeStaticRasterScaled;
```

`maxWidth`/`maxHeight` are an upper bound, not an exact size. Return the cheapest result that
fits, preserve the aspect ratio, report the real size in `IGPixelBuffer`, and return a source
already inside the box unscaled. Ownership is identical to `DecodeStaticRaster`: the plugin
allocates, the host frees through `FreePixelBuffer`.

Leaving it null is always safe, and so is returning `IGStatus.Unsupported`; the host falls back
to the full-size path. Older hosts ignore the field entirely, since it lives past the
`StructSize` they know.


## Step 5 – Load metadata

Before decoding pixels the host wants the basics: dimensions, pixel format, alpha, frame
count, color space. `LoadMetadata` fills an `IGImageInfo` without allocating any pixels –
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
- **Color management:** set `ColorSpace` to one of `IGColorSpace`, or – for arbitrary
  profiles like ProPhoto RGB – point `IccProfileData`/`IccProfileSize` at the raw ICC bytes
  and the host builds the color space from them. The plugin keeps ownership; the host reads
  the bytes synchronously inside this call.
- **`FrameCount`** drives whether the host treats this as multi-frame. Report the real count.


## Step 6 – Decode pixels

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

// 3) Allocate a native BGRA8 (straight-alpha) buffer the host will own.
ulong stride = (ulong)w * 4UL;
ulong size   = stride * (ulong)h;
if (size > int.MaxValue) return IGStatus.OutOfMemory;

// NativeMemory.Alloc throws OutOfMemoryException; it never returns null.
var pixels = (byte*)NativeMemory.Alloc((nuint)size);

var dstInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
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
- **`Bgra8Unorm` means straight (unpremultiplied) alpha.** The host maps it to
  `SKAlphaType.Unpremul`, so writing premultiplied bytes under that tag is invisible on
  opaque images and shows as dark halos on everything with alpha.
- **`NativeMemory.Alloc` throws `OutOfMemoryException`** instead of returning null, so a
  `== null` check after it is dead code. Let the entry point's catch map it (Step 2).
- **`ReleaseContext`** is an opaque cookie the host hands back verbatim to your
  `FreePixelBuffer`. Use it to identify exactly what to free. The sample also keeps a
  `Dictionary<nint, nint>` keyed by the pixel pointer as bookkeeping.
- **Return the right status on failure**, and **free anything you allocated before
  returning a failure** – the host won't call `FreePixelBuffer` for a call that didn't
  return `OK`. The full `IGStatus` set: `OK`, `Unsupported`, `Canceled`, `InvalidArg`,
  `DecodeFailed`, `OutOfMemory`, `Internal`, `NotImplemented`, `IoError`.


## Step 7 – Free the buffer (thread-safe!)

The host calls `FreePixelBuffer` to release a buffer you returned. **This must be
thread-safe.** ImageGlass hands your pixels to SkiaSharp via
`SKImage.FromPixels(..., releaseDelegate, ctx)`, and Skia may invoke the release delegate
**from any thread** when the `SKImage` is disposed.

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static void CodecFreePixelBuffer(IGPixelBuffer* buf)
{
    if (buf == null || buf->Data == null) return;

    try
    {
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
    catch { }   // void entry point: nothing to report, and the caller may be the finalizer
}
```

`NativeMemory.Free`, `free()`, and `CoTaskMemFree` are all thread-safe; the lock here
protects the bookkeeping dictionary, not the free itself. The double-free guard (remove
from the map first, bail if absent) is cheap insurance. The `catch` is not optional: the
thread that disposes the `SKImage` can be the GC finalizer thread, and an exception leaving
this method kills the host (Step 2).

> **The cardinal memory rule: whoever allocates, frees.** The plugin allocates pixel and
> animation buffers; the host calls back into your `FreePixelBuffer` / `FreeAnimationInfo`
> to release them. Never free a buffer the host gave you, and never expect the host to
> `free()` a pointer with an allocator it doesn't know about – that's why you free your own.

> **`FreePixelBuffer` is best-effort, not guaranteed for every buffer.** At host shutdown
> (or when the host reloads your plugin) the host may unload your library while images
> backed by your buffers are still alive. In that case the host **skips** the remaining
> `FreePixelBuffer` calls – unloading the library reclaims your buffers with it. So free
> only memory you allocated inside the library. Do **not** rely on `FreePixelBuffer` to run
> for externally-observable cleanup (temp files, sockets, host callbacks); use `Shutdown`
> for that, and note `Shutdown` can be called while buffers are still outstanding.


## Step 8 – Honor cancellation

Long decodes receive an **opaque cancellation token** (`void* cancellation`). You can't
inspect it – you poll the host through `IGHostCoreApi.IsCancellationRequested` and bail with
`IGStatus.Canceled` when it returns non-zero. Check it at coarse boundaries (after I/O,
before a big allocation, between frames) – not in a tight per-pixel loop.

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

The same `IGHostCoreApi` table gives you a host log channel – handy when a decode misbehaves
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


## Step 9 – Write the manifest

A plugin ships as a **folder** containing the native library and an `igplugin.json` manifest
([`PluginManifest`](../source/Plugins/PluginManifest.cs)). The host scans for this file to
discover the plugin – see [`igplugin.json`](../samples/Base64Codec/igplugin.json):

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
- **There is no extension list in the manifest.** The formats a codec reads and writes are
  declared in code, through `IGCodecCapability.DecodeExtensions` / `EncodeExtensions`
  (see [Step 3](#step-3--advertise-the-codecs-capabilities)). Users narrow those sets in
  ImageGlass's plugin settings, which is stored in the app's own config.
- **The manifest is yours, and the host never writes it.** Treat it as immutable identity
  metadata: nothing in ImageGlass edits `igplugin.json`, and users are not expected to.


## Step 10 – Publish as a Native AOT shared library

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

> **Ship the published library, never a `dotnet build` output.** A plain build emits the
> *managed* assembly under `bin/<config>/<tfm>/`, with the same file name and no
> `ig_plugin_get_api` export. The host loads that file successfully, fails to resolve the
> export, and skips the plugin – which looks exactly like "my plugin is ignored". The tell is
> size: an AOT library is megabytes (this sample, ~1.6 MB), a managed assembly is a few KB.

**Native dependencies resolve in the host process, not from your plugin folder.** Your
`DllImport`s are resolved by the OS loader once ImageGlass is running, and its search order
starts at the *ImageGlass executable's* directory – on Windows a copy sitting next to your
library is not found. SkiaSharp works because ImageGlass already ships `libSkiaSharp` beside
its own executable, so you can leave it out of your package (~12 MB saved). If you need a
native dependency the host does not ship, do not rely on the plugin folder being searched:
load it yourself by absolute path and register a `NativeLibrary.SetDllImportResolver`, or link
it statically.


## Step 11 – Install and test

There are two ways in: hand-copy the folder (below), or package a `.igplugin.zip` and let the
user install it from **Settings > Plugins > Add** (see [Step 12](#step-12--package-a-igpluginzip)).

Copy the **entire published folder** into the `_plugins` directory of ImageGlass's
**config directory**. The config directory depends on your platform:

| Platform | Config directory |
| --- | --- |
| Windows | `%LocalAppData%\ImageGlass` |
| Linux | `~/.local/share/ImageGlass` |
| macOS | `/Users/<username>/Library/Application Support/ImageGlass` |

The plugin folder goes under `_plugins`, and the `igplugin.json` manifest **must** sit in
that folder – e.g. `configdir/_plugins/my_codec/igplugin.json`. For this sample on Windows:

```text
%LocalAppData%\ImageGlass\_plugins\Base64Codec\
    igplugin.json           # the manifest – must be here
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

Open `test.b64` in ImageGlass – it renders as the original image. If it doesn't, see
[Troubleshooting](#troubleshooting).


## Step 12 – Package a `.igplugin.zip`

For distribution, zip the published payload as `<name>.igplugin.zip` – the only format
**Settings > Plugins > Add** accepts. `igplugin.json` must sit at the archive root or exactly
one folder below it:

```text
my-codec_win-x64.igplugin.zip
    Plugin_MyCodec/
        igplugin.json
        MyCodec.dll
```

```pwsh
$src = "bin/publish/win-x64"
Compress-Archive -Path "$src/*.dll", "$src/igplugin.json" `
    -DestinationPath my-codec_win-x64.igplugin.zip
```

Leave out anything the host never reads: debug symbols (`.pdb`, `.dbg`, `.dSYM`), the SDK's
`.xml` IntelliSense docs, and any native dependency ImageGlass already ships (see Step 10).
Zip on any platform – ILCompiler produces a `644` shared library, so there is no executable
bit to preserve.

Installing extracts the payload to `_plugins/<plugin id>/`. Trust is keyed by plugin **id**
and survives a reinstall, so re-installing a byte-identical library stays enabled and loads
immediately, while a rebuilt library no longer matches the pinned hash and asks for one
re-consent click.


## Animated formats

The sample is static-only, but the ABI fully supports animation. To add it:

1. Set `SupportsAnimationDecoding = 1` in `GetCapability`.
2. Provide **all three** animation function pointers in `InitCodecApi` (the host downgrades
   the flag to 0 if any is null):
   - `GetAnimationInfo(path, IGAnimationInfo* out, cancellation)` – fill `FrameCount`,
     `LoopCount` (0 = infinite), and allocate the `Frames` array (one
     `IGAnimationFrameInfo` per frame, with `DurationMs` and `HasAlpha`). The host releases
     it via `FreeAnimationInfo`.
   - `DecodeAnimationFrame(path, frameIndex, IGPixelBuffer* out, cancellation)` – same
     contract as `DecodeStaticRaster`, freed by the same `FreePixelBuffer`.
   - `FreeAnimationInfo(IGAnimationInfo* info)` – release the `Frames` allocation.

> **Critical animation rule: every decoded frame must be a fully composed RGBA image.** The
> host does **not** do sub-rect composition or disposal/blend replay. Codecs whose native
> frame stream is sub-rect (GIF, APNG) must composite each frame against the previous ones
> *internally* before returning it, honoring the format's disposal rules. The
> `IGAnimationFrameInfo` struct intentionally has no sub-rect/blend/disposal fields – that
> work is yours.

Frames do **not** have to share one size. The host reads each decoded frame's dimensions
from the `IGPixelBuffer` you return and, when they differ from the previous frame, re-fits
the viewport to that frame: the zoom mode is re-applied, the frame is centered, and the
titlebar dimensions follow it. This lets an animated format act as an image container – one
file holding several unrelated images. Frames of a single fixed-size timeline are the common
case and behave exactly as before.

What still holds: each frame is composed on its own canvas, so a frame *is* its full canvas.
There is no per-frame X/Y placement inside a larger canvas – the ABI carries no offset
fields, and adding them would be a breaking change.

The `Width`/`Height` you report from `LoadMetadata` seed the viewport before the first frame
arrives; reporting frame 0's size there avoids a visible correction on the first frame.

### Animation vs multi-page

Pick the path by how the frames are meant to be consumed:

- **Timeline** (per-frame delays, auto-advance): the animation path above –
  `SupportsAnimationDecoding = 1` plus the three animation entry points.
- **User-stepped container** (pages, unrelated images, no timing): report `FrameCount > 1`
  from `LoadMetadata` and leave `SupportsAnimationDecoding = 0`. The host treats it as a
  multi-page document, steps frames on user command, and decodes each through
  `DecodeStaticRaster`, so per-frame sizing already works there.

Note the asymmetry with encoding: the *decode* side splits these two cases, because a page
container can be stepped through the stateless `DecodeStaticRaster`. The *encode* side does
not, because both cases need you to hold state across frames, so both use the one multi-frame
session and differ only by the `IsAnimated` flag.


## Encoding

A codec can also **write** files. Once you advertise
`SupportsStaticRasterEncoding = 1` plus a non-empty `EncodeExtensions` list, your formats
appear in ImageGlass's Save As dialog and your encoder receives the pixels.

Encoding is fully independent of decoding: a write-only codec is valid (leave every decode
flag at 0), and so is a codec that reads one set of extensions and writes another.

### The destination path is a host temp file

The host does **not** hand you the filename the user picked. It gives you a temp path in the
destination folder, carrying the real target extension last:

```
C:\Users\me\Pictures\.ig-save-7f3c1d9a48b04e1e9c2f5b6a1d0e8c72.b64
```

On success the host moves that file onto the user's chosen name. That is deliberate: `Save`
overwrites the file currently open in the viewer, so if you could truncate the real
destination and then fail, the user's original would be gone. Consequences for you:

- **Create and overwrite exactly the path you are given**, and write it completely.
- **Close your handle before returning, on every path including failure.** The host moves or
  deletes that file immediately afterwards, and on Windows an open handle makes that fail with
  a sharing violation, which turns your error into a confusing one.
- **Do not derive anything from the filename.** The base name is a GUID. Read the extension if
  you write several formats; for the user-visible name and the original's metadata, use
  `IGEncodeOptions.SourceFilePath`.
- Do not delete the temp file on failure. The host owns it and cleans it up.

### The source buffer belongs to the host

`IGPixelBuffer` is used in both directions, with **opposite ownership**. On decode you
allocate it and the host releases it through your `FreePixelBuffer`. On encode the host
allocates it and it dies when your call returns:

- **Never retain it** past the call.
- **Never pass it to `FreePixelBuffer`.** That frees host memory, and it is the single most
  likely bug in an encoder. The host sets `ReleaseContext` to a non-null sentinel it does not
  interpret, so a shared free routine can detect and refuse this case. The sample's
  `FreePixelBuffer` is naturally safe because it only frees pointers it recorded itself.

The host normalizes pixels to `IGPixelFormat.Bgra8Unorm` (BGRA8, **unpremultiplied**) with a
tight `Width * 4` stride. Read `PixelFormat` and return `IGStatus.Unsupported` for anything
you do not handle rather than assuming, so a future host that passes wider formats degrades
instead of writing garbage.

### Static raster encode

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static IGStatus CodecEncodeStaticRaster(IGStringRef destFilePath, IGPixelBuffer* source,
    IGEncodeOptions* options, void* cancellation)
{
    if (destFilePath.Data == null || destFilePath.Length <= 0) return IGStatus.InvalidArg;
    if (source == null || source->Data == null) return IGStatus.InvalidArg;
    if (source->PixelFormat != (int)IGPixelFormat.Bgra8Unorm) return IGStatus.Unsupported;

    try
    {
        var destPath = new string(destFilePath.Data, 0, destFilePath.Length);
        var quality  = ReadQuality(options);

        if (IsCanceled(cancellation)) return IGStatus.Canceled;

        var info = new SKImageInfo(source->Width, source->Height,
            SKColorType.Bgra8888, SKAlphaType.Unpremul);

        // FromPixelCopy copies, so nothing outlives the call.
        using var image = SKImage.FromPixelCopy(info, (nint)source->Data, source->Stride);
        if (image is null) return IGStatus.EncodeFailed;

        using var png = image.Encode(SKEncodedImageFormat.Png, quality);
        if (png is null || png.Size == 0) return IGStatus.EncodeFailed;

        // File.WriteAllText writes and closes in one go.
        File.WriteAllText(destPath, Convert.ToBase64String(png.AsSpan()));
        return IGStatus.OK;
    }
    catch (IOException) { return IGStatus.IoError; }
    catch { return IGStatus.Internal; }
}
```

### Reading `IGEncodeOptions` safely

The host allocates it and sets `StructSize`. Never read past that: a host built against a
smaller struct does not carry the later fields at all.

```csharp
private static int ReadQuality(IGEncodeOptions* options)
{
    // offsetof(Quality) + sizeof(int): the smallest StructSize that includes Quality.
    const int QUALITY_END = sizeof(int) * 2;

    if (options == null || options->StructSize < QUALITY_END) return 100;
    return Math.Clamp(options->Quality, 1, 100);
}
```

| Field | Use |
|---|---|
| `Quality` | 1..100. Ignore it for formats with no quality knob. |
| `Lossless` | Set independently of `Quality`, because "100" means near-lossless in some encoders and truly lossless in others (WEBP, JXL, AVIF). |
| `PreserveAlpha` | 0 lets you pick an opaque palette or chroma subsampling. |
| `SourceFilePath` | The original image, or an empty slice for clipboard/selection content. **This is how metadata survives a save:** you receive raw pixels only, so EXIF/XMP/ICC of the original are not included. Read them from here if your format carries them. |
| `IccProfileData` / `IccProfileSize` | The profile describing the pixels you were handed; null means sRGB. Tag your output with it or colors shift. |

### Multi-frame encode

For formats that hold several images, implement all three session entry points and set
`SupportsMultiFrameEncoding = 1`:

```c
IGStatus BeginEncodeMultiFrame(IGStringRef destFilePath, const IGMultiFrameEncodeInfo* info,
                               const IGEncodeOptions* options, void** outSession, void* cancellation);
IGStatus EncodeFrame(void* session, const IGPixelBuffer* frame,
                     const IGEncodeFrameInfo* frameInfo, void* cancellation);
IGStatus EndEncodeMultiFrame(void* session, int commit, void* cancellation);
```

**One session covers both animated formats and page containers**; branch on
`IGMultiFrameEncodeInfo.IsAnimated`. With `1` the frames form a timeline, so `LoopCount` and
each frame's `DurationMs` are meaningful (animated GIF/WEBP/APNG). With `0` they are an
unordered container (multi-page TIFF, PDF, multi-size ICO), `LoopCount` is ignored, and every
`DurationMs` is 0.

The contract:

- `Begin` returns your session pointer through `outSession`. On `IGStatus.OK` it must be
  non-null and the host calls `End` **exactly once**. On any other status the host does **not**
  call `End`, so release everything before returning.
- `EncodeFrame` is called exactly `info->FrameCount` times, with `FrameIndex` strictly
  increasing from 0 and no gaps.
- **The host holds one frame at a time and cannot re-supply an earlier one.** Its frame cache
  is small and evicted frames are destroyed, so there is no rewind. An encoder needing a
  second pass must buffer internally.
- `End` with `commit = 1` writes your trailer and flushes; with `commit = 0` the save was
  aborted or canceled, so just stop (the host discards the temp file, so do not delete it).
  **Either way, close the file handle and free all session state.** The session pointer is
  dead afterwards.
- The host opens at most one session per codec at a time and issues every call for a session
  **from a single thread**, so session state needs no internal locking. This is the one place
  the ABI gives you thread affinity; `FreePixelBuffer` still does not have it.

### Status codes

| Return | Host behavior |
|---|---|
| `OK` | The temp file is moved onto the user's chosen name. |
| `Unsupported` / `NotImplemented` | Treated as declining: the host falls back to its own built-in encoder for that extension. |
| `EncodeFailed` | The encoder ran but produced nothing valid. Surfaced to the user as a save error. |
| `IoError` | A file-level failure (create, write, close, permissions). Also surfaced as a save error. |
| `Canceled` | The user canceled; the host stays silent rather than showing an error. |

The user's original file is never touched for any of these, so a failure is always safe.


## Rules you must not break

These are baked into the contract. Violating them is how a plugin "loads but crashes the
host" or "works on my machine but not in production."

- **ABI versioning.** `IG_PLUGIN_ABI_VERSION = MAJOR * 1_000_000 + MINOR * 1_000 + PATCH`.
  The host **rejects** plugins whose **major** version differs. **Reordering, inserting, or
  removing fields is breaking.** Appending to the *end* of a struct is only backward-compatible
  where that struct carries a `StructSize` **and the side that allocates it sets it** – that is
  what bounds the reader. Where the allocating side has no `StructSize`
  (`IGAnimationFrameInfo`, whose array you allocate and the host strides), appending is
  breaking too. Always set every `StructSize` you own: `IGPluginApi`, `IGCodecApi`,
  `IGCodecCapability`.
- **The revised v1 contract requires a rebuild.** Encoding support changed `IGCodecApi` and
  `IGCodecCapability` in place (new `StructSize` members, a pointer-returning `GetCapability`,
  renamed capability flags) *without* a version bump. A library built against the older
  contract is refused per codec by the `IGCodecApi.StructSize` check.
- **No exception may escape an `[UnmanagedCallersOnly]` entry point.** The host cannot catch
  it across the native frame; it fails fast and the app dies. Guard every entry point and
  return an `IGStatus` instead (Step 2).
- **Memory ownership.** Whoever allocates frees. You allocate decode pixel buffers and
  animation info; the host calls your `FreePixelBuffer` / `FreeAnimationInfo` to release them.
  **The encode input buffer is the exception: the host owns it.** Never retain it and never
  pass it to `FreePixelBuffer`.
- **Encoders never name their own destination.** Write exactly the temp path you are given,
  completely, and close the handle before returning on every path. The host moves it into place;
  it also owns cleanup, so do not delete it on failure.
- **`FreePixelBuffer` must be thread-safe** – Skia may call it from any thread on dispose.
- **`FreePixelBuffer` is not guaranteed to run for every buffer.** If the host unloads your
  library (shutdown/reload) while buffers are outstanding, it skips the remaining frees; the
  unload reclaims them. Free only in-library memory there – never rely on it for external cleanup.
- **Animation frames are fully composed RGBA.** No host-side compositing, no sub-rect output,
  no disposal/blend replay.
- **Animation frames may differ in size.** The host adopts each frame's own dimensions and
  re-fits the viewport. Uniform sizing is no longer required – but a frame is always its own
  full canvas; there is no placement offset inside a larger one.
- **Cancellation** is an opaque `void*`; poll `IGHostCoreApi.IsCancellationRequested` and
  return `IGStatus.Canceled`.
- **Strings handed to the host (`IGStringRef`) must stay valid long enough** – capability
  strings and extensions for the plugin's lifetime; the sample pre-allocates them as
  process-lifetime buffers.
- **Free your own allocations on the failure path.** The host only calls `FreePixelBuffer`
  for calls that returned `IGStatus.OK`.
- **Discovery is not trust.** A newly installed plugin does not run until the user enables it
  in Settings > Plugins; enabling pins the library's SHA-256, and changing the file revokes it.


## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| Plugin silently doesn't load | Not yet enabled in **Settings > Plugins** (new plugins load only after you trust them), or the DLL changed since you trusted it (hash mismatch: re-enable), or `ig_plugin_get_api` returned `null` (major ABI mismatch), or `executable` doesn't match the library filename. **Settings > Plugins** shows the loader's own reason. |
| Row says "Not loaded" while the toggle is on | The library is trusted but did not load. Hover the status for the loader's message – usually a managed assembly instead of an AOT library (Step 10), a stale contract (`StructSize` mismatch: rebuild), or quarantine. |
| Packaged plugin never loads, no error | You zipped a `dotnet build` output. It has no `ig_plugin_get_api` export; check the size (KB = managed, MB = AOT). See Step 10. |
| ImageGlass dies outright when opening your format | A managed exception escaped one of your entry points – uncatchable across the ABI. Guard them all (Step 2). A missing native dependency surfaces this way, as a `TypeInitializationException` on the first call that touches it. |
| Dark halos / muddy edges on transparent images | Premultiplied pixels tagged `Bgra8Unorm`, which means straight alpha. Decode with `SKAlphaType.Unpremul`. |
| Plugin loaded once, then never again | It hard-crashed the host during a previous load and was quarantined. Fix the crash, then clear `{ConfigDir}/_plugins/_quarantine/`. |
| Host loads it but the file won't open | `CanHandleExtension` returned 0 for that extension, or a built-in codec out-bid your `DecodePriority`. Raise the priority or check the extension string (lowercase, leading dot). |
| Crash on close / intermittent crash | `FreePixelBuffer` isn't thread-safe, or you freed a buffer you'd already freed. Use the remove-from-map-first guard. |
| Garbled / shifted pixels | Wrong `Stride` or `PixelFormat`. `Stride` must be ≥ `Width * bytesPerPixel` and match the actual buffer layout. |
| Animation flickers / ghosts | Frames aren't fully composed – you're emitting raw sub-rects. Composite internally. |
| Frames of differing sizes render pinned top-left, with empty space or cropped | An ImageGlass build older than per-frame viewport re-fitting. Update the host; no plugin rebuild is needed. |
| Works in `dotnet run`, not when published | You're testing the managed assembly, not the AOT-published native library. Always test the published `.dll`/`.so`/`.dylib`. |

Use the host log channel (`IGHostCoreApi.Log`) liberally during development – it's your
only window into a plugin running inside the host process.


## API reference

Every type below lives in the `ImageGlass.SDK.Plugins` namespace.

**Entry point & versioning**
- [`IGNativeAbi`](../source/Plugins/Native/ABI/IGNativeAbi.cs) – `ENTRY_POINT_NAME`,
  `IG_PLUGIN_ABI_VERSION`, `IG_PLUGIN_ABI_MAJOR`.

**API tables (function-pointer structs)**
- [`IGHostApi`](../source/Plugins/Native/API/IGHostApi.cs) – top-level host table (→ `Core`).
- [`IGHostCoreApi`](../source/Plugins/Native/API/IGHostCoreApi.cs) – `Log`, `Alloc`/`Free`,
  `IsCancellationRequested`, `GetConfigDirectory`.
- [`IGPluginApi`](../source/Plugins/Native/API/IGPluginApi.cs) – `GetCodec`, `Initialize`,
  `Shutdown`, `SelfTest`.
- [`IGCodecApi`](../source/Plugins/Native/API/IGCodecApi.cs) – `StructSize`, `GetCapability`,
  `CanHandleExtension`, `CanHandleSignature`, `LoadMetadata`, `DecodeStaticRaster`,
  `FreePixelBuffer`, the animation decode trio, `EncodeStaticRaster`, the multi-frame
  encode session (`BeginEncodeMultiFrame`, `EncodeFrame`, `EndEncodeMultiFrame`), and the
  optional `DecodeStaticRasterScaled`.

**Data structs & enums**
- [`IGCodecCapability`](../source/Plugins/Native/ABI/IGCodecCapability.cs)
- [`IGImageInfo`](../source/Plugins/Native/ABI/IGImageInfo.cs)
- [`IGPixelBuffer`](../source/Plugins/Native/ABI/IGPixelBuffer.cs)
- [`IGAnimationInfo`](../source/Plugins/Native/ABI/IGAnimationInfo.cs) /
  [`IGAnimationFrameInfo`](../source/Plugins/Native/ABI/IGAnimationFrameInfo.cs)
- [`IGEncodeOptions`](../source/Plugins/Native/ABI/IGEncodeOptions.cs),
  [`IGMultiFrameEncodeInfo`](../source/Plugins/Native/ABI/IGMultiFrameEncodeInfo.cs),
  [`IGEncodeFrameInfo`](../source/Plugins/Native/ABI/IGEncodeFrameInfo.cs)
- [`IGStringRef`](../source/Plugins/Native/ABI/IGStringRef.cs)
- [`IGStatus`](../source/Plugins/Native/ABI/IGStatus.cs),
  [`IGPixelFormat`](../source/Plugins/Native/ABI/IGPixelFormat.cs),
  [`IGColorSpace`](../source/Plugins/Native/ABI/IGColorSpace.cs),
  [`IGHdrTransferFn`](../source/Plugins/Native/ABI/IGHdrTransferFn.cs),
  [`IGPluginKind`](../source/Plugins/Native/ABI/IGPluginKind.cs)

**Manifest**
- [`PluginManifest`](../source/Plugins/PluginManifest.cs) – the `igplugin.json` schema.

**C declaration for non-C# plugins**
- [`ig_plugin_abi.h`](ig_plugin_abi.h) – every struct, enum, and table in fixed-width C.
  Documentation only: nothing in the SDK build compiles it, so it is reviewed against the C#
  definitions whenever those change.

**Full sample:** [`samples/Base64Codec`](../samples/Base64Codec/)
