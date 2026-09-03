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
- **Click to turn pages** — click the page to go forward, or its left quarter to
  go back. Scrolls within the chapter first, then moves to the next one; the
  keyboard (`Space`, `PgDn`/`PgUp`, `←`/`→`) does exactly the same thing.
- Chapter sidebar (`F4`) from the book’s built-in table of contents
- Font size, theme (light / sepia / dark), font family, line spacing, and margins
- Book search (`Ctrl+F`), bookmarks (`B`)
- Remembers last reading position and display settings per book (local JSON
  only, written crash-safely)
- Open via dialog (`Ctrl+O`), drag & drop, or double-click a `.epub` (file association)
- Print current chapter (`Ctrl+P`) via the system print UI
- Scripts from EPUB content are stripped, and book content cannot make any
  network request — navigation and resource loads are locked to the book's
  local virtual host
- About window (⚙ → About) with version, license, and an optional
  **Contact support** page that loads the Green Yoga website only when you
  explicitly ask — the reader itself stays fully offline

## Keyboard reference

| Key | Action |
|---|---|
| `Ctrl+O` | Open EPUB |
| `Ctrl+F` | Search book |
| `Ctrl+P` | Print current chapter |
| `F4` | Show / hide chapter sidebar |
| `1` / `2` / `3` | Single / Facing / Continuous mode |
| `F11` (`Esc` to exit) | Full screen |
| `Space` / `Shift`+`Space` | Next / previous page or chapter |
| `←` `→` / `PgUp` `PgDn` | Previous / next page or chapter |
| `Home` / `End` | Beginning / end of book |
| `Ctrl` `+` / `−` | Increase / decrease text size |
| `Ctrl+0` | Reset text settings |
| `B` | Add or remove bookmark |

## What's new in 1.0.5

- **Build coverage**: The `EpubSmoke` tool is now included in the solution file,
  so `dotnet build EpubLiteReader.slnx` catches API-breaking changes in the
  smoke-test tool automatically.
- **Stronger test assertions**: `BookStateStore` round-trip test now verifies
  `BookId` and `FilePath` survive serialization.
- Full codebase review report added (`docs/code-review/`).

## What's new in 1.0.4

See [packaging/submission-1.0.4/store-listing.md](packaging/submission-1.0.4/store-listing.md)
for the full 1.0.4 release notes.

## Requirements

- Windows 10 version 19041 or later
- [.NET 10](https://dotnet.microsoft.com/download) (for building) or the self-contained publish
- [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (usually already installed with Edge)

## Building

```powershell
dotnet build -c Release
dotnet run --project src\EpubLiteReader
```

## Tests

```powershell
dotnet test EpubLiteReader.slnx -c Release
```

There is also a headless EPUB pipeline smoke check:

```powershell
dotnet run --project tools\EpubSmoke\EpubSmoke.csproj -c Release -- tools\fixtures\sample.epub
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
