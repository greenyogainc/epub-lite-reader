# Validation report — EPUB Lite Reader 1.0.6

## Build environment

- Windows 11 (10.0.26200)
- .NET SDK 10.0.112
- WebView2 1.0.4078.44
- VersOne.Epub 3.3.6

## Source

- Branch: `claude/code-review-release-1-0-6-8139aa` (PR #4)
- Base: `main` @ `6d4374c5aaf840728d9814ebbd8519fb0ccfe79b`
- Build rev: `20f4868b6da8e77340892fd8f67aebd85fffb5ad`

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
  Passed!  - Failed: 0, Passed: 178, Skipped: 0, Total: 178
```

Baseline at 1.0.5 + PR #3 was 131. The 47 new tests cover:

- sanitization of mislabeled resources and SVGs
- the prefixed-script, separator, XSL-PI and splice bypasses
- linear-time behavior on 4 MB adversarial inputs
- the text-resource size cap
- null-field state normalization

## Smoke

```
dotnet run --project tools/EpubSmoke/EpubSmoke.csproj -c Release -- tools/fixtures/sample.epub
  SearchHits=1
  OK (exit 0)
```

## Independent verification

- Tester agent: incremental and `--no-incremental` builds are clean. Each production
  fix was reverted in isolation, and its targeted tests failed.
- Reviewer, pass 1: 3 findings (regex DoS, null-commit regression, sniff window),
  all fixed.
- Reviewer, pass 2: **APPROVE**. It compared old and new regex output on a
  differential corpus, then ran a 20k-iteration fuzzer over the full `StripScripts`
  pipeline. Neither found any output difference.

## MSIX packages

| File | Architecture | Version | Size |
|------|-------------|---------|------|
| EpubLiteReader-1.0.6-win-x64.msix | x64 | 1.0.6.0 | 70,638,463 bytes |
| EpubLiteReader-1.0.6-win-arm64.msix | arm64 | 1.0.6.0 | 66,114,074 bytes |

Both were built with `packaging\Build-Msix.ps1` (`-Rid win-x64` / `-Rid win-arm64`).
The packed executable reports `ProductVersion 1.0.6+20f4868b6da8e77340892fd8f67aebd85fffb5ad`
and `FileVersion 1.0.6.0`.

## Manifest verification

Both unpacked manifests (`manifest-evidence/`) confirm:

- Identity Version: `1.0.6.0`
- Identity Name: `GreenYogaInc.EPUBLiteReader`
- Publisher: `CN=1F15826A-1F07-4E59-AC9A-622A84CC59FF`
- ProcessorArchitecture: `x64` / `arm64`, matching each RID
- UTF-8 intact: the em dash is present and there are no mojibake bytes
- The only difference from the 1.0.5 evidence is the `Version` attribute

## External gates (not run in this session)

- [ ] **WACK**: must be run against each MSIX on a clean machine. This dev
      workstation has the Store build installed.
- [ ] **Sideload install**: verify that upgrading from 1.0.5 keeps reading position,
      bookmarks, and settings.

## Changes from 1.0.5

A security and robustness release from the second code review of 2026-09-24
(`docs/code-review/code-review-2026-09-24-release-1.0.6.md`):

1. Sanitization is now decided by served extension plus a content sniff, not by
   the declared media type. SVGs and mislabeled HTML/XML resources are sanitized.
2. `StripScripts` now removes namespace-prefixed script elements, event handlers
   with no preceding whitespace, and non-CSS `<?xml-stylesheet?>` PIs. Its passes
   can no longer splice fragments into a live tag.
3. The sanitizer regexes run in linear time (`RegexOptions.NonBacktracking`).
   A 4 MB hostile chapter drops from 58.8 s to about 0.1 s.
4. The 64 MB per-resource extraction cap now also applies to text resources.
5. A failed open no longer disposes the book that is already open. Null fields
   in saved state files are normalized on load.
6. The About window disposes the support WebView2 if it closes during initialization.

No new dependencies. No new capabilities. No UI or string changes.
