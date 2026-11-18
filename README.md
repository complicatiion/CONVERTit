![CONVERTit](/preview/favicon.png)

# CONVERTit

**CONVERTit is an convert, upscale, compress trim and optimize app for video, audio and Images file.**

CONVERTit brings video, audio and image conversion together in one clear interface. Drop files or folders on the window – or right-click them in Explorer – pick a target format or a size, and add them to the queue. Single files or whole folders, with exact control over quality, size, resolution and frame rate. A modern Windows app built on FFmpeg, ImageMagick and VLC.

![CONVERTit](/preview/preview-collage.png)

## Features

- **Quick Convert** — Drop anything: videos, audio files and images are recognized and converted in one go. **Convert** starts right away and shows progress in every row; images go to JPEG, transparent PNGs stay PNG.
- **Compress** — Make videos, audio files and images smaller: by **target size** (e.g. 25 MB for e-mail, 10 MB for chats – two-pass encoding hits the size closely), by **share of the original** (e.g. 50 %) or by **quality level**. Ready-made profiles plus your **own profiles**; results that would not be smaller are discarded.
- **Select files** — Ctrl/Shift + click or Ctrl+A in every file list; *Add selected* queues only those files with the current settings, the rest can get other settings. **Right-click** a file: open, show in folder, copy path, convert only these, send to another page. Every list shows **thumbnails**.
- **Folder batches** — Add a whole folder, e.g. *all audio files* (MP3, OGG, M4A, FLAC, WAV …) or only the extensions you list, with subfolders and mirrored folder structure.
- **Video with full control** — MP4, MKV, WebM, MOV, AVI · H.264, H.265, AV1, VP9 · quality levels from *Best* to *Very small*, constant quality (CRF), fixed bitrate or a **file size slider** (share of the original) · resolution presets from 360p to 4K or your own size · aspect ratios 16:9, 4:3, 1:1, 9:16, 21:9, 4:5 or your own · frame rate 23.976 – 120 fps or your own.
- **Audio in every common quality** — MP3 320/256/192/128 kbps or VBR V0–V9, AAC, Opus, Ogg Vorbis, FLAC, ALAC, WAV · sample rate, mono/stereo, tags and cover · **loudness normalization** (EBU R128) and **silence trimming**.
- **Tag editor** — Like Mp3tag: title, artist, album, album artist, year, track, disc, genre, composer, comment and cover for **MP3 (ID3v2.3/v2.4, ID3v1), FLAC, OGG, Opus, M4A, WAV and AIFF** – many files at once, tags from file names, file names from tags, track numbering.
- **Mini player** — Play/pause and a seek bar in every row of the audio lists (Tags, Audio › Convert, Game audio), switchable per list.
- **Game development** — **Game textures** for Godot, Unity, Unreal Engine and DirectX engines: **TGA** (RLE), **DDS** (BC1/DXT1, BC3/DXT5, mipmaps), PNG, power-of-two sizes, **pixel-art scaling** and **normal maps**. **Game audio** presets for Godot, Unity and Unreal Engine – sound effects (WAV, mono, silence trimmed) and music (OGG Vorbis or WAV, −16 LUFS). **Sprite sheets** from images, GIFs or videos with a Godot `SpriteFrames` resource and TexturePacker JSON. **Godot video** (Ogg Theora).
- **App icons** — One motif → every icon file for Android (adaptive + TV banner), iOS, macOS, Windows, Web/PWA and Godot, with a mini editor: drag, zoom, background, safe zone, live previews.
- **Images and icons** — PNG (compression, 256-color palette, transparency), JPEG, WebP, AVIF, TIFF, BMP, GIF, TGA, DDS · resize · EXIF rotation and metadata removal · multi-size ICO, PNG sets, ICNS, SVG and a complete favicon package.
- **AI upscaling, denoise, smooth motion** — Upscale images and videos ×2–×4 with **Real-ESRGAN** or **waifu2x**, interpolate frames with **RIFE** (optional modules, one click to download), denoise videos and audio with FFmpeg filters.
- **Thumbnail cache viewer** — Look into `thumbcache_*.db` and `Thumbs.db` (read-only) and export the images.
- **Trim & Loop with player** — Built-in VLC player with timeline: start and end by click, key (I/O) or by dragging, **multi-cut** (S/C to cut, Del to remove a segment), exact or fast cut, fades, loops, boomerang, output as GIF or animated WebP.
- **GIF and animated WebP**, **Remux** (new container without re-encoding) and **audio extraction**.
- **Explorer context menu** — Right-click media files: *Open in CONVERTit*, *Convert to MP4 / MP3 / OGG / PNG / JPG / WebP*, *Compress …*, *Trim …*, *Make GIF / WebP …*, *Resize …*, *Make icon / app icons …*, *Extract audio*, *Export images from ICO* – separate menus for videos, audio files, images, icons and thumbnail caches, several files at once. Optional **file associations**.
- **GPU acceleration** — Hardware encoders (NVIDIA NVENC, Intel Quick Sync, AMD AMF, tested on your PC before they can be chosen), **video decoding on the graphics card** and **GPU turbo** (NVIDIA/Intel: decode, scale and encode on the GPU). If a file does not work with the GPU, CONVERTit simply converts it again the normal way.
- **Right picture automatically** — HDR videos are converted to SDR so colors stay right, interlaced videos (DVD, TV recordings) are deinterlaced.
- **Performance profiles** — *Auto* recognizes notebook and battery: on battery CONVERTit runs quietly and efficiently (Eco), on a strong desktop PC at full speed. *Eco*, *Balanced*, *Maximum* or your own values – the results stay the same, only the speed changes. *Settings → This system* shows what was detected.
- **Runs in the background** — Icon in the notification area with queue control; Explorer actions like *Convert to MP3* run without opening the window and report the result by notification. Optionally: minimize or close to the notification area, start with Windows.
- **Always see what's happening** — The line under the page title is a progress bar: it runs while files are loaded or large videos are opened and fills up while the queue works. Back and forward buttons (also mouse buttons and Alt+←/→) move through the visited pages.
- **Queue** — Colored status (waiting, running, done, failed), progress, size before/after and savings for every job, console with the FFmpeg command, retry and cancel.
- **Windows integration** — Progress in the taskbar, Windows notification with *Open folder* when a batch is done, no standby during long batches, *below normal* priority.
- **Fits every screen** — Display profiles from *extra small* (tablets under 10″) to *extra large* (monitors from 30″), or *Auto*.
- **Modern interface** — Dark mode (default), light mode or *System*, neon-green accent, smooth animations, English and German (chosen automatically on the first start). Hints can be switched off.


