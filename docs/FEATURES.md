# Features

Overview of all functions of CONVERTit v0.0.5. Only technically relevant hints are shown in the app (marked *Note:*); background information is collected here and in the README.

## Pages

| Page | Options |
| --- | --- |
| Quick Convert | target format for videos, audio and images; folder options; output |
| Compress | profile (built-in / own), mode (target size, share of the original, quality), target size, percent, strength, keep only smaller results, metadata · videos: codec, encoder, maximum resolution, maximum frame rate, audio bitrate or remove · audio files: format, bitrate · images: format, quality, maximum size |
| Video › Convert | container, codec, encoder, quality mode (level / CRF / bitrate), speed, 8-bit, resolution, size, aspect ratio, fitting, bar color, never enlarge, frame rate, conversion method, audio, audio bitrate, subtitles, metadata, fast start |
| Video › Trim & Loop | player, timeline, thumbnails, start/end, exact/fast, fade in/out, loop (count, target length, boomerang), output format |
| Video › GIF / Animated WebP | format, start, length, frame rate, width, dithering, WebP quality, loop count, boomerang |
| Video › Remux | container, subtitles, fast start |
| Video › Extract audio | copy / convert, all tracks, audio format options |
| Audio › Convert | format, bitrate mode, bitrate, VBR level, Vorbis quality, FLAC level, bit depth, sample rate, channels, loudness (EBU R128), silence trimming, not above source, metadata, cover |
| Audio › Trim & Loop | as video, without picture |
| Audio › Tags | title, artist, album, album artist, year, track/total, disc/total, genre, composer, comment, cover (choose, drop, export, remove, resize to 1000 px) · ID3v2.3/2.4, extra ID3v1, keep file date · tags from file names, rename from tags, number tracks, title case · MP3, FLAC, OGG, Opus, M4A/M4B, WAV, AIFF |
| Audio › Game audio | presets Unity/Godot sound effects or music, all audio options incl. loudness and silence trimming |
| Image › Convert | format, quality, PNG compression/palette, JPEG progressive/chroma, WebP lossless/effort, AVIF speed, TIFF compression, GIF dither, animation, transparency, background, resize, EXIF rotation, metadata |
| Image › Resize | as Convert, preset to *keep format* + *maximum size* |
| Image › Game textures | presets Unity (TGA), Godot (PNG), DirectX/mods (DDS BC3), older engines (DDS BC1); maximum texture size; DDS compression and mipmaps; TGA RLE; power-of-two size |
| Image › Icon & Favicon | sizes 16–256, pad/crop, padding, sharpen, favicon package, preview |

## Quality reference

| Target | Recommended |
| --- | --- |
| Video for web/social | MP4 · H.264 · *Balanced* (CRF 23) · AAC 192 kbps · fast start |
| Video archive, small | MKV · H.265 *High* (CRF 20) or AV1 *Balanced* · audio copy |
| Music for any device | MP3 320 kbps CBR or V0 |
| Music, efficient | Opus 128–160 kbps or AAC 256 kbps |
| Speech / podcasts | Opus 48–64 kbps mono, MP3 96–128 kbps |
| Lossless | FLAC level 5 |
| Photos for the web | WebP quality 80–85 or JPEG 85 (4:2:0), *maximum size* 1920–2560 px, metadata removed |
| Graphics, screenshots | PNG with palette or WebP lossless |
| Video for e-mail / chat | Compress · profile *E-mail · 25 MB* or *Chat · 10 MB* |
| Unity / Godot sound effects | Game audio · WAV 16 bit, 44.1 kHz, mono, silence trimmed |
| Unity / Godot music | Game audio · OGG Vorbis q6, −16 LUFS |
| DirectX / mod textures | Game textures · DDS BC3 (alpha) or BC1, mipmaps, power of two |

## File lists

- Ctrl/Shift + click and Ctrl+A select several files; *Add selected (n)* queues only them with the current settings and removes them from the list. *Add to queue* takes all files.
- While files are read or large media is opened, the header shows a progress bar.

## Folder batches

- *All files of this type* takes every supported extension of the page's kind (audio page: all audio formats).
- *Skip files already in the target format* compares the file type (video: container and codec).
- The output folder of the batch is excluded from scanning, unfinished temp files (`*.cvt-xxxxxx.*`) too.
- With *Keep folder structure*, `converted\` contains the same subfolders as the source.

## Safety

- Source files are opened read-only, results go to `name.cvt-xxxxxx.ext` and are renamed after success.
- Cancelling or closing the app ends FFmpeg (Windows job object) and deletes unfinished files.
- When the app is closed while jobs run, it asks first.

## Windows integration

- **Explorer context menu** (optional): *CONVERTit* submenu for videos, audio files and images in the classic menu (Windows 11: *Show more options*). Entries: open in CONVERTit, convert to MP4 / MP3 / OGG / PNG / JPG / WebP, compress, extract audio. Several selected files form one batch; conversions use the settings of the matching Convert page.
- **Notifications** through the Windows notification API with an *Open folder* button; *Test notification* names the reason when Windows does not show it.
- **Taskbar progress**, standby prevention, process priority.

## Display profiles

| Profile | Scale | For |
| --- | --- | --- |
| Auto | 80–130 % by screen size | default |
| Extra small | 78 % | tablets and screens under 10″ |
| Small | 90 % | 11–14″ notebooks |
| Medium | 100 % | 15–24″ |
| Large | 115 % | 27″ |
| Extra large | 130 % | 30″ and more |
