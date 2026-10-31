# Changelog

All notable changes to CONVERTit. Versions follow the release packages `convertit_vX.Y.Z`.

## v0.0.5 – 2026-10-31

### Added

- **Mini player** in the file lists of *Audio › Tags*, *Audio › Convert* and *Game audio*: icon switch in the list toolbar (on by default for Tags, off for the others); every row gets play/pause and *show in folder*; the playing row shows a progress bar to click or drag. One file at a time; leaving the page, removing the file or writing its tags stops it.
- **Settings › Appearance** (new card): theme, display profile, *Collapsed sidebar: open only one group at a time* (accordion, default) and *Start maximized*.
- **About**: link button to sksdesign.de (globe icon) next to the GitHub project page.

### Changed

- **Collapsed sidebar**: Video, Audio and Image no longer open a flyout menu – their sub pages unfold below as full-size icons in a box with an accent line (accordion or independently, see Appearance).
- **Tag editor**: every field is labelled (Title, Artist, Album …) above the box and inside the empty box.

### Fixed

- **Tag editor**: the fields were not connected in v0.0.4 (no labels, values not shown, *Save tags* wrote nothing). The tools (tags from file names, rename, numbering, title case) were not affected.
- **Trim & Loop**: the empty player area shows the drop zone like the Convert pages (icon, text and hint on their own lines); the audio position no longer overlaps the drop text.

## v0.0.4 – 2026-10-31

### Added

- **Audio › Tags** (new page): tag editor for **MP3** (ID3v2.3 by default, ID3v2.4 optional, ID3v1 in addition), **FLAC, OGG Vorbis, Opus** (Vorbis comments), **M4A/M4B** (MP4 atoms), **WAV** and **AIFF**.
  - Fields: title, artist, album, album artist, year, track / total tracks, disc / total discs, genre, composer, comment.
  - Several files at once: different values show *‹keep›*, only changed fields are written.
  - Cover: choose (optionally reduced to 1000 px JPEG), drop an image on the window, save as file, remove.
  - Tools: tags from file names and file names from tags with a pattern (`%track% - %artist% - %title%`), number tracks, title case.
  - Writing is protected by a backup copy until it succeeded; file dates are kept (option).
  - Explorer context menu for audio files: *Edit tags*.
- Third-party component **TagLib#** (LGPL-2.1, `licenses/third-party/TAGLIBSHARP.md`).

### Changed

- CONVERTit starts **maximized**; the window state at closing is remembered.
- **Loading indicator** appears immediately: adding files shows it before anything is read, LibVLC is loaded in the background at start, and opening a file in Trim & Loop draws the indicator before the player starts (large 8–9 GB videos no longer look frozen).
- **Trim & Loop**: *Set start* (thick arrow →) and *Set end* (thick arrow ←) with text again; the player area uses the standard grey background instead of black; the download symbol in the empty player area is gone.

## v0.0.3 – 2026-10-31

### Added

- **Loading indicator in the header**: a progress bar with text while files are read, folders are scanned, large media files are opened (Trim & Loop) or FFmpeg is downloaded (with percentage). The Trim & Loop player additionally shows *Loading file …* over the picture.
- **Multi-selection in file lists**: Ctrl/Shift + click and Ctrl+A (or the *Select all* button). **Add selected (n)** queues only the selected files with the current settings and removes them from the list, so the remaining files can get different settings; *Remove* deletes all selected rows.
- **Video › Convert – file size slider**: new quality mode *File size* (5–100 % of the original); the bitrate is calculated per file, H.264 runs in two passes; the expected size is shown under the slider.
- **More quality levels**: *Best* and *Very small* in addition to High, Balanced and Small.
- **Unreal Engine presets**: Game textures (TGA, power of two) and Game audio (sound effects and music as WAV 48 kHz).
- **Trim & Loop**: *Close file* button (✕) next to *Show in Explorer*.

### Changed

- **Light mode** softened: light grey surfaces instead of pure white (also the window title bar).
- **Collapsed sidebar**: selection and hover are square (44 × 44) instead of stretched.
- Video, Audio and Image groups always start expanded.
- **Queue**: the summary labels and numbers are colored too – running green, waiting orange, done white/dark, failed red.
- **Notifications** in the window sit at the bottom-right edge.
- **Trim & Loop**: *Set start* / *Set end* are icon buttons (|→ and →|) with the text in the tooltip; the audio waveform symbol is gone.
- **About**: buttons *GitHub project page* and *Show license* in the standard grey; "Everything runs locally" and "Commercial use" removed from the texts.
- **Dependencies**: *Remove* (FFmpeg) is red.