## Requirements

- Windows 10 (1809 or newer) or Windows 11, 64-bit.
- The .NET runtime, ImageMagick and VLC are included.
- **FFmpeg** is the engine behind all video and audio conversions. It is **not included** in CONVERTit and is installed once – the setup or CONVERTit does this for you (see below). Images work without FFmpeg.
- AI upscaling and frame interpolation need a graphics card with **Vulkan** support (NVIDIA, AMD, Intel) and the optional module.

## How To – Step by Step

**1 · Install**

1. Download `convertit_vX.Y.Z_setup.exe` or `convertit_vX.Y.Z_portable.zip` from the [releases](https://github.com/complicatiion/CONVERTit/releases).
2. **Setup:** run it – CONVERTit is installed **for all users** by default (needs administrator rights); choose *Install for me only* in the first dialog to install without administrator rights. On the options page, *Download FFmpeg now* is ticked (untick it to do it later in the app); *Start CONVERTit with Windows* and *Hide hints* are off by default. **Portable:** extract the ZIP and start **`CONVERTit.exe`**.
3. On the first start CONVERTit uses German on German systems and English everywhere else – you can change it under **Settings → Language**.

**2 · Install FFmpeg (once)**

1. Installed with *Download FFmpeg now*? Then it is already there – skip this step. Otherwise a dialog on the first start explains what FFmpeg is and asks whether it should be installed. Click **Download & install**.
2. CONVERTit downloads the current Windows build from the official [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases) releases on GitHub, checks its checksum and stores it in the CONVERTit data folder. The header then shows **✓ FFmpeg x.y**.
3. Clicked *Later*? Click the orange **FFmpeg missing** in the header, or go to **Dependencies** / **Settings → Modules**.
4. FFmpeg already installed somewhere else? Use **Choose FFmpeg folder** and select the folder that contains `ffmpeg.exe` and `ffprobe.exe` – or **Install from ZIP** with an archive you downloaded yourself.

**3 · Compress a video**

1. Go to **Compress** and drop the video.
2. Choose a **profile**, e.g. *E-mail · 25 MB* – or set **Target size** yourself.
3. Click **Add to queue**. The result lands in a `compressed` folder next to the original.
4. Your own settings can be saved as a profile (name → **Save profile**).

**4 · Convert a whole folder (example: all audio files to MP3 320 kbps)**

1. **Audio → Convert**, open the folder options (⚙): *Include subfolders*, *All files of this type*.
2. **Add folder** or drop it on the window.
3. **Format** *MP3*, **Bitrate** *320 kbps* → **Add to queue**.

**5 · Textures and sounds for Unity or Godot**

1. **Image → Game textures**: choose the **engine preset** (Unity/Unreal → TGA, Godot → PNG, DirectX/mods → DDS with mipmaps) and, if needed, a maximum texture size.
2. **Audio → Game audio**: *sound effects* (WAV, mono, silence trimmed) or *music* (OGG, normalized to −16 LUFS).

**6 · Cut out several parts of a video**

1. **Video → Trim & Loop**, add the video.
2. Play it and press **S** (or **C**, or the scissors button) wherever you want to cut. Drag the cut lines and the start/end handles on the timeline to fine-tune.
3. Click a segment you don't want and press **Del** (or the trash button, or right-click → *Remove segment*).
4. Choose whether the kept parts become **one file** or **separate files**, then **Add to queue**. As output you can also pick **GIF** or **animated WebP**.

**7 · App icons for Android, iOS and the web (or Godot)**

1. **Image → App icons**, add a square image (1024 px or more, ideally with transparency).
2. Tick the platforms; drag the preview to move the motif, use the mouse wheel to resize it, pick a background color.
3. **Add to queue** – a folder `name_appicons` holds all files with the exact names each platform expects.

**8 · Sharper images and smoother videos (AI)**

1. **Dependencies → Optional modules**: **Download & install** Real-ESRGAN, waifu2x or RIFE (each a few dozen to a few hundred MB, from the original project on GitHub).
2. **Tools → Upscale (AI)**: choose the model (photos or drawings) and the factor; videos are processed frame by frame.
3. **Tools → Interpolate frames**: 2× frame rate for smoother motion, or *Slow motion*.

**9 · Edit tags (MP3, FLAC, OGG, M4A …)**

1. **Audio → Tags**, add files or a folder; select one or several files (Ctrl/Shift, Ctrl+A).
2. Change the fields – with several files, *‹keep›* means the files keep their own value. Add a cover with **Choose cover** or by dropping an image on the window.
3. **Save tags**. Tools: *Tags from file names*, *Rename files from tags* (pattern e.g. `%track% - %artist% - %title%`), *Number tracks*, *Title Case*.
4. MP3 files get ID3v2.3 by default (best compatibility with Windows and car radios); ID3v2.4 and an extra ID3v1 tag can be chosen.

**10 · Right-click in Explorer**

1. **Settings → Windows integration → Explorer context menu** on.
2. Right-click media files → **CONVERTit** → e.g. *Convert to MP4* or *Compress …*. On Windows 11 the entry is under **Show more options**.
3. Direct actions (*Convert to MP4/MP3/OGG/PNG/JPG/WebP*, *Extract audio*, *Export images from ICO*) run in the background: no window opens, a Windows notification reports the result. Switch this off under **Settings → Background & notification area**.
4. **Settings → Windows integration → File associations** adds CONVERTit to *Open with* for common media files. Windows only lets you make it the default yourself: **Default apps** opens the right page.


### Supported formats

| Type | Read | Write |
| --- | --- | --- |
| Video | MP4, M4V, MKV, WebM, MOV, AVI, WMV, FLV, MPEG, TS/M2TS, 3GP, OGV, VOB, MXF … | MP4, MKV, WebM, MOV, AVI · GIF, animated WebP |
| Audio | MP3, AAC/M4A, Opus, OGG, FLAC, WAV, WMA, AIFF, APE, WavPack, AC3, DTS, MKA … | MP3, AAC (M4A), Opus, Ogg Vorbis, FLAC, ALAC, WAV |
| Video for Godot | – | OGV (Theora + Vorbis) |
| Image | PNG, JPEG, WebP, AVIF, HEIC, TIFF, BMP, GIF, ICO, PSD, TGA, DDS, SVG, JXL … | PNG, JPEG, WebP, AVIF, TIFF, BMP, GIF, ICO, ICNS, TGA, DDS · sprite sheets (+ Godot `.tres`, JSON) |

More: [docs/FORMATS.md](docs/FORMATS.md) · [docs/FEATURES.md](docs/FEATURES.md) · Changes: [docs/CHANGELOG.md](docs/CHANGELOG.md)

## Privacy & Security

- Everything runs locally. CONVERTit sends no data anywhere and has no telemetry.
- Only when you install or update FFmpeg or an optional module, CONVERTit (or the setup) contacts `api.github.com`/`github.com` – nothing is sent except the request itself.
- The thumbnail cache viewer only reads cache files; it never changes or uploads them. List thumbnails are cached in the CONVERTit data folder and can be cleared in the settings.
- Source files are only read; results are always new files.
- Settings are stored in `%LocalAppData%\CONVERTit` (portable: `data` next to the app). Details: [licenses/PRIVACY.md](licenses/PRIVACY.md).

## Credits

CONVERTit is built on **[FFmpeg](https://ffmpeg.org/)** (used as a separate program), **[ImageMagick](https://imagemagick.org/)** via **[Magick.NET](https://github.com/dlemstra/Magick.NET)** and **[LibVLC](https://www.videolan.org/vlc/libvlc.html)** via **[LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp)** **[TagLib#](https://github.com/mono/taglib-sharp)** and **[OpenMcdf](https://github.com/ironfede/openmcdf)**. Optional modules: **[Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN)**, **[waifu2x-ncnn-vulkan](https://github.com/nihui/waifu2x-ncnn-vulkan)** and **[RIFE ncnn-vulkan](https://github.com/nihui/rife-ncnn-vulkan)** by xinntao and nihui, built on **[ncnn](https://github.com/Tencent/ncnn)**. The interface is made with **[Avalonia UI](https://avaloniaui.net/)** and **[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)** on **[.NET](https://dotnet.microsoft.com/)**. Windows FFmpeg builds by **[BtbN](https://github.com/BtbN/FFmpeg-Builds)**. Icons: **[Material Design Icons](https://pictogrammers.com/library/mdi/)**, font: **[Inter](https://rsms.me/inter/)**. Licenses: [licenses/README.md](licenses/README.md).

## Project & Copyright

CONVERTit is a personal software project by **Sven Klocke** (*sksdesign | complicatiion*).

**Project repository:** [github.com/complicatiion/CONVERTit](https://github.com/complicatiion/CONVERTit)  
**Website:** [sksdesign.de](https://sksdesign.de)

CONVERTit is released under the **MIT License** ([licenses/LICENSE.md](licenses/LICENSE.md)).

## Disclaimer & Notes

- **FFmpeg** is a separate program and is not part of CONVERTit. CONVERTit only calls it via the command line; the downloaded Windows build (GPL variant, includes x264/x265) is licensed under the **GPL**, CONVERTit itself stays MIT-licensed. You install it yourself by confirming the download. Source code and license of FFmpeg: [ffmpeg.org/legal](https://ffmpeg.org/legal.html), [licenses/third-party/FFMPEG.md](licenses/third-party/FFMPEG.md).
- **Optional AI modules** (Real-ESRGAN BSD-3-Clause, waifu2x MIT, RIFE MIT) are separate programs, not part of CONVERTit, and only downloaded when you click *Download & install*: [licenses/third-party/OPTIONAL-MODULES.md](licenses/third-party/OPTIONAL-MODULES.md). AI upscaling invents plausible detail – it is not a restoration of the original.
- **Codecs & patents:** some codecs (e.g. H.264, H.265) may be covered by patents in some countries. Whether you need a license depends on your use and country – see [licenses/LEGAL-NOTICE.md](licenses/LEGAL-NOTICE.md).
- **Hardware encoders:** an encoder listed by FFmpeg does not mean that the matching GPU and driver are present. CONVERTit therefore tests every hardware encoder with a short test encode; only encoders that work can be chosen. They are fast but produce larger files than software encoders at the same quality. AV1 in hardware needs a recent graphics card (NVIDIA RTX 40, Intel Arc, AMD RX 7000 or newer).
- **GPU decoding and GPU turbo** change the speed, not the result. Unusual files (e.g. 4:2:2 or 10-bit with H.264) are converted the normal way automatically.
- **HDR:** HDR videos converted to 8-bit (H.264, or *Maximum compatibility (8-bit 4:2:0)* on) are tone mapped to SDR. With H.265/AV1 and *Maximum compatibility* off, HDR is kept.
- **Performance profiles** only change speed and load (threads, parallel jobs, priority, Windows EcoQoS); quality and file size stay the same. *Eco* takes longer.
- **Background & autostart:** *Start with Windows* writes an entry for your user (`HKCU\…\Run`), removed by the switch or by the uninstaller. Explorer background jobs end CONVERTit again when it was started only for them.
- **Performance:** video encoders already use all CPU cores – one parallel video job is usually fastest. *Below normal* priority keeps the PC responsive while converting.
- **Tag editor:** tags are written directly into the files (not into copies); a backup copy protects each file until the write succeeded. The audio data is not changed.
- **Output:** results are written to a temporary file first and renamed when complete, so an aborted job never leaves a half-written file. Originals are never changed.
- **Compress:** a target size is a goal, not a guarantee – very short or very complex videos can end up a little larger or smaller. Images do not use the target size; quality and maximum size decide.
- **Quality:** converting to a lossy format (MP3, AAC, H.264 …) always loses some quality; converting a lossy file to FLAC or WAV does not restore it. A *fast cut* starts at the previous keyframe (up to a few seconds earlier).
- **Windows notifications and the Explorer menu** need a small registration for your user (no administrator rights). It can be removed under *Settings → Windows integration*; the uninstaller removes it automatically.
- Only convert files you are allowed to use. Copy-protected media (DRM) is not supported.
- CONVERTit is provided **without warranty**; liability is limited to the extent permitted by law – see [licenses/LEGAL-NOTICE.md](licenses/LEGAL-NOTICE.md) and [licenses/COPYRIGHT.md](licenses/COPYRIGHT.md).
- Windows, FFmpeg, VLC, ImageMagick, Unity, Godot, DirectX, Android, iOS, macOS, NVIDIA, Intel and AMD are trademarks of their respective owners. CONVERTit is not affiliated with or endorsed by them.

---

