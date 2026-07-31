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

## File association

The MSIX manifest registers `.epub` via `windows.fileTypeAssociation`. There is no in-app registry writing.
