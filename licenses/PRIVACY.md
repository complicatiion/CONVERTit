# Privacy

CONVERTit is **local-first**:

- **No telemetry, no account, no cloud.** Files are converted on your PC only; nothing is uploaded.
- **Network connections only on request:**
  - *FFmpeg setup dialog, Dependencies or Settings → Modules → Download / Check for update* (only after you confirm): requests to `api.github.com` and `github.com` (FFmpeg release list, archive and checksum file). Only the HTTP request itself is sent (IP address, user agent `CONVERTit/<version>`).
  - *Installer → Download FFmpeg now* (pre-selected, can be unticked): the same requests to `github.com` during setup.
  - *Settings → Updates* (off by default; switching on asks first, also the installer option *Check for updates*): once a day at start, or on *Check now*, a request to `api.github.com/repos/complicatiion/CONVERTit/releases/latest`; *Download update* loads the setup or portable ZIP and its checksum from `github.com`.
  - *Dependencies → Optional modules → Download & install*: requests to `api.github.com` (release information of the pinned version) and `github.com` (archive).
  - Links you click (project page, licenses) open in your browser.
- **No external resources in the app.** Fonts and icons are embedded.

## Data stored by CONVERTit

| Location | Content |
| --- | --- |
| `%LocalAppData%\CONVERTit\settings.json` (portable: `data\settings.json`) | settings, last used options of every page, own compress profiles, last folder, result of the encoder test |
| `%LocalAppData%\CONVERTit\modules\` (portable: `data\modules\`) | downloaded FFmpeg (ffmpeg.exe, ffprobe.exe, LICENSE.txt, download.json) and optional modules with their license files |
| `C:\ProgramData\CONVERTit\modules\` | FFmpeg downloaded by the setup for all users (removed by the uninstaller) |
| `%LocalAppData%\CONVERTit\thumbs\` | small preview images of files you added to a list (only when the Windows thumbnail cache has none; ≤ 200 MB; *Settings → Data → Clear thumbnail cache*) |
| `HKCU\Software\Classes\AppUserModelId\sksdesign.CONVERTit` | name and icon for Windows notifications |
| `HKCU\Software\Classes\convertit` | per-user handler for the "Open folder" button of notifications (opens Explorer only) |
| `HKCU\Software\Classes\SystemFileAssociations\.<ext>\shell\CONVERTit` | optional Explorer context menu (only when switched on in *Settings → Windows integration*) |
| `HKCU\Software\Classes\.<ext>\OpenWithProgids`, `HKCU\Software\Classes\CONVERTit.*`, `HKCU\Software\RegisteredApplications` | optional file associations (only when switched on in *Settings → Windows integration*) |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `CONVERTit` | optional start with Windows (*Settings → Background & notification area* or the installer option) |

The registry entries can be removed in *Settings → Windows integration → Remove Windows integration*; the uninstaller removes them as well. Converted files are written where you choose (by default a `converted` subfolder next to the sources).

On the first start CONVERTit reads the Windows display language and the keyboard layout once to choose German or English; nothing is stored except the chosen language.

For the performance profiles CONVERTit reads processor name and cores, memory size, the graphics adapters (name, vendor, video memory) and the power source (battery level, energy saver) with Windows functions. These values are only shown in *Settings → This system* and used to choose the profile; they are neither stored nor sent anywhere.

## Thumbnail cache viewer

The viewer (*Tools → Thumbnail cache*) opens cache files read-only and never changes, copies or uploads them. Only *Clean cache …* deletes files: after an opt-in dialog, the Explorer thumbnail cache and optionally the icon cache of your user account (`%LocalAppData%\Microsoft\Windows\Explorer\thumbcache_*.db`, `iconcache_*.db`, `%LocalAppData%\IconCache.db`); Windows rebuilds them. Marks exist only while the app runs. Exported images are written only to the folder you choose. The Windows cache can contain thumbnails of files that were deleted long ago; it has no file names (Thumbs.db files from older systems or network shares do).