## v0.0.2 – 2026-10-31

### Added

- **Compress** (new page): videos, audio files and images by **target size** (two-pass x264 for exact sizes), **share of the original** or **quality level** · maximum resolution and frame rate, codec, hardware encoder, audio bitrate · built-in profiles (e-mail 25 MB, messenger 16 MB, chat 10 MB, half, quarter, H.265, strong) and **own profiles** (save/delete) · results that are not smaller can be discarded automatically.
- **Image › Game textures** (new page): presets for Unity (TGA), Godot (PNG), DirectX/mods (DDS BC3 + mipmaps) and older engines (DDS BC1) · maximum texture size · **TGA** (RLE) and **DDS** (BC1/DXT1, BC3/DXT5, uncompressed, mipmaps) as new image formats on every image page · **power-of-two** sizes (nearest, lower, higher).
- **Audio › Game audio** (new page): presets for sound effects (WAV 16 bit, mono, silence trimmed) and music (OGG Vorbis, −16 LUFS) for Unity and Godot.
- **Loudness normalization** (EBU R128: −14/−16/−23 LUFS) and **silence trimming** (start and end) for all audio pages.
- **FFmpeg setup dialog** on the first start (dimmed overlay): explains FFmpeg, source, license, size and install folder; installs only after confirmation; *Later* and *Choose FFmpeg folder*.
- **Choose FFmpeg folder** (folder with `ffmpeg.exe` and `ffprobe.exe`, also its `bin` subfolder) on the Dependencies page, in the setup dialog and in the new **Settings → Modules** card.
- **Explorer context menu** (classic menu; Windows 11: *Show more options*): separate submenus for videos, audio files and images – open, convert to MP4/MP3/OGG/PNG/JPG/WebP, compress, extract audio; several selected files are collected into one batch. Switch in *Settings → Windows integration*; removed by the uninstaller.
- **Display profiles** in *Settings → General*: Auto, extra small (< 10″), small, medium, large, extra large (≥ 30″).
- **Automatic language** on the first start: German for German Windows display language or keyboard layout, English otherwise.
- **Single instance with hand-over**: later starts (Explorer, *Open with*) pass their files to the running window.

### Changed

- **One accent color everywhere**: the neon green is identical in dark and light mode for icons, logo and controls; video, audio and image no longer use different colors. In light mode selected entries and status pills use a dark chip so the green stays readable.
- **Windows notifications** are shown directly through the Windows notification API (no PowerShell); *Test notification* reports why a notification could not be shown (e.g. notifications turned off).
- **Smooth animations**: sidebar width and labels animate when collapsing/expanding, sub entries slide in, setup dialog fades in.
- **Sidebar**: separators between Quick Convert/Compress, the groups and the queue.
- **Header**: FFmpeg pill with check mark when installed; click on *FFmpeg missing* opens the setup dialog.
- **Queue**: status as colored pills – waiting orange, running accent, done neutral, failed red.
- **Dependencies**: buttons and links centered, status (e.g. *FFmpeg 8.0 installed and verified*) as a colored pill, version and path as pills.
- **Settings**: theme *Like Windows* renamed to *System*; data folder shown as *Data folder:* with the path in a pill.
- **Fewer hint texts**: only technically relevant hints remain, each starting with *Note:* / *Hinweis:*; the removed explanations moved to the README (*Disclaimer & Notes*) and `licenses/LEGAL-NOTICE.md`.
- German language entry shown as *Deutsch*.

## v0.0.1 – 2026-10-30

First version.

### Added

