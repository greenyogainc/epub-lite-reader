# Code Review & Fix — 2026-09-26

**Outcome: 9 confirmed defects fixed (1 high, 4 medium, 4 low) plus two nits, on branch
`claude/review-2026-09-26-fixes`. No version bump (no release requested).**

| | |
|---|---|
| Repository | `greenyogainc/epub-lite-reader` |
| Base | `main` @ `b48f03b` (release 1.0.6) |
| Branch | `claude/review-2026-09-26-fixes` |
| Findings source | `docs/code-review/full-codebase-review-2026-09-26.md` |

## Why there is no local build/test evidence here

`dotnet build` / `dotnet test` need a Windows host (WPF, `net10.0-windows`), which the
review environment is not, and its package registry egress is blocked. The
security-critical behavior was instead validated in real Chromium (the engine WebView2
uses), with the reader's own injected script and the new CSP headers, and the sanitizer
and localization changes were validated by executable port. **The Windows build and the
full `dotnet test` run must be executed on a Windows host before merge.** Two points can
only be confirmed there and are called out under "Windows verification" below.

## Findings and fixes

### F1 — `javascript:` URLs and book script (high)

Book documents are now served through a new `BookResourceServer`
(`WebResourceRequested`) with a locked-down `Content-Security-Policy`
(`default-src 'none'; script-src 'none'; object-src 'none'; form-action 'none';
base-uri 'none'`, with same-origin `img/media/style/font/frame`), an explicit
`Content-Type` per extension, and `X-Content-Type-Options: nosniff`. The continuous
document — the one page with a legitimate inline script — is allowed exactly that script
by `sha256` hash (`EpubDoc.ContinuousScriptCspSource`, computed over the emitted script).
The virtual-host folder mapping is kept only as a functional fallback; the handler answers
every `epub.local` request, so CSP is always applied. `ReaderInject`'s click guard was
also hardened (resolves via `URL`, covers `<a>`/`<area>`/SVG `<a>` through
`composedPath()`, and a `submit` guard covers form/`formaction`) as defense-in-depth
behind the CSP. `EpubDoc.StripScripts` remains as a third layer.

Verified in Chromium: `<iframe src="javascript:…">`, an inline `onerror`, and the leading-
space / SVG-`xlink` / `<area>` / `<form action>` click variants all fail to execute under
the CSP; the reader's injected `window.__elr*` functions still run under `script-src
'none'`; the continuous document's inline script runs under its hash.

### F2 — extension/MIME mismatch (medium)

`MarkupExtensions` gained `.shtm .xhtm .ehtml .xbl .xul .rdf .rss` (Chromium maps several
of these to `text/html`/XML). Passive-extension byte resources are now run through
`LooksLikeMarkup` and sanitized if they begin with `<`. The `BookResourceServer`
Content-Type map plus `nosniff` closes the served-type side independently.

### F3 — event-handler pass corrupted content (medium)

The `on*=` removal is now confined to matches of a quote-aware `StartTagRegex`, so it can
no longer touch prose or code (`int one = 1;` is preserved) or delete a following end tag
and break XHTML well-formedness. Ported-logic checks confirm real handlers
(`<img src=x onerror=…>`, separator-less, `/`-prefixed, post-PI-splice) are still removed.

### F4 — search never left the current chapter (medium)

`__elrFind` now calls `window.find` with `wrapAround = false`, so an exhausted chapter
reports `false` and the search advances. `MainWindow` picks the next hit in a *different*
spine (`NextHitIndexInOtherSpine`, unit-tested), wrapping at the book's ends, in
single/facing modes; continuous mode keeps its per-hit walk. Chromium confirms the
non-wrapping behavior.

### F5 — unescaped spine URLs (medium)

`EpubDoc.ToUrlPath` percent-encodes each path segment; `GetSpineUrl` and the continuous
document's `data-src` both use it. A chapter named `C#.xhtml` / `100%.xhtml` / `a b.xhtml`
now builds a URL that `BookResourceServer` resolves back to the file on disk
(round-trip tested).

### F6 — empty spine (low)

`OpenWithChaptersCoreAsync` throws `EpubNoReadableContentException` before the commit
point when `ReadingOrder` is empty; `MainWindow` shows the localized `NoReadableContent`
message and keeps the previous book. `GoToSpineAsync` also guards `SpineCount == 0`.

### F7 — temp/log hygiene (low)

Each extract root gets a sibling `.lock` file held for the document's lifetime.
`SweepOrphanedExtracts` runs once at startup (off the UI thread) and removes roots whose
lock can be taken exclusively, or lock-less legacy roots older than a day. (Log rolling was
scoped out; see residual risks.)

### F8 — `Build-Msix.ps1` (low)

Added a `signtool.exe` null-check and a guard that fails the build when the csproj
`<Version>` and the manifest `Identity/@Version` disagree.

### F9 — persisted display values (low)

`BookStateStore` clamps `FontScale`/`LineHeight`/`MarginEm` on load (both app defaults and
per-book), mirroring the UI ranges and replacing non-finite values with defaults.

### Nits

- Removed the dead `blocked-nav` host message (it had no handler; navigation blocking is
  unchanged).
- `WriteAtomic` now flushes the temp file to disk before the rename (power-loss safety).
- Documented `ComputeBookId`'s path-dependent identity.

## New key

`NoReadableContent` was added to `Strings.resx`, all 13 satellites, and
`make_localized_resx.py`; the generator round-trips byte-identically.

## Tests added

`BookResourceServerTests` (path resolution + Range), `SanitizerTests` (+5 F3 cases),
`SearchAdvanceTests` (F4), `ReservedCharAndEmptySpineTests` (F5/F6),
`ExtractSweepTests` (F7), `BookStateStoreTests` (+F9 clamps), `ContinuousCspTests` (F1
hash matches the emitted script), plus two fixture builders.

## Windows verification (must run before merge)

1. `dotnet build -c Release` and `dotnet test -c Release` on Windows.
2. Confirm in WebView2 (not just Chromium) that `AddScriptToExecuteOnDocumentCreated` /
   `ExecuteScriptAsync` and `chrome.webview.postMessage` still work under the book CSP —
   i.e. themes, paging, and messaging function. Open `review-probe.epub` (from the review
   report) and confirm every marker stays "not run", the code sample is intact, and the
   `C#.xhtml` chapter renders.

## Residual risks

1. **Log growth** (`EpubLiteReader.log`) is unbounded; not addressed here.
2. VersOne.Epub parse-time memory (carried over) is unchanged.
3. The continuous-document CSP hash depends on the emitted script matching
   `EpubDoc.ContinuousScript` byte-for-byte after HTML newline normalization;
   `ContinuousCspTests` guards this, and the script literal is LF-normalized so a CRLF
   checkout cannot break it.
