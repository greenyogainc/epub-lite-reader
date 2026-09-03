# Validation report — EPUB Lite Reader 1.0.5

## Build environment

- Windows 10/11 (10.0.26200)
- .NET SDK 10.0
- WebView2 1.0.4078.44
- VersOne.Epub 3.3.6

## Source

- Branch: `code-review/full-codebase-review-20260903-0927`
- Base: `main` @ `32d0cd00ce4f33d2990d02de15e3e0e9fb811b14`
- Build rev: `60d2beb0700d9177251e916a7551d97479c9f51f`

## Build results

```
dotnet build EpubLiteReader.slnx -c Release
  0 Warning(s)
  0 Error(s)
  3 projects built (EpubLiteReader, EpubLiteReader.Tests, EpubSmoke)
```

## Test results

```
dotnet test EpubLiteReader.slnx -c Release
  Passed!  - Failed: 0, Passed: 115, Skipped: 0, Total: 115
```

## MSIX packages

| File | Architecture | Version | Size |
|------|-------------|---------|------|
| EpubLiteReader-1.0.5-win-x64.msix | x64 | 1.0.5.0 | ~67 MB |
| EpubLiteReader-1.0.5-win-arm64.msix | arm64 | 1.0.5.0 | ~63 MB |

## Manifest verification

Both unpacked manifests (`manifest-evidence/`) confirm:
- Identity Version: `1.0.5.0`
- Identity Name: `GreenYogaInc.EPUBLiteReader`
- Publisher: `CN=1F15826A-1F07-4E59-AC9A-622A84CC59FF`
- Architecture correctly set per RID

## External gates (not run in this session)

- [ ] **WACK** — must be run against each MSIX on a clean machine
- [ ] **Sideload install** — verify upgrade from 1.0.4 preserves reading position,
      bookmarks, and settings

## Changes from 1.0.4

This is a maintenance release from a full codebase review:
1. `EpubLiteReader.slnx` — added `tools/EpubSmoke/EpubSmoke.csproj`
2. `BookStateStoreTests.cs` — added `BookId` and `FilePath` round-trip assertions
3. `docs/code-review/full-codebase-review-2026-09-03.md` — review report

No user-facing behavior changes. No new dependencies. No new capabilities.
