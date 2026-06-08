# ITHMB Codec for ImageGlass v10

Reads Apple `.ithmb` thumbnail-cache files by locating embedded JPEG payloads and decoding them.

**Tested with:** `T####.ithmb` files from iPhone 5 (iOS 7) iPod Photo Cache — 956/956 verified.

## What works

- `.ithmb` files containing embedded JPEG data (JFIF/Exif markers)
- Recovers **full-resolution original photos** (not just thumbnails)
- Preserves all EXIF metadata (camera model, capture date, GPS, etc.)
- Also includes best-effort legacy raw profile support (iPhone/iPod F-prefix files — untested)

## What doesn't work

- `.ithmb` files without embedded JPEG payloads
- Encrypted or protected content
- Folder browsing in ImageGlass v10 Beta 2 (plugin API file dialog integration pending)

## Install

1. Download `IthmbCodec_win-x64.zip` from the [latest release](https://github.com/B67687/image-glass/releases)
2. Extract to `%LocalAppData%\ImageGlass_10\_plugins\IthmbCodec\`
3. Restart ImageGlass v10
4. Drag any `.ithmb` file into the ImageGlass window

## Build from source

Requires .NET 10 SDK and Visual Studio 2022 with C++ workload (for Native AOT).

```powershell
git clone https://github.com/ImageGlass/SDK.git imageglass-sdk --depth 1
dotnet publish src/IthmbCodec/IthmbCodec.csproj -c Release -r win-x64
```

## If this plugin doesn't work for your files

Try [ithmb.org](https://ithmb.org) — a free browser-based `.ithmb` decoder with broader device support. No upload required.

## License

MIT — see [LICENSE](LICENSE)

The original IthmbDecoder reference implementation (PR [#2316](https://github.com/d2phap/ImageGlass/pull/2316)) was GPL-3.0. This plugin is a clean-room rewrite for the v10 SDK ABI and uses no GPL code.
