# Full Codebase Review — 2026-09-03

## Outcome

**READY FOR MERGE** — Three confirmed findings fixed, all others refuted by
independent skeptic verification.

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

### Initial review candidates: 20

Two parallel review subagents (two passes each) produced 20 candidate findings.

### Skeptic pass result: 3 confirmed, 17 invalid

| ID | Severity | Status | Summary |
|---|---|---|---|
| TOOL-001 | Medium | **Fixed** | `EpubSmoke` missing from `.slnx` — API-breaking changes in main project would not surface |
| TEST-001 | Low | **Fixed** | `FilePath` not asserted in `BookStateStoreTests` round-trip test |
| TEST-002 | Low | **Fixed** | `BookId` not asserted in `BookStateStoreTests` round-trip test |

### Refuted candidates (sample)

| Claim | Refutation |
|---|---|
| Path traversal via absolute paths | `TryMapToDisk` checks `IsPathRooted` + canonical containment; tested |
| Non-atomic file write | `WriteAtomic` already does temp+rename |
| Unquoted event handlers bypass sanitizer | `EventAttrRegex` already handles unquoted values |
| async void HandleHostMessage | Method does not exist; `OnWebMessage` has try/catch |
| CSS injection via font-family | `JsonStringEnumConverter` rejects invalid enum values; caught by error handler |
| View pump race condition | All callers run on WPF dispatcher thread; no true concurrency |
| WebView2 not disposed | WPF disposes child controls on window close |
| NavigateBlankAsync no-await | Only caller does not depend on about:blank being loaded |
| BookId includes path (orphaning) | Deliberate design choice for separate positions per location |
| ICO frame ordering fragile | Speculative; current Pillow produces correct icon |

## Security Posture

The codebase has a well-layered security model:
- **Zip extraction**: `TryMapToDisk` with rooted-path rejection, `..` segment rejection, reserved-name rejection, and canonical containment check
- **HTML sanitization**: `StripScripts` removes `<script>` tags and all `on*` event handlers (quoted and unquoted)
- **WebView2 hardening**: Navigation restricted to `epub.local` virtual host, all external resource requests blocked with 403, dev tools disabled, host objects disabled, downloads blocked, permissions denied
- **State persistence**: Atomic temp-file-then-rename writes

## Fixes Applied

1. **`EpubLiteReader.slnx`**: Added `tools/EpubSmoke/EpubSmoke.csproj` to solution
2. **`BookStateStoreTests.cs`**: Added `Assert.Equal` for `BookId` and `FilePath` in round-trip test

## Validation Evidence

- **Build**: `dotnet build EpubLiteReader.slnx` — 0W, 0E, 3 projects (now includes EpubSmoke)
- **Tests**: `dotnet test` — 115/115 passed
- **Passes**: 2 (pass 1 found 3 actionable findings; pass 2 clean after fixes)

## Residual Risk

None. The codebase demonstrates thoughtful defensive coding with multi-layer
security controls, comprehensive test coverage for security-critical paths, and
robust error handling throughout.

## Pipeline Evidence

This repository has no CI pipeline configured on GitHub. Status: `NO PIPELINE CONFIGURED`.

## Merge Recommendation

Three confirmed findings fixed (EpubSmoke in solution, round-trip test
assertions). Safe to merge at the maintainer's discretion.
