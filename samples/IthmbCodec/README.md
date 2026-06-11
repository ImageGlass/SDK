# ITHMB Codec for ImageGlass v10

A Native AOT C# codec plugin for [ImageGlass v10](https://imageglass.org) that opens Apple `.ithmb` thumbnail-cache files. Primarily works by locating embedded JPEG payloads inside `.ithmb` files and decoding them via SkiaSharp. Also includes best-effort decoders for legacy raw thumbnail profiles (untested).

Tested with **956 T####.ithmb files** from an iPhone 5 (iOS 7) iPod Photo Cache --- **100% extraction rate**.

---

## Table of Contents

- [How it works](#how-it-works)
- [Install](#install)
- [Build from source](#build-from-source)
- [Architecture](#architecture)
- [Verified devices and formats](#verified-devices-and-formats)
- [Limitations](#limitations)
- [Troubleshooting](#troubleshooting)
- [SDK sample PR](#sdk-sample-pr)
- [License](#license)

---

## How it works

`.ithmb` files (iThumbnail cache) are a proprietary format used by Apple iOS devices to store photo thumbnails. Two broad categories exist:

| Type                                | Description                                                                                                                                                     | Our support                                                                                                                    |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| **T-prefix** (e.g. `T####.ithmb`)   | Contains a single full-resolution photo as an embedded JPEG (JFIF or Exif). These are found in newer iOS device caches (iPhone 5 and later).                    | ✅ **Fully supported** --- the primary path. 956/956 verified.                                                                 |
| **F-prefix** (e.g. `F1019_1.ithmb`) | Older format used by iPods and early iPhones. Contains multiple raw-format thumbnails concatenated together (RGB565, YUV422, YCbCr420). These are uncompressed. | ⚠️ Best-effort decoders exist for 6 known profiles (1007, 1009, 1015, 1019, 1020, 1023). Untested due to lack of sample files. |

### Decode pipeline

1. **Read the file** --- the entire `.ithmb` file is read into memory (typical size: 1--2 MB).
2. **JPEG scan** --- the file is scanned for a JPEG SOI marker (`FF D8`) followed within 128 bytes by either a JFIF or Exif header. If found, the JPEG payload is extracted (SOI -> EOI) and decoded via SkiaSharp.
3. **Raw fallback** --- if no embedded JPEG is found, the first 4 bytes are read as a big-endian integer prefix and checked against `KnownProfiles`. On match, the appropriate raw decoder (RGB565, YUV422, or YCbCr420) is used.
4. **EXIF orientation** --- if the JPEG contains an EXIF APP1 segment with an orientation tag (0x0112), it is parsed and reported to the host. ImageGlass rotates the image accordingly.

### File size guard

Files larger than **100 MB** are rejected before reading to prevent OOM from pathological input.

---

## Install

### Requirements

- [ImageGlass v10](https://imageglass.org) Beta 2 or later (Windows 10/11 64-bit)
- An `.ithmb` file from an iOS device photo cache

### Steps

1. Download `IthmbCodec_win-x64.zip` from the [latest release](https://github.com/B67687/ithmb-codec/releases).
2. Extract the contents to `%LocalAppData%\ImageGlass_10\_plugins\IthmbCodec\`.

   The folder should contain:

   ```
   %LocalAppData%\ImageGlass_10\_plugins\IthmbCodec\
       IthmbCodec.dll        (1.6 MB --- the native plugin)
       libSkiaSharp.dll      (11 MB --- SkiaSharp dependency)
       igplugin.json         (plugin manifest)
   ```

3. Restart ImageGlass v10.
4. Drag any `.ithmb` file into the ImageGlass window.

> **Note:** ImageGlass v10 Beta 2 does not register `.ithmb` in the file-open dialog. Drag-and-drop works. This is a known limitation of the beta.

---

## Build from source

### Windows (release binary)

Requires .NET 10 SDK and Visual Studio 2022 with the "Desktop development with C++" workload (for Native AOT).

```powershell
# Clone SDK dependency (once)
git clone https://github.com/ImageGlass/SDK.git imageglass-sdk --depth 1

# Publish as Native AOT shared library
dotnet publish src/IthmbCodec/IthmbCodec.csproj -c Release -r win-x64
```

Output lands in `src/IthmbCodec/bin/Release/net10.0/win-x64/native/`. To package for distribution:

```powershell
cd src/IthmbCodec
Copy-Item igplugin.json bin/Release/net10.0/win-x64/native/
Compress-Archive -Path bin/Release/net10.0/win-x64/native/* -DestinationPath IthmbCodec_win-x64.zip
```

### Cross-platform

Native AOT cross-compilation is not supported. You must build on each target platform:

| Target      | Command                                  | Output             |
| ----------- | ---------------------------------------- | ------------------ |
| Windows x64 | `dotnet publish -c Release -r win-x64`   | `IthmbCodec.dll`   |
| Windows ARM | `dotnet publish -c Release -r win-arm64` | `IthmbCodec.dll`   |
| Linux x64   | `dotnet publish -c Release -r linux-x64` | `IthmbCodec.so`    |
| macOS ARM   | `dotnet publish -c Release -r osx-arm64` | `IthmbCodec.dylib` |

### Running tests

```bash
dotnet test src/IthmbCodec/test/IthmbCodec.Tests.csproj -c Release
```

Tests cover: RGB565 decode (corner cases, endianness), YUV422/Ycbcr420 neutral chroma, JPEG slice detection (SOI/JFIF/EOI), EXIF orientation parsing (19 tests total).

### Building the SDK dependency

The project references `ImageGlass.SDK` via a `<ProjectReference>`. If you want to build the SDK separately:

```bash
git clone https://github.com/ImageGlass/SDK.git imageglass-sdk --depth 1
```

The `.csproj` expects the SDK at `../../imageglass-sdk/` relative to `src/IthmbCodec/`.

---

## Architecture

### Plugin ABI

The plugin follows the ImageGlass v10 native codec plugin ABI (v1.0.0.0):

```
ig_plugin_get_api() -> IGPluginApi -> GetCodec() -> IGCodecApi
```

- **Single entry point** (`ig_plugin_get_api`) --- the only C export.
- **Double-checked locking** in `GetApi` for thread-safe initialization.
- **Single codec**, single-frame static raster decoder.
- **Memory ownership**: the plugin allocates pixel buffers; the host calls back into `FreePixelBuffer` to release them (thread-safe).

### Key source files

| File                  | Description                                                                                                                      |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| `IthmbCodecPlugin.cs` | Main plugin implementation (~670 lines) --- entry point, codec API, JPEG extraction, raw profile decoders, EXIF parsing, helpers |
| `IthmbCodec.csproj`   | .NET 10 Native AOT project targeting `win-x64`, `win-arm64`, `linux-x64`, `osx-arm64`                                            |
| `igplugin.json`       | Plugin manifest consumed by ImageGlass on startup                                                                                |
| `test/`               | xUnit test project (19 tests) covering RGB565, YUV422, YCbCr420, JPEG extraction, EXIF orientation                               |

### Raw profile definitions

Six legacy profiles are defined based on known iPod/iPhone thumbnail formats:

| Profile | Resolution | Encoding    | Notes                                    |
| ------- | ---------- | ----------- | ---------------------------------------- |
| 1007    | 480×864    | RGB565      | Swapped dimensions                       |
| 1009    | 42×30      | RGB565      | Smallest thumbnail                       |
| 1015    | 130×88     | RGB565      | Slideshow browser                        |
| 1019    | 720×480    | YUV422      | TV-out resolution                        |
| 1020    | 176×220    | RGB565      | Portrait thumbnail                       |
| 1023    | 176×132    | RGB565      | Landscape thumbnail                      |
| 1067    | 720×480    | YCbCr 4:2:0 | iPod Classic 6G / Nano 3G (padded frame) |

### EXIF orientation parsing

The codec parses TIFF IFD0 tag 0x0112 from the JPEG APP1 segment and sets `outInfo->Orientation` (1--8). ImageGlass uses this to auto-rotate the display. Additional EXIF metadata (camera model, GPS, etc.) is preserved in the JPEG bytes and may be extracted independently by the host.

---

## Verified devices and formats

| Device   | iOS version | Files tested    | Result                                 |
| -------- | ----------- | --------------- | -------------------------------------- |
| iPhone 5 | iOS 7       | 956 T####.ithmb | ✅ 100% --- all files yield valid JPEG |

If you test this plugin with a different device or iOS version, please open an issue with sample files (or a link to them).

---

## Limitations

1. **Only T-prefix `.ithmb` files with embedded JPEG** --- this is the primary tested path. Other `.ithmb` variants may not work.
2. **Legacy raw profiles are untested** --- the decoders exist (RGB565, YUV422, YCbCr420) but no sample files were available for verification.
3. **No drag-and-drop in file dialog** --- ImageGlass v10 Beta 2 doesn't register `.ithmb` for the open-file dialog. Use drag-and-drop.
4. **No folder browsing** --- third-party extensions can't be registered for folder navigation in Beta 2.
5. **Single-frame only** --- `.ithmb` files contain a single image per file. No animation/multi-frame support.

---

## Troubleshooting

| Symptom                      | Likely cause                                                                                                     |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| Plugin silently doesn't load | Missing `igplugin.json` in the plugin folder, or `executable` in the manifest doesn't match the `.dll` filename. |
| File won't open              | The `.ithmb` file may not contain an embedded JPEG. Try [ithmb.org](https://ithmb.org) to verify.                |
| Garbled image                | The JPEG extraction found a false positive SOI marker. This is rare but possible with unusual files.             |
| "File too large" error       | The file exceeds the 100 MB size guard. This should never happen for normal iPhone photos.                       |
| Crash on close               | Report as an issue. The `FreePixelBuffer` is thread-safe, but other edge cases may exist.                        |

If the plugin doesn't work for your files, try [ithmb.org](https://ithmb.org) --- a free browser-based `.ithmb` decoder with broader device support. No upload required.

---

## SDK sample PR

This plugin has been submitted as a sample to the [ImageGlass SDK](https://github.com/ImageGlass/SDK) repository:

->️ **[PR #2 --- samples: add IthmbCodec plugin](https://github.com/ImageGlass/SDK/pull/2)**

The standalone repo (`B67687/ithmb-codec`) is the primary development home. The SDK PR is a mirror for reference.

---

## References and Acknowledgments

This implementation draws on the work of several open-source projects that reverse-engineered the `.ithmb` format. Their findings are referenced in the code and documentation.

| Project                                                                             | Author(s)       | What it contributed                                                                                                                                                                                                 | License |
| ----------------------------------------------------------------------------------- | --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------- |
| [**Keith's iPod Photo Reader**](https://github.com/kebwi/Keiths_iPod_Photo_Reader)  | Keith W.        | Original reverse engineering of .ithmb: 13 decode methods, interlaced YUV 4:2:2, 4:2:0, RGB565, RGB32, grayscale. The most comprehensive format reference available. Format documentation reproduced in README.txt. | GPL-2.0 |
| [**ithmbrdr**](https://github.com/cyianor/ithmbrdr)                                 | cyianor         | Go implementation of F1067 planar YCbCr 4:2:0 using correct BT.601 coefficients. Confirmed the "half padded" frame structure for iPod Classic 6G / Nano 3G.                                                         | MIT     |
| [**andrewmalta/ithmb**](https://github.com/andrewmalta/ithmb)                       | Andrew Malta    | Python decoder for F1019 interlaced YUV (720x480) and F1015/F1024/F1036 16-bit RGB. Confirmed the CLCL packed-chroma pixel layout.                                                                                  | MIT     |
| [**ithmb-extractor-F1007**](https://github.com/Gaurav-Phogat/ithmb-extractor-F1007) | Gaurav Phogat   | Python decoder for F1007 RGB565 at 480×864 (iPod nano 7G). Confirmed 5-6-5 bit layout with MSB-replication scaling.                                                                                                 | MIT     |
| [**ImageGlass**](https://github.com/d2phap/ImageGlass)                              | Duong Dieu Phap | Host application. Original PR [#2316](https://github.com/d2phap/ImageGlass/pull/2316) proposed GPL-3.0 ITHMB support in v9; this plugin is a clean-room MIT replacement for the v10 Native AOT plugin ABI.          | GPL-3.0 |
| [**ImageGlass SDK**](https://github.com/ImageGlass/SDK)                             | Duong Dieu Phap | Native codec plugin ABI (`IGPluginApi`, `IGCodecApi`, `IGHostApi` types), `igplugin.json` schema, and the Base64Codec reference sample.                                                                             | MIT     |

### Color conversion references

- The YCbCr → RGB conversion uses the **ITU-R BT.601** matrix (JPEG standard), as documented in [Recommendation ITU-R BT.601-7](https://www.itu.int/rec/R-REC-BT.601).
- The 16-bit RGB565 → RGB888 scaling uses standard **MSB replication** (also used by ffmpeg, libpng, and Skia).

### Additional format references

- [Just Solve the File Format Problem: IThmb](http://justsolve.archiveteam.org/wiki/IThmb) — community wiki documenting known profile prefixes and resolutions.
- [iThmb Format Guide (ithmb.org)](https://ithmb.org/guide) — browser-based decoder with descriptions of encoding variants.

---

## License

MIT --- see [LICENSE](../../LICENSE).
