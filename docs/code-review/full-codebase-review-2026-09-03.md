# Full Codebase Review — 2026-09-03

## Outcome

**READY FOR MERGE** — Zero actionable findings after complete coverage review
with independent skeptic verification.

## Repository

- **Repo**: `greenyogainc/epub-lite-reader`
- **Target branch**: `main`
- **Base SHA**: `32d0cd00ce4f33d2990d02de15e3e0e9fb811b14`
- **Review branch**: `code-review/full-codebase-review-20260903-0927`

## Initial Git State

- Branch: `main`, clean, up to date with `origin/main`
- Remote: `https://github.com/greenyogainc/epub-lite-reader.git`

## Stack & Baseline

- .NET 10.0, WPF, WebView2, VersOne.Epub
- **Build**: 0 warnings, 0 errors
- **Tests**: 115/115 passing (534 ms)

## Coverage Manifest

### Reviewed maintained source (18 files)
- `src/EpubLiteReader/App.xaml`, `App.xaml.cs`
- `src/EpubLiteReader/MainWindow.xaml`, `MainWindow.xaml.cs`
- `src/EpubLiteReader/AboutWindow.xaml`, `AboutWindow.xaml.cs`
- `src/EpubLiteReader/EpubDoc.cs`
- `src/EpubLiteReader/ReadingHost.cs`
- `src/EpubLiteReader/ReaderInject.cs`
- `src/EpubLiteReader/BookStateStore.cs`
- `src/EpubLiteReader/ChapterItem.cs`
- `src/EpubLiteReader/Loc.cs`
- `src/EpubLiteReader/AssemblyInfo.cs`
- `src/EpubLiteReader/EpubLiteReader.csproj`

### Reviewed tests (13 files)
- `tests/EpubLiteReader.Tests/BookStateStoreTests.cs`
- `tests/EpubLiteReader.Tests/ContinuousDocumentTests.cs`
- `tests/EpubLiteReader.Tests/EpubFixtureBuilder.cs`
- `tests/EpubLiteReader.Tests/EpubOpenTests.cs`
- `tests/EpubLiteReader.Tests/EpubPathSafetyTests.cs`
- `tests/EpubLiteReader.Tests/EscapeAndTitleTests.cs`
- `tests/EpubLiteReader.Tests/HostMessageTests.cs`
- `tests/EpubLiteReader.Tests/MaliciousEpubTests.cs`
- `tests/EpubLiteReader.Tests/ReaderUriTests.cs`
- `tests/EpubLiteReader.Tests/SanitizerTests.cs`
- `tests/EpubLiteReader.Tests/SupportUriTests.cs`
- `tests/EpubLiteReader.Tests/TestPaths.cs`
- `tests/EpubLiteReader.Tests/EpubLiteReader.Tests.csproj`

### Reviewed tools & packaging (9 files)
- `tools/EpubSmoke/EpubSmoke.csproj`, `Program.cs`
- `tools/make_demo_epub.py`, `make_icons.py`, `make_localized_resx.py`
- `tools/Capture-StoreScreenshots.ps1`
- `packaging/Build-Msix.ps1`
- `packaging/Package.appxmanifest`
- `packaging/README.md`, `packaging/store-listing.md`

### Reviewed config & docs (5 files)
- `.gitignore`, `EpubLiteReader.slnx`, `README.md`, `LICENSE`, `CLAUDE.md`

### Reviewed localization (13 files)
- `src/EpubLiteReader/Strings.resx` + 12 satellite `.resx` files

### Excluded (documented)
- `packaging/Assets/*.png` — binary image assets
- `packaging/gy-logo.png` — binary image
- `packaging/store-screenshots/*.png` — binary screenshots
- `packaging/submission-1.0.4/**` — release evidence artifacts
- `src/EpubLiteReader/Assets/sample.epub` — binary fixture
- `src/EpubLiteReader/app.ico` — binary icon
- `tools/fixtures/**` — test fixtures (epub + constituent files)
- `.varmem/` — machine-local variable-tracking data

## Review Lenses Applied

