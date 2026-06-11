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

Every known open-source `.ithmb` implementation across the internet (GitHub, Codeberg, GitLab, SourceHut, Bitbucket, Gitee, Launchpad, SourceForge) was surveyed --- a total of **11 projects**. Below is the complete list with license compatibility for this MIT-licensed plugin.

### Directly incorporated (MIT-licensed, compatible)

| Project                                                                        | Author(s) | What it contributed                                                                                                                                         |
| ------------------------------------------------------------------------------ | --------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [**iOpenPod**](https://github.com/TheRealSavi/iOpenPod)                        | Savi      | Most complete modern codec (2026). 50+ format entries covering all iPod generations. Encode + decode for RGB565/UYVY/I420 with geometry resolution.         |
| [**ithmbrdr**](https://github.com/cyianor/ithmbrdr)                            | cyianor   | Go implementation of F1067 planar YCbCr 4:2:0 using correct BT.601 coefficients. Confirmed the "half padded" frame structure for iPod Classic 6G / Nano 3G. |
| [**B67687/ithmb-codec**](https://github.com/B67687/ithmb-codec) (this project) | B67687    | C# Native AOT ImageGlass plugin. JPEG-embedded path (956/956 files). Primary development home.                                                              |

### Used as format reference (clean-room, no code copied)

Format specifications (resolution per format ID, byte layout, encoding types) are factual discoveries from reverse-engineering binary files, not copyrightable creative expression. These projects' documentation and public discussions informed this implementation.

| Project / Source                                                                                                                 | Author(s)          | What it contributed                                                                                                                                                                                                                                                                      |
| -------------------------------------------------------------------------------------------------------------------------------- | ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [**Keith's iPod Photo Reader**](https://github.com/kebwi/Keiths_iPod_Photo_Reader)                                               | Keith Wiley        | Original 2005 reverse engineering. 13 decode methods, the definitive format reference. [iLounge thread](https://web.archive.org/web/20191225184817/https://forums.ilounge.com/threads/hacking-ithmb-file-format.110066/) documents the YUV 4:2:2 interlaced discovery with working code. |
| [**iLounge "Gory Details" thread**](https://web.archive.org/web/20090120040252/http://forums.ilounge.com/showthread.php?t=66435) | jhollington        | Complete per-device format ID table (2005): every iPod/iPhone generation mapped to its ithmb format IDs, resolutions, and byte counts.                                                                                                                                                   |
| [**andrewmalta/ithmb**](https://github.com/andrewmalta/ithmb)                                                                    | Andrew Malta       | Python decoder confirming F1019 CLCL packed-chroma layout and the 5G iPod format variants.                                                                                                                                                                                               |
| [**Gaurav-Phogat/ithmb-extractor-F1007**](https://github.com/Gaurav-Phogat/ithmb-extractor-F1007)                                | Gaurav Phogat      | F1007 RGB565 at 480×864 (iPod nano 7G). Confirmed 5-6-5 bit layout with MSB-replication scaling.                                                                                                                                                                                         |
| [**keyj.emphy.de blog**](https://web.archive.org/web/2024*/https://keyj.emphy.de/an-ipod-hackers-diary/)                         | Jeff Luyten (KeyJ) | ArtworkDB reverse-engineering diary: discovered F1027/F1031 are **mandatory** filenames (not arbitrary), RGB565 byte-swapped artwork format.                                                                                                                                             |
| [**worstje/repear**](https://github.com/worstje/repear)                                                                          | worstje            | Python ArtworkDB writer with complete format→dimension encoder table. Documents model-based format ID assignment.                                                                                                                                                                        |
| [**tbutter/podsyncr**](https://github.com/tbutter/podsyncr)                                                                      | tbutter            | iPod Nano 2G photo syncer (2006). Writes F1023/F1032 .ithmb files with configurable endianness.                                                                                                                                                                                          |
| [**libgpod/gtkpod**](https://github.com/gtkpod/libgpod)                                                                          | gtkpod team        | C library with 22 format variants, RGB565/RGB555/RGB888/UYVY/I420 packers, complete ArtworkDB/PhotoDB parser. 22 years of Linux distribution.                                                                                                                                            |

### Historical references (archived from dead sources)

| Source                                                                                                                                        | Content                                                                                                            |
| --------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| [**iLounge hacking thread**](https://web.archive.org/web/20191225184817/https://forums.ilounge.com/threads/hacking-ithmb-file-format.110066/) | Keith Wiley's original YUV 4:2:2 reverse-engineering with working C++ code. Recovered from Wayback Machine.        |
| [**keyj.emphy.de blog**](https://web.archive.org/web/2024*/https://keyj.emphy.de/an-ipod-hackers-diary/)                                      | ArtworkDB RE diary. Recovered from Wayback Machine.                                                                |
| [**iPodLinux Wiki: ITunesDB**](https://web.archive.org/web/2024*/http://www.ipodlinux.org/ITunesDB/)                                          | Complete iTunesDB/ArtworkDB/PhotoDB binary format specification. Recovered from Wayback Machine.                   |
| **iThmbConv** (lost C source)                                                                                                                 | Windows CLI ithmb decoder (2007-2014). C source code included in RAR, links now dead. Known to support 4G/5G iPod. |

### External tools (not open source)

| Tool                                                            | Platform | Notes                                                                                   |
| --------------------------------------------------------------- | -------- | --------------------------------------------------------------------------------------- |
| [**iThmb Converter**](https://www.ithmbconverter.com/)          | Windows  | Most comprehensive commercial tool. 6 languages, Constructor mode for unknown variants. |
| [**File Juicer**](https://echoone.com/filejuicer/formats/ithmb) | macOS    | Commercial format extractor. Documents F1019/F1023/F3008.                               |
| [**ithmb.org**](https://ithmb.org)                              | Web      | Free browser-based decoder (WASM, offline). Broader device support claimed.             |

### Color conversion references

- The YCbCr → RGB conversion uses the **ITU-R BT.601** matrix (JPEG full-range variant), as documented in [Recommendation ITU-R BT.601-7](https://www.itu.int/rec/R-REC-BT.601).
- The 16-bit RGB565 → RGB888 scaling uses standard **MSB replication** (also used by ffmpeg, libpng, and Skia).

### Additional format references

- [Just Solve the File Format Problem: IThmb](http://justsolve.archiveteam.org/wiki/IThmb) — community wiki documenting known profile prefixes and resolutions.
- [iThmb Format Guide (ithmb.org)](https://ithmb.org/guide) — browser-based decoder with descriptions of encoding variants.

---

## License

MIT --- see [LICENSE](../../LICENSE).
