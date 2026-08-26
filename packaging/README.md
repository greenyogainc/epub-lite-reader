# Packaging EPUB Lite Reader

## Sideload (zip)

```powershell
dotnet publish src\EpubLiteReader -c Release -r win-x64 --self-contained true -o dist\win-x64
```

Zip `dist\win-x64` and run `EpubLiteReader.exe`. WebView2 Evergreen Runtime must be present.

## MSIX (Store / sideload)

1. Generate store tile assets if missing: `python tools\make_icons.py`
2. Update `Package.appxmanifest` Identity `Name` / `Publisher` from Partner Center when the Store product exists.
3. Build:

```powershell
.\packaging\Build-Msix.ps1
.\packaging\Build-Msix.ps1 -Rid win-arm64
```

Output lands in `packaging\out\`. The Store signs packages on upload; for local sideload, pass `-SignThumbprint`.

Notes:

- Only `win-x64` and `win-arm64` are accepted RIDs; anything else fails fast.
- The staged manifest is edited with `XmlDocument` (UTF-8 safe) and the build
  fails if the packed manifest would lose non-ASCII text (em-dash check) —
  this bit the 1.0.3 package, whose embedded manifest shipped with mojibake.
- The exact source commit is stamped into the executable's
  `InformationalVersion` for provenance.

## Store screenshots

`tools\Capture-StoreScreenshots.ps1` captures the listing set at 1600×900
(Store minimum for Desktop is 1366×768) using the app's `--statefile`
automation hook and UI Automation, so every shot waits on real UI state. For
release screenshots, capture from the published layout:

```powershell
.\tools\Capture-StoreScreenshots.ps1 -ExePath packaging\out\layout-win-x64\EpubLiteReader.exe
```

The Contact Support shot loads the live `https://greenyogainc.com/contact/`
page and requires internet; nothing is typed into or submitted through the
form.

## File association

The MSIX manifest registers `.epub` via `windows.fileTypeAssociation`. There is no in-app registry writing.
