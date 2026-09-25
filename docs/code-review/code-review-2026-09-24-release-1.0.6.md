# Code Review & Fix — 2026-09-24 (release 1.0.6)

**Outcome: 13 confirmed defects fixed (6 high-severity security/availability, 2 high-severity reliability, 2 medium, 3 low), released as 1.0.6.**

| | |
|---|---|
| Repository | `greenyogainc/epub-lite-reader` (GitHub) |
| Base | `main` @ `6d4374c` (merge of PR #3, the earlier 2026-09-24 review) |
| Branch | `claude/code-review-release-1-0-6-8139aa` |
| Run mode | Claude Code. Discovery by three cloud review agents (one per partition), independent verification by the controller, fixes by an implementer agent, validation by a tester agent, and two independent reviewer passes. |

## Discovery

Three reviewers each took one partition, with the prior report
(`full-codebase-review-2026-09-24.md`) supplied as context so that fixed,
refuted, or accepted items were not re-reported:

| Partition | Scope | Candidates |
|---|---|---|
| Core / security | `EpubDoc.cs`, `ReaderInject.cs`, `ReadingHost.cs`, `EpubSmoke`, sanitizer/extraction tests | 2 |
| UI / state / l10n | `MainWindow`, `App`, `AboutWindow`, `BookStateStore`, `Loc`, 14× resx, generator | 2 |
| Build / packaging / docs | slnx/csproj, `Build-Msix.ps1`, manifest, store listing, submission pack, README, tools | 1 |

The controller verified every candidate against the code. A probe test showed
that mislabeled resources were extracted verbatim. The controller's own review
of `StripScripts`, plus two independent reviewer passes, then found further
defects.

## Findings ledger

| ID | Sev | Location | Defect | Fix |
|---|---|---|---|---|
| C1 | high | `EpubDoc.OpenWithChaptersCoreAsync` | Sanitization was gated on the manifest's declared media type (attacker-controlled). VersOne.Epub returns SVGs and any item declared e.g. `image/png` as bytes, and those were written raw. `evil.html`, `evil.svg`, or `evil.dat` (HTML body) kept their `<script>`/`onclick` and were served by extension as live documents on `epub.local`. | Decide by served extension (passive allowlist / markup set) plus a content sniff; decode (BOM-aware) and sanitize anything that can render as markup (`ddd180a`, `3c28c7c`) |
| S1 | high | `EpubDoc.StripScripts` | Namespace-prefixed script elements (`<svg:script>`, `<h:script xmlns:h=…>`) survived. They are real, executing elements in XML-parsed chapters and SVGs. | Optional prefix allowed on open, close, self-closing, and residual forms (`9419127`) |
| S2 | high | `EpubDoc.StripScripts` | Event handlers with no preceding whitespace survived (`<img src="x"onerror=…>`, `<img/onerror=…>`). The HTML5 tokenizer still creates the attribute, which matters for `.html` chapters (common in EPUB 2). | Lookbehind on `[\s/"']` separators (`9419127`) |
| S3 | high | `EpubDoc.StripScripts` | A `<?xml-stylesheet type="text/xsl"?>` PI made Chromium run XSLT, which can synthesize a `<script>` that no regex can see | Non-CSS stylesheet PIs removed (`9419127`) |
| S4 | high | `EpubDoc.StripScripts` | Passes replaced matches with `""` sequentially, so a later removal could splice fragments into a token an earlier pass had already scanned for (`<scr<link rel=preconnect …>ipt>` → live `<script>`; PI removal → live `onerror`). The link case predates this review. | Fixed pass order, and PI/link/script removals leave a single space (`5aa5b23`) |
| R1 | high | `EpubDoc` regexes | Lazy/looping patterns were O(n²) on hostile input: `"<script>"`×500k (4 MB) took 58.8 s. With the 64 MB cap, a single chapter could pin a core for hours, with no cancellation. | `RegexOptions.NonBacktracking` for every lookaround-free regex (the same case now takes 119 ms). `EventAttrRegex` measured linear. New `SanitizerPerformanceTests` (`5136bd6`) |
| U1 | high | `MainWindow.OpenFileAsync`, `BookStateStore` | A failure after the commit point (e.g. a state file with `"display": null`) disposed the book that was already live, deleting its extract root. Every reopen failed the same way. `"defaults": null` in `settings.json` crashed startup. | Explicit `committed` flag; null normalization on load (`2b4ce24`, `f13a126`) |
| U1b | high | `MainWindow.OpenFileAsync` | Regression found while reviewing the first U1 fix: the `ReferenceEquals(doc, _doc)` guard read a failed *first* open (both null) as committed, leaving the chapter pane stuck on "Loading". | Explicit `committed` flag (`f13a126`) |
| C2 | medium | `EpubDoc.OpenWithChaptersCoreAsync` | The 64 MB per-resource cap applied only to binary entries. Text (chapters) was written uncapped and its plain text was kept in memory. | Cap applied to text; oversized spine items get empty plain text (`462540d`) |
| C3 | medium | `EpubDoc` markup sniff | The 1 KB sniff window (added during this run) could be pushed past with leading whitespace | Windowless, BOM-aware byte-level sniff (`3c28c7c`) |
| U2 | low | `AboutWindow.LoadSupportPageAsync` | Closing About during WebView2 init leaked the view, and so did a failed init on each Retry | `_closed` flag; dispose in-flight or failed views (`1ef9465`) |
| B1 | low | `packaging/store-listing.md` | The en-US 1.0.5 note claimed API breaks were "caught automatically" (there is no CI) | Reworded (`c84046e`) |
| T1 | low | `SanitizerTests` | Two splice tests passed even with the space-replacement fix reverted | Added a genuinely space-dependent test; corrected comments (`fa2717f`) |

Refuted / no action: the EpubSmoke failure against the store-screenshot demo
book is by design. The smoke tool asserts content specific to
`tools/fixtures/sample.epub`.

## Validation

- `dotnet build EpubLiteReader.slnx -c Release`: 0 warnings, 0 errors (incremental and `--no-incremental`)
- `dotnet test EpubLiteReader.slnx -c Release`: **178/178** passed (baseline 131; +47 regression, performance, and mutation-backed tests)
- `EpubSmoke` on `tools/fixtures/sample.epub`: `SearchHits=1`, `OK`, exit 0
- Mutation testing (tester agent, in a throwaway worktree): each production fix reverted in isolation, and its targeted test class failed in every case
- Timing (4 MB adversarial inputs, after the fix): every case under 120 ms (see `SanitizerPerformanceTests`)

## Residual risks

1. **Regex sanitization is inherently best-effort.** The strongest remaining
   hardening would be a Content-Security-Policy on book documents
   (`script-src 'none'`), injected as a meta tag or served through
   `WebResourceRequested` with headers. It needs a runtime check that the
   reader's own injected scripts (`AddScriptToExecuteOnDocumentCreated` /
   `ExecuteScriptAsync`) are unaffected, so it was not attempted in this
   maintenance release. Navigation, frame, and resource filters in
   `ReadingHost` remain the second layer: they block every non-`epub.local`
   navigation and request.
2. **VersOne.Epub parse-time memory** (carried over): hostile EPUBs are fully
   materialized by the library before any cap applies.
3. **Unknown-extension binaries that start with `<`** are decoded as UTF-8
   and sanitized, which can alter them. This is accepted: no legitimate EPUB
   binary format starts with `<`, and Chromium would sniff such a file as markup.
4. **External gates** (unchanged): WACK and the install/upgrade smoke test
   must run on a clean VM, not the dev workstation, which has the Store build
   installed.
