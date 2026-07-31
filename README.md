# EPUB Lite Reader

A **free, lightweight EPUB reader for Windows**. Open a book and start reading
immediately — no library, no account, no distractions.

© 2026 Green Yoga Inc · Freeware, released under the [MIT License](LICENSE).

**Tagline:** Fast, clean EPUB reading for Windows. No library, no account, no distractions.

## Features

- **Three viewing modes**, toggleable from the toolbar or keyboard:
  - **Single** — one chapter pane at a time (`1`); `F11` for distraction-free full screen
  - **Facing** — two consecutive spine items side by side (`2`)
  - **Scroll** — continuous chapter scrolling (`3`)
- Chapter sidebar (`F4`) from the book’s built-in table of contents
- Font size, theme (light / sepia / dark), font family, line spacing, and margins
- Book search (`Ctrl+F`), bookmarks (`B`)
- Remembers last reading position and display settings per book (local JSON only)
- Open via dialog (`Ctrl+O`), drag & drop, or double-click a `.epub` (file association)
- Print current chapter (`Ctrl+P`) via the system print UI
- Scripts from EPUB content are stripped; external network navigation is blocked

## Keyboard reference

| Key | Action |
|---|---|
| `Ctrl+O` | Open EPUB |
| `Ctrl+F` | Search book |
| `Ctrl+P` | Print current chapter |
| `F4` | Show / hide chapter sidebar |
| `1` / `2` / `3` | Single / Facing / Continuous mode |
| `F11` (`Esc` to exit) | Full screen |
| `←` `→` / `PgUp` `PgDn` | Previous / next page or chapter |
| `Home` / `End` | Beginning / end of book |
| `Ctrl` `+` / `−` | Increase / decrease text size |
| `Ctrl+0` | Reset text settings |
| `B` | Add or remove bookmark |

## Requirements

- Windows 10 version 19041 or later
- [.NET 10](https://dotnet.microsoft.com/download) (for building) or the self-contained publish
- [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually already installed with Edge)

## Building

```powershell
dotnet build -c Release
dotnet run --project src\EpubLiteReader
```

## Distribution

See [packaging/README.md](packaging/README.md) for Microsoft Store (MSIX) and
sideloading notes.

## Tech

WPF (.NET 10) + [VersOne.Epub](https://github.com/vers-one/EpubReader) for EPUB 2/3
parsing + [WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/) for
XHTML/CSS rendering. Extracted book content is served through a locked-down local
virtual host (`epub.local`). Reading position, bookmarks, and display settings are
stored under `%LocalAppData%\GreenYogaInc\EpubLiteReader\`.

Companion to [PDF Lite Viewer](https://github.com/greenyogainc/pdf-lite-viewer).