- **Interface**: Avalonia 11 app for Windows 10/11 x64 with sidebar groups *Video*, *Audio*, *Image* and sub pages, collapsible sidebar (click the logo, sub pages as flyout), dark mode (default) and light mode with neon-green accent `#BBF236` and lavender `#B4ACF6`, theme switch in the header, English (default) and German.
- **Quick Convert**: drop zone for videos, audio files and images with one target format per type; the quality settings come from the Convert pages.
- **Sources**: files and folders (drag & drop, pickers), folder batch with subfolders, *all files of this type* or an extension list, *skip files already in the target format*; file list with probing and info panel (codec, resolution, frame rate, pixel format, audio tracks, subtitles, cover, alpha, preview frame).
- **Output**: subfolder (default `converted`), same folder or own folder; mirrored folder structure; suffix; conflicts (keep both / skip / overwrite); copy file dates; temporary file + rename; the source is never overwritten.
- **Video › Convert**: MP4, MKV, WebM, MOV, AVI · H.264, H.265, AV1, VP9 · software, NVENC, Quick Sync and AMF (tested encoders only) · quality levels, CRF/CQ with codec range, fixed bitrate · speed · 8-bit compatibility · resolution presets 360p–2160p (short side) or custom size · aspect ratios 16:9, 4:3, 1:1, 9:16, 21:9, 4:5, 3:2 or custom with fit (bars, color), fill (crop) or stretch · never enlarge · frame rate 23.976–120 or custom with drop, blend or motion interpolation · audio auto/copy/AAC/Opus/MP3/remove with bitrate · subtitles (copy, mov_text, WebVTT; picture subtitles reported) · metadata · fast start.
- **Video › Trim & Loop** and **Audio › Trim & Loop**: LibVLC player with timeline, thumbnails, start/end marks (buttons, keys I/O, exact times), play range, exact or fast (keyframe) cut, fade in/out, loop by number of plays or target length, boomerang; output in the same format or with the Convert settings.
- **Video › GIF / Animated WebP**: part, frame rate, width, two-pass palette with Bayer/Floyd–Steinberg/no dithering, WebP quality, loop count, boomerang.
- **Video › Remux** without re-encoding with compatibility check; **Video › Extract audio** (copy with matching extension or convert, first or all tracks).
- **Audio › Convert**: MP3 (CBR 64–320 kbps, VBR V0–V9), AAC, Opus, Ogg Vorbis (q0–q10), FLAC (0–8), ALAC, WAV (16/24/32 float) · sample rate · channels · tags · cover art · *not above source quality*.
- **Image › Convert / Resize**: PNG (compression 0–9, palette), JPEG (quality, progressive, 4:2:0/4:4:4), WebP (lossy/lossless, effort), AVIF (quality, speed), TIFF (LZW/ZIP/none), BMP, GIF, ICO · keep/remove transparency with background color · animated GIF/WebP stay animated · maximum size, percent, exact size (fit/fill/stretch) · EXIF rotation · metadata removal.
- **Image › Icon & Favicon**: multi-size ICO (16–256 px), pad or crop, padding, sharpening of small sizes, preview on light and dark tiles, favicon package (favicon.ico, apple-touch-icon, 192/512 PNG, 16/32 PNG, site.webmanifest, HTML snippet).
- **Queue**: separate lanes for FFmpeg and image jobs with adjustable parallelism, progress with speed, size before/after and savings, warnings, error texts, retry/cancel/remove, open result, show in Explorer, console with FFmpeg command and output, *cancel all*, *clear finished*.
- **Estimated output size** in the footer (exact for constant bitrates, rule of thumb for quality modes).
- **Dependencies**: FFmpeg download from BtbN/FFmpeg-Builds (newest release branch, GPL build, SHA-256 verified), update check, own ffmpeg.exe, remove; hardware encoder test; components with versions.
- **Windows integration**: taskbar progress (ITaskbarList3), toast notification when a batch is done with *Open folder* (per-user AppUserModelID and `convertit://` handler, no administrator rights), standby prevention, process priority (default below normal), job object so FFmpeg ends with the app, single instance, native title bar in the theme colors.
- **Settings**: language, theme, remember page options, parallel jobs, priority, hardware encoders, standby, taskbar, notifications, open folder when done, data folder, reset page settings, remove Windows integration.
- **Packaging**: Inno Setup (per user, optional all users), portable ZIP with `CONVERTit.portable`, `build\publish.ps1`, `build\verify.ps1`, GitHub Actions (build, test, release).
- **Tests**: FFmpeg command builder, geometry, progress parser, timecodes, output paths, scanner, German/English text parity.

### Planned for v0.0.2

- Presets (save/load/share, built-in presets for web, Discord, WhatsApp, podcasts) and command line mode (`CONVERTit.exe --preset …`).
- Target size mode (two-pass bitrate from duration), audio normalization (EBU R128 two-pass loudnorm), image optimization page.
- Explorer context menu “Convert with CONVERTit”.
