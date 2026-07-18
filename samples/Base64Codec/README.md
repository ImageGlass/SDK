# ImageGlass Base64 Sample Codec Plugin

A minimal, cross-platform **native codec plugin** for ImageGlass v10 that teaches
the in-process native plugin ABI (`ImageGlass.SDK.Plugins` / `IGNativeAbi`) with
the smallest interesting codec: it adds support for `.b64` files.

| Extension | What it does |
| --------- | ------------ |
| `.b64`    | Reads a base64-encoded image from a text file and decodes it to a raster image |

A `.b64` file is just a text file holding a base64 string. Two shapes are accepted:

1. A raw base64 payload – `iVBORw0KGgoAAAANSUhEUgAA...`
2. A data URI – `data:image/png;base64,iVBORw0KGgo...`

Whitespace and newlines anywhere in the payload are ignored.

## What it demonstrates

- Exports the well-known C entry point `ig_plugin_get_api`
- Advertises **one** static-image codec (`plugin.base64.codec`) for `.b64`
- Implements `LoadMetadata` and `DecodeStaticRaster`:
  1. `File.ReadAllText` → strip an optional `data:…;base64,` prefix
  2. `Convert.FromBase64String` → original image bytes (PNG/JPEG/WebP/…)
  3. `SKCodec` decodes those bytes straight into a native **32bpp premultiplied
     BGRA** buffer (`IGPixelFormat.Bgra8Unorm`)
- Allocates pixel buffers with `NativeMemory.Alloc` and releases them in a
  thread-safe `FreePixelBuffer`
- Honors the host-supplied opaque cancellation token at coarse boundaries
- Leaves the animation entry points null (static images only)

Unlike a format-specific codec, the heavy lifting is delegated to **SkiaSharp**
(the SDK's only dependency), so the plugin stays tiny and works on any platform
SkiaSharp supports.

## Build (Native AOT shared library)

```powershell
dotnet publish samples/Base64Codec/Base64Codec.csproj `
    -c Release -r win-x64 -p:Platform=x64 `
    -o samples/Base64Codec/bin/publish/win-x64
```

This produces `Base64Codec.dll` next to `igplugin.json`. (Use `-r linux-x64` /
`-r osx-arm64` and the matching `-p:Platform` for other targets.)

## Install

Copy the published folder (containing the DLL, `igplugin.json`, and the native
`libSkiaSharp` asset emitted by the AOT publish) into the host's plugins dir:

```text
%LOCALAPPDATA%\ImageGlass\_plugins\Base64Codec\
    Base64Codec.dll
    igplugin.json
    libSkiaSharp.dll
```

On next launch the host discovers the manifest, loads the DLL, calls
`ig_plugin_get_api`, and registers `plugin.base64.codec` for `.b64`.

## Try it

Create a test file from any image:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("photo.png")) `
    | Set-Content -NoNewline test.b64
```

Open `test.b64` in ImageGlass – it renders as the original image.

## Manifest schema

`igplugin.json` is deserialized into `ImageGlass.SDK.Plugins.PluginManifest`.
Required fields: `id`, `name`, `executable`. The `kind` field defaults to
`"Codec"` if omitted.

## Notes

- Codec selection is priority-based. No built-in codec claims `.b64`, but the
  plugin still advertises `metadataPriority`/`decodePriority` of 200 so it
  reliably wins selection for that extension.
- `Convert.FromBase64String` ignores embedded whitespace, so wrapped/multi-line
  base64 files work without pre-processing.
- The codec reports `IGColorSpace.Srgb`; it does not extract embedded ICC
  profiles (`SupportsColorProfiles = 0`).