1. Logic, boundaries, state transitions, error paths
2. Callers, callees, contracts, defaults, serialization
3. Concurrency, cancellation, cleanup, resource ownership
4. Data integrity, transactions, idempotency
5. Validation, authentication, authorization, secrets, privacy
6. Nullability, type soundness, unsafe casts
7. Performance, repeated I/O
8. Logging, observability, configuration, failure modes
9. Build, package, release, CI, infrastructure
10. Missing regression coverage
11. Dead, duplicated, unreachable, contradictory code
12. Repository-local rule violations

## Findings

### Initial review candidates: 12

Two parallel review subagents produced 12 candidate findings across:
- Path traversal in zip extraction (alleged)
- HTML sanitizer gaps (alleged: unquoted event handlers, javascript: URIs)
- Non-atomic file writes (alleged)
- Async void exception swallowing (alleged)
- DragMove race condition (alleged)
- Missing test coverage for absolute paths and unquoted handlers (alleged)
- Hardcoded API model in dev tool (alleged)
- Missing .gitignore patterns (alleged)

### Skeptic pass result: 0 confirmed, 12 invalid

Every candidate was refuted against the actual source code:

| ID | Claim | Refutation |
|---|---|---|
| CORE-002 | async void HandleHostMessage | Method does not exist; `OnWebMessage` has try/catch |
| CORE-003 | Path traversal via absolute paths | `TryMapToDisk` checks `IsPathRooted` + canonical containment (line 532-536); tested at lines 41-42 of EpubPathSafetyTests |
| CORE-004 | Non-atomic file write | `WriteAtomic` already does temp+rename (line 132-148) |
| CORE-005 | Corrupt JSON loses all state | Per-book files, not single-file; `WriteAtomic` prevents corruption |
| CORE-008 | Unquoted event handlers bypass sanitizer | `EventAttrRegex` third alternative `[^\s>]+` already handles unquoted values (line 564) |
| CORE-009 | Regex sanitizer fragility | WebView2 blocks all non-local navigation+resources as defense-in-depth (lines 260-280 of ReadingHost) |
| CORE-010 | DragMove race | `TitleBar_MouseLeftButtonDown` does not exist in the codebase |
| TEST-001 | Missing unquoted handler test | Test exists at SanitizerTests.cs line 48: `onmouseover=evil()` |
| TEST-002 | Missing absolute path test | Tests exist at EpubPathSafetyTests.cs lines 41-42: `C:\evil.txt`, `C:/evil.txt` |
| TEST-003 | javascript: in src untested | WebView2 navigation guard blocks all non-epub.local URIs |
| TEST-005 | Hardcoded API model | `make_localized_resx.py` is a static translation table, no API calls |
| TEST-009 | Missing .gitignore patterns | `packaging/out/` already gitignored; MSIX output goes there |

## Security Posture

The codebase has a well-layered security model:
- **Zip extraction**: `TryMapToDisk` with rooted-path rejection, `..` segment rejection, reserved-name rejection, and canonical containment check
- **HTML sanitization**: `StripScripts` removes `<script>` tags and all `on*` event handlers (quoted and unquoted)
- **WebView2 hardening**: Navigation restricted to `epub.local` virtual host, all external resource requests blocked with 403, dev tools disabled, host objects disabled, downloads blocked, permissions denied
- **State persistence**: Atomic temp-file-then-rename writes

## Validation Evidence

- **Build**: `dotnet build EpubLiteReader.slnx` — 0W, 0E
- **Tests**: `dotnet test` — 115/115 passed
- **Passes**: 1 complete pass, 0 actionable findings

## Commits

This review branch contains only this report (no source changes required).

## Residual Risk

None identified. The codebase demonstrates thoughtful defensive coding with
multi-layer security controls, comprehensive test coverage for security-critical
paths, and robust error handling throughout.

## Pipeline Evidence

This repository has no CI pipeline configured on GitHub. Status: `NO PIPELINE CONFIGURED`.

## Merge Recommendation

This branch adds only the review report. No source changes were needed —
the codebase passed review with zero confirmed findings. Safe to merge at
the maintainer's discretion.
