# Validation report — EPUB Lite Reader 1.0.4

Source revision: **afbe075e9af195b4d49ca81032ab435b83e48e64** (branch `claude/release-1.0.4`)
Toolchain: .NET SDK 10.0.111 · Windows 11 Pro 26200 · Windows 10 SDK 10.0.19041.0 · WebView2 Runtime 151.0.4129.101

## Build, test, smoke — all from the release revision

| Check | Command | Result |
|---|---|---|
| Release build (solution) | `dotnet build EpubLiteReader.slnx -c Release` | **PASS** — 0 warnings, 0 errors |
| Unit/regression tests | `dotnet test EpubLiteReader.slnx -c Release` | **PASS** — 115 passed, 0 failed, 0 skipped |
| EPUB pipeline smoke | `dotnet run --project tools\EpubSmoke\EpubSmoke.csproj -c Release -- tools\fixtures\sample.epub` | **PASS** — `Title=Sample EPUB; Spine=2; Chapters=2; SearchHits=1; OK` |
| Localization key parity | 14 `Strings*.resx` files | **PASS** — 79 keys each, identical set, `{0}` placeholders intact |

The test suite covers: atomic state persistence (incl. interrupted-write
recovery and stale-temp cleanup), the `SchemaVersion` migration marker,
EPUB extraction path-safety (traversal / drive-and-stream separators /
reserved device names / generated-file collision / container-absolute
href containment), script & event-attribute sanitization, web-message
validation (types, finiteness, clamping, direction, spine range, the
forwarded `key` shortcuts), reader/support URI allowlists, real-fixture
open/search/cancellation/dispose, a generated 150-spine EPUB proving the
continuous document is fully lazy (data-src only, no `setInterval`) with
escaped chapter-aware titles and a correct `#spine-N` target URL, and a
manifest-traversal EPUB proving extraction cannot escape its temp root.

## MSIX packages (built from the release revision)

| Property | win-x64 | win-arm64 |
|---|---|---|
| File | `packages/EpubLiteReader-1.0.4-win-x64.msix` | `packages/EpubLiteReader-1.0.4-win-arm64.msix` |
| Size | 70,561,314 bytes | 66,039,163 bytes |
| Identity Name | `GreenYogaInc.EPUBLiteReader` | `GreenYogaInc.EPUBLiteReader` |
| Identity Version | `1.0.4.0` | `1.0.4.0` |
| Publisher | `CN=1F15826A-1F07-4E59-AC9A-622A84CC59FF` | (same) |
| ProcessorArchitecture | `x64` | `arm64` |
| Executable | `EpubLiteReader.exe` | `EpubLiteReader.exe` |
| Exe FileVersion | `1.0.4.0` | `1.0.4.0` |
| Exe ProductVersion | `1.0.4+afbe075e9af195b4d49ca81032ab435b83e48e64` | (same) |
| File association | `.epub` | `.epub` |
| Store/tile logos present in payload | StoreLogo, Square150x150, Square44x44, EpubFileLogo | (all present) |

Verification method: each `.msix` was unpacked with `makeappx.exe unpack`
and its **embedded** `AppxManifest.xml` inspected byte-for-byte (the
unpacked manifests are in `manifest-evidence/`), not merely the source XML.

### Encoding correctness (the 1.0.3 mojibake bug is fixed)

The 1.0.3 packages shipped an embedded manifest whose em dash was
corrupted to the bytes `C3 A2 E2 82 AC` (mojibake). For 1.0.4, both
packed manifests were checked at the byte level:

- Real em dash `E2 80 94` present in the `<Description>`: **True**
- Mojibake bytes `C3 A2 E2 82 AC` present: **False**

The packaging script (`Build-Msix.ps1`) now edits the manifest via
`System.Xml.XmlDocument` (UTF-8 safe) and **fails the build** if the
staged manifest loses its em dash or contains mojibake bytes, so the
regression cannot silently recur.

## Screenshots

7 Desktop PNGs, all ≥ the Store minimum (1366×768) and < 50 MB:

| File | Dimensions | Bytes |
|---|---|---|
| 1-facing-chapters.png | 1600×900 | 62,311 |
| 2-continuous-scroll.png | 1600×900 | 57,563 |
| 3-search-highlight.png | 1600×900 | 53,548 |
| 4-fullscreen-reading.png | 1920×1080 | 313,777 |
| 5-theme-dark.png | 1600×900 | 34,772 |
| 6-about.png | 1600×900 | 63,055 |
| 7-contact-support.png | 1600×900 | 73,473 |

Captured from the Release build via the app's `--statefile` automation
hook (state-driven readiness, not fixed sleeps), then verified visually
one-by-one: correct mode/theme/state, readable text, no blank panes, no
clipped controls, no desktop/editor/terminal leakage, critical UI in the
top two-thirds. The entire old 1100×850 set was replaced.

## Runtime validation (framework-dependent Release build)

The deterministic capture run drove the actual release binary through:
book load, Facing spread + chapter sidebar, genuine Continuous mode,
full-text search with a visibly highlighted match **in continuous mode**
(the repaired frame-aware search), full-screen reading, return to Single,
Dark theme, the new About window, and Contact Support loading the live
`https://greenyogainc.com/contact/` page — every transition settling
correctly. Rapid mode switching and continuous↔single spine retention
were exercised. No personal data was entered into or submitted through
the contact form.

## External gates (not completed here — see Partner Center checklist)

- **Windows App Certification Kit (WACK):** `appcert.exe` is present
  (`C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe`).
  It was **not run on this machine** because its test cycle installs then
  uninstalls the package, and this machine has the user's real
  Store-signed **1.0.3** installed; running WACK here would risk removing
  the user's app and its local reading data, and installing version
  `1.0.4.0` could block the future Store `1.0.4.0` from installing (same
  version number). Run WACK on a clean test machine/VM:
  `appcert.exe test -appxpackagepath <EpubLiteReader-1.0.4-win-x64.msix> -reportoutputpath wack-x64.xml`
- **Installed-package / upgrade-from-1.0.3 smoke:** same reason — not
  performed against the user's daily machine. Do it on a dedicated
  environment, or with explicit approval to alter the installed app.
- **Store signing & certification:** the Store re-signs on upload;
  Microsoft certification happens in Partner Center after submission.
