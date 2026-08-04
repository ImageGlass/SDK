# ImageGlass Base64 Sample Codec Plugin

A minimal, cross-platform **native codec plugin** for ImageGlass v10 that teaches
the in-process native plugin ABI (`ImageGlass.SDK.Plugins` / `IGNativeAbi`) with
the smallest interesting codec: it adds support for `.b64` files.

| Extension | What it does |
| --------- | ------------ |
| `.b64` (read)  | Reads a base64-encoded image from a text file and decodes it to a raster image |
| `.b64` (write) | PNG-encodes the pixels the host hands over and writes the base64 of that PNG |

A `.b64` file is just a text file holding a base64 string. Two shapes are accepted
on read; writing always emits shape 1:

1. A raw base64 payload – `iVBORw0KGgoAAAANSUhEUgAA...`
2. A data URI – `data:image/png;base64,iVBORw0KGgo...`

Whitespace and newlines anywhere in the payload are ignored. Because the write path
uses PNG, a decode/encode round trip through ImageGlass is lossless.

## What it demonstrates

- Exports the well-known C entry point `ig_plugin_get_api`
- Advertises **one** static-image codec (`plugin.base64.codec`) that both reads and
  writes `.b64`, via separate `DecodeExtensions` / `EncodeExtensions` lists
- Allocates the `IGCodecCapability` itself and returns it by pointer, and sets every
  `StructSize` it owns (`IGPluginApi`, `IGCodecApi`, `IGCodecCapability`)
- Implements `LoadMetadata` and `DecodeStaticRaster`:
  1. `File.ReadAllText` → strip an optional `data:…;base64,` prefix
  2. `Convert.FromBase64String` → original image bytes (PNG/JPEG/WebP/…)
  3. `SKCodec` decodes those bytes straight into a native **32bpp unpremultiplied
     BGRA** buffer (`IGPixelFormat.Bgra8Unorm`)
- Implements `EncodeStaticRaster`: wraps the host's pixels with
  `SKImage.FromPixelCopy`, PNG-encodes, base64s, and writes the text file
- Reads `IGEncodeOptions.Quality` through a `StructSize` bounds check instead of
  assuming the field is present
- Allocates decode pixel buffers with `NativeMemory.Alloc` and releases them in a
  thread-safe `FreePixelBuffer` that only frees pointers it recorded itself, so a
  host-owned encode buffer can never be freed by mistake
- Honors the host-supplied opaque cancellation token at coarse boundaries
- Leaves the animation-decode and multi-frame-encode entry points null, showing that
  each capability quadrant is independently optional

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

Then go the other way: open any JPG or PNG and use **Save as** with the `.b64` type.
Reopening the result renders the same image, proving the round trip.

## Manifest schema

`igplugin.json` is deserialized into `ImageGlass.SDK.Plugins.PluginManifest`.
Required fields: `id`, `name`, `executable`. The `kind` field defaults to
`"Codec"` if omitted. There is **no** extension list in the manifest: the formats
this codec reads and writes are declared in `IGCodecCapability`, and users narrow
those sets in ImageGlass's plugin settings.

## Notes

- Codec selection is priority-based. No built-in codec claims `.b64`, but the
  plugin still advertises `metadataPriority`/`decodePriority`/`encodePriority` of
  200 so it reliably wins selection for that extension.
- Without this plugin, saving as `.b64` fails: the entry exists in ImageGlass's
  Save As list but Magick.NET has no base64 coder. The plugin is what makes it work.
- `Convert.FromBase64String` ignores embedded whitespace, so wrapped/multi-line
  base64 files work without pre-processing.
- The codec reports `IGColorSpace.Srgb`; it does not extract embedded ICC
  profiles (`SupportsColorProfiles = 0`).
