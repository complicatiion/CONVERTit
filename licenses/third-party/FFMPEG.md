# FFmpeg

| | |
| --- | --- |
| Shipped | **No.** Downloaded only after the user confirms it in the app (setup dialog on the first start, *Dependencies* or *Settings → Modules*), or provided by the user (*Choose FFmpeg folder*) |
| Source of the download | https://github.com/BtbN/FFmpeg-Builds/releases (Windows x64, GPL build of the latest release branch, SHA-256 checked against `checksums.sha256`) |
| License | FFmpeg: LGPL-2.1+; the GPL build contains GPL components (x264, x265 …) and is licensed under the GPL-3.0 as a whole |
| Copyright | Copyright the FFmpeg developers and the authors of the included libraries |
| Website / legal | https://ffmpeg.org · https://ffmpeg.org/legal.html |
| Use in CONVERTit | Video and audio conversion, probing (ffprobe), frame previews – started as a separate process |

CONVERTit does not link FFmpeg. It starts `ffmpeg.exe`/`ffprobe.exe` as independent programs and talks to them
only through command-line arguments and their text output. CONVERTit itself therefore stays under the MIT License.

The downloaded build keeps its `LICENSE.txt` in the FFmpeg folder (data folder → `tools\ffmpeg`). Source code of the
build and of all included libraries is available from the BtbN/FFmpeg-Builds repository and https://ffmpeg.org/download.html.

**Codecs and patents:** some codecs (for example H.264, H.265/HEVC, AAC) may be covered by patents in some countries.
Whether a license is needed depends on how and where you use them; this is your responsibility as the user.

FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.
