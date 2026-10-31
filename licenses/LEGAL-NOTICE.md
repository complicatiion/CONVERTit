# Legal Notice

## Disclaimer

CONVERTit is provided **“as is”**, without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose and non-infringement (see the MIT License in [LICENSE.md](LICENSE.md)).

To the extent permitted by applicable law, the author is not liable for any damages arising from the use of CONVERTit, including data loss, damaged or incomplete output files or quality loss caused by lossy codecs. Mandatory statutory liability (e.g. for intent or gross negligence, or under product liability law) remains unaffected.

CONVERTit never modifies or deletes source files: results are written to new files (first as a temporary file, renamed when complete). Keep backups of important files anyway.

## User responsibility

- Only convert files you are allowed to use and modify. Copy protection (DRM) is not removed or bypassed by CONVERTit.
- **Codecs and patents:** FFmpeg is not part of CONVERTit; it is downloaded on your request or provided by you. Some codecs (e.g. H.264, H.265/HEVC, AAC) may be subject to patents in some countries. Whether you need a license depends on your use (private, commercial, distribution of results) and your country. This is your responsibility.
- The FFmpeg build offered for download is a GPL build. If you redistribute that build, the GPL applies to it – see [third-party/FFMPEG.md](third-party/FFMPEG.md).

## Technical notes

- **Hardware encoders:** an encoder listed by FFmpeg does not mean that the matching GPU and driver are present. CONVERTit tests every hardware encoder with a short test encode and only offers encoders that work.
- **Compress:** target sizes are goals, not guarantees; results that are not smaller than the source can be discarded automatically.
- **Windows integration:** notifications and the optional Explorer context menu are registered per user under `HKEY_CURRENT_USER\Software\Classes` (no administrator rights). *Settings → Windows integration → Remove Windows integration* and the uninstaller remove these entries.

## Third-party software

CONVERTit ships third-party libraries (Avalonia, ImageMagick/Magick.NET, LibVLC and others) and controls FFmpeg as an external program. These components are subject to their own licenses and disclaimers – see [README.md](README.md).

## Contact

Sven Klocke · sksdesign | complicatiion · [sksdesign.de](https://sksdesign.de)
