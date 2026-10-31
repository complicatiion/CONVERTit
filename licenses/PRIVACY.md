# Privacy

CONVERTit is **local-first**:

- **No telemetry, no account, no cloud.** Files are converted on your PC only; nothing is uploaded.
- **Network connections only on request:**
  - *FFmpeg setup dialog, Dependencies or Settings → Modules → Download / Check for update* (only after you confirm): requests to `api.github.com` and `github.com` (FFmpeg release list, archive and checksum file). Only the HTTP request itself is sent (IP address, user agent `CONVERTit/<version>`).
  - Links you click (project page, licenses) open in your browser.
- **No external resources in the app.** Fonts and icons are embedded.

## Data stored by CONVERTit

| Location | Content |
| --- | --- |
| `%LocalAppData%\CONVERTit\settings.json` (portable: `data\settings.json`) | settings, last used options of every page, own compress profiles, last folder, result of the encoder test |
| `%LocalAppData%\CONVERTit\tools\ffmpeg\` (portable: `data\tools\ffmpeg\`) | downloaded ffmpeg.exe, ffprobe.exe, LICENSE.txt, download.json |
| `HKCU\Software\Classes\AppUserModelId\sksdesign.CONVERTit` | name and icon for Windows notifications |
| `HKCU\Software\Classes\convertit` | per-user handler for the "Open folder" button of notifications (opens Explorer only) |
| `HKCU\Software\Classes\SystemFileAssociations\.<ext>\shell\CONVERTit` | optional Explorer context menu (only when switched on in *Settings → Windows integration*) |

The registry entries can be removed in *Settings → Windows integration → Remove Windows integration*; the uninstaller removes them as well. Converted files are written where you choose (by default a `converted` subfolder next to the sources).

On the first start CONVERTit reads the Windows display language and the keyboard layout once to choose German or English; nothing is stored except the chosen language.
