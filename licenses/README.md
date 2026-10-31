# Licenses

## CONVERTit

CONVERTit is released under the **MIT License** – see [LICENSE.md](LICENSE.md).
Copyright © 2026 Sven Klocke (*sksdesign | complicatiion*).

| Document | Content |
| --- | --- |
| [LICENSE.md](LICENSE.md) | MIT License of CONVERTit |
| [COPYRIGHT.md](COPYRIGHT.md) | Copyright and trademark notes |
| [LEGAL-NOTICE.md](LEGAL-NOTICE.md) | Disclaimer, user responsibility (codecs, patents) |
| [PRIVACY.md](PRIVACY.md) | Privacy notes (local-first, no telemetry) |

## Third-party components

Components shipped with CONVERTit keep their own copyrights and licenses. Full license texts are in [third-party/](third-party/).

| Component | Version | License | Shipped as | File |
| --- | --- | --- | --- | --- |
| FFmpeg (ffmpeg.exe, ffprobe.exe) | latest release branch | LGPL-2.1+ / GPL-3.0 (GPL build) | **not shipped** – download in the app → data folder `tools\ffmpeg\` | [third-party/FFMPEG.md](third-party/FFMPEG.md) |
| ImageMagick · Magick.NET | 14.x | ImageMagick License · Apache-2.0 (+ native libraries, see notice) | `Magick.*.dll`, `Magick.Native-Q8-x64.dll` | [third-party/MAGICK-NET.md](third-party/MAGICK-NET.md) |
| LibVLC · LibVLCSharp | 3.0 · 3.9 | LGPL-2.1+ | `libvlc\win-x64\`, `LibVLCSharp*.dll` | [third-party/LIBVLC.md](third-party/LIBVLC.md) |
| TagLib# (TagLibSharp) | 2.3.0 | LGPL-2.1 | `TagLibSharp.dll` | [third-party/TAGLIBSHARP.md](third-party/TAGLIBSHARP.md) |
| .NET Runtime | 10.0 | MIT | self-contained runtime | [third-party/DOTNET.md](third-party/DOTNET.md) |
| Avalonia UI (incl. Fluent theme, Inter font package) | 11.3.14 | MIT | library | [third-party/AVALONIA.md](third-party/AVALONIA.md) |
| SkiaSharp / HarfBuzzSharp (via Avalonia) | – | MIT (+ Skia BSD-3-Clause, HarfBuzz MIT-style) | native libraries | [third-party/SKIASHARP.md](third-party/SKIASHARP.md) |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | library | [third-party/COMMUNITYTOOLKIT-MVVM.md](third-party/COMMUNITYTOOLKIT-MVVM.md) |
| Material Design Icons (Pictogrammers) | 7.4.47 | Apache-2.0 | vector icons in the UI | [third-party/MATERIAL-DESIGN-ICONS.md](third-party/MATERIAL-DESIGN-ICONS.md) |
| Inter (font) | 4.x | SIL OFL 1.1 | embedded font | [third-party/INTER-FONT.md](third-party/INTER-FONT.md) |

Shared license texts: [Apache License 2.0](third-party/LICENSE-APACHE-2.0.md) · [GNU LGPL 2.1](third-party/LICENSE-LGPL-2.1.md) ·
[SIL Open Font License 1.1](third-party/LICENSE-OFL-1.1.md) · [MIT](third-party/LICENSE-MIT.md) · [BSD 2-Clause](third-party/LICENSE-BSD-2-CLAUSE.md) · [BSD 3-Clause](third-party/LICENSE-BSD-3-CLAUSE.md)

Build-time only (not shipped): xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), Inno Setup (own license, installer only).

**Why CONVERTit can stay MIT:** FFmpeg is never bundled and only started as a separate program; LGPL libraries (LibVLC, TagLib#, libheif/libde265 inside Magick.NET) are shipped unmodified as replaceable dynamic libraries with their license texts and source links.
