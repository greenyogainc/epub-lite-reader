# Full Codebase Review & Fix — 2026-09-24

**Outcome: READY FOR MERGE** (merge proposal opened; repository has **NO PIPELINE CONFIGURED** — see Pipeline evidence)

| | |
|---|---|
| Repository | `greenyogainc/epub-lite-reader` (GitHub) |
| Target branch / base SHA | `main` @ `fdb532c` (= `origin/main` at start) |
| Review branch | `code-review/full-codebase-review-20260924-1325` |
| Isolation | Dedicated review branch created before any edit via `scripts/init-review-branch.sh main`; starting worktree was clean on `main`, so no separate worktree was required. `main` never checked out for work, never merged into, never pushed to. |
| Run mode | Delegated (Kilo 7.4.8 `task` tool, `permission.task=allow`). Not degraded. |
| Passes | 4 (converged; limit 5) |

## Host capability findings

- **Kilo version:** 7.4.8 (CLI `kilo --version`).
- **Delegation tool used:** `task` with `subagent_type` (Kilo 7.x). Subtask agents used: `Code Reviewer` (read-only discovery), `Code Skeptic` (independent verification), `general` (edit-capable, assigned fixes only).
- **Controller model:** `alibaba-plan/qwen3.8-max` — **not** Kilo Auto Efficient. Stated plainly per protocol; run continued.
- **Effective delegated-task model:** `minimax-coding-plan/MiniMax-M3` (pinned via `subagent_model` in `~/.config/kilo/kilo.json`) — **not** Auto Efficient. The pin overrides the picker for subtasks.
- **Return-contract enforcement:** one pass-1 reviewer (partition P1) returned an empty message; per contract it was recorded as a FAILED subtask, re-dispatched once with the contract restated, and returned a valid result. No partition was reviewed controller-side and presented as delegated work.

## Stack inventory

WPF desktop app, .NET 10 (`net10.0-windows10.0.19041.0`), C# with nullable + implicit usings; WebView2 `1.0.4078.44` (locked-down `epub.local` virtual host); VersOne.Epub `3.3.6`; xUnit test project (131 tests at HEAD); `EpubSmoke` console smoke tool; Python 3 localization/asset generators; PowerShell MSIX build + screenshot capture; MSIX Store packaging (version 1.0.5).

## Baseline (before any edit, on base SHA)

- `dotnet build EpubLiteReader.slnx -c Release` → 0 warnings, 0 errors
- `dotnet test EpubLiteReader.slnx -c Release` → 115/115 passed
- `dotnet run --project tools/EpubSmoke -- tools/fixtures/sample.epub` → OK, exit 0
- No CI configuration tracked (`git ls-files` matches none), no `.github/` in repo, GitHub Actions workflow count = 0.

## Coverage manifest (120 tracked files)

| Category | Count | Covered by |
|---|---|---|
| Maintained source (C#/XAML, `src/EpubLiteReader`) | 13 | P1 (core/security), P2 (UI/l10n), P3 (persistence) |
| Localization resources (`Strings*.resx`) | 14 | P2 |
| Tests (`tests/**.cs`) | 12 | P4 |
| Build/automation scripts (`Build-Msix.ps1`, `Capture-StoreScreenshots.ps1`, 3× Python, `EpubSmoke`) | 6 | P3, P5 |
| Project/solution config (slnx, 3 csproj, `Package.appxmanifest`, `.gitignore`) | 6 | P5 |
| Text fixtures (`tools/fixtures/sample/*`) | 7 | P1 context |
| Docs (README, CLAUDE.md, LICENSE, packaging + submission docs, prior review report) | 16 | P5 |

**Exclusions (46 files, documented):** binary images (PNG ×28, ICO ×1), binary EPUB fixtures ×3, generated checksum manifests (`SHA256SUMS.txt` ×2), generated partner-center evidence snapshots (`manifest-evidence/*.xml` ×4), local tool state (`.varmem/*` ×2), and other packaged binaries. Frozen historical submission packs (`submission-1.0.4/**`) reviewed only for harmful contradiction with current truth.

Every partition was reviewed by a delegated read-only reviewer in pass 1 and re-reviewed in pass 2 (fix verification + fresh sweep). Pass 3 covered the post-pass-2 delta; unchanged partitions inherit pass-2 clean coverage (proven by `git diff --stat 945d74a..HEAD`). Pass 4 (final skeptic) inspected the full `main...HEAD` diff.

## Findings ledger

24 candidate findings across 4 passes; deduplicated (R2P3-1 = R2P2-1). **16 confirmed and fixed, 7 refuted/invalid/no-action, 1 informational polish applied.**

### Confirmed → fixed

| ID | Sev | Location | Defect | Fix (commit) |
|---|---|---|---|---|
| P1-1 | high | `EpubDoc.StripScripts` | Unclosed `<script>` at EOF bypassed the paired-tag regex; HTML5 parser executes everything after it → broke the "scripts are stripped" promise | Residual-tag truncation + 2 regression tests (`d56c217`) |
| P1-2 | medium | `EpubDoc.StripScripts` | `<link rel=preconnect/dns-prefetch>` leaks a DNS/TCP connection; never raises `WebResourceRequested`, defeating the no-network promise | Network-hint link stripping + tests (`d56c217`) |
| P1-3 | low | `MainWindow.OpenFileAsync` | Old extract root deleted while virtual-host mapping still pointed at it → transient 404 flicker on book switch | Dispose deferred until after re-map; finally-backstop (`f667ade`) |
| P1-5 | low | `EpubDoc` extraction | No size cap on binary resources; unused `CoverImage` byte[] retained for doc lifetime | 64 MB per-resource cap (skip + record), dead `CoverImage` removed, cap tests (`d56c217`) |
| P2-1 | low | 14× `Strings*.resx`, generator | Dead keys `BookmarkAdded`/`BookmarkRemoved` (zero code references) | Keys removed; all locales at identical 77-key sets (`967b053`) |
| P3-1 | high | `tools/EpubSmoke/Program.cs` | `Search("rivers")` result printed but never asserted; search regression would pass smoke | `hits.Count == 0` → FAIL, exit 1 (`51c826e`) |
| P3-2 | low | `tools/EpubSmoke/Program.cs` | No try/finally; exception path leaked the extracted temp dir | try/finally dispose (`51c826e`) |
| P3-3 | low | `BookStateStore.WriteAtomic` | Crash-safe but concurrent writers silently last-writer-wins; contract undocumented | Doc comment states crash-safety-only + last-writer-wins contract (`dfd1ef7`) |
| P4-1 | medium | `MaliciousEpubTests` | Bare `catch { return; }` swallowed ANY exception, making the traversal invariant untestable on unrelated throws | Typed `catch (EpubReaderException)` (`954b687`) |
| P5-1 | high | `packaging/store-listing.md` | Canonical listing had no 1.0.5 What's-new blocks while the 1.0.5 checklist points operators at it → stale 1.0.4 notes would ship with the 1.0.5 package | 14 languages' verbatim 1.0.5 blocks added above preserved 1.0.4 history (`945d74a`) |
| R2P1-1 | high | `EpubDoc` `LinkTagRegex` | `<link\b[^>]*>` stopped at `>` inside a quoted attribute: `<link foo=">" rel="preconnect" …>` survived sanitization | Quote-aware, backtracking-safe pattern + bypass tests (`316fe30`) |
| R2P1-2 | medium | `EpubDoc.IsNetworkHintLink` | rel tokens split on U+0020 only; `rel="stylesheet\tpreconnect"` slipped through (HTML5 splits on all ASCII whitespace) | Whitespace split + tab/LF/CR test rows (`316fe30`) |
| R2P1-4 | info | `EpubDoc.StripScripts` docs | Tail-truncation trade-off undocumented (risk of a future "fix" reintroducing P1-1) | XML doc explains the deliberate trade-off (`316fe30`) |
| R2P2-1 | **critical** | `tools/make_localized_resx.py` | Generator drifted to 58 keys vs 77 in the checked-in resx (20 live About/Support keys missing, orphan `AboutMessage`); re-running it would silently wipe localized strings from all 13 satellites. Pre-existing drift (since `fc35a5b`), found in pass 2 | Generator synced to the resx source of truth; round-trip verified identical for all 13 satellites; new `LocalizationDriftTests` (key-set equality guards) (`443ce0e`) |
| R2P4-1 | low | test suite | No end-to-end test that EXTRACTED files on disk are sanitized (unit tests only call the helper) | `SanitizedExtractionTests` asserts extracted chapter carries no hints/scripts (`316fe30`) |
| R2P4-3 | low | `ResourceCapTests` | Skip-test lacked the post-dispose cleanup assertion its sibling had | Assertion added (`316fe30`) |
| R3-1 | low | `tools/make_localized_resx.py` | Trailing newline lost during the sync edit | Restored (`208db8b`) |

### Refuted / invalid / accepted-no-action

| ID | Disposition | Reason |
|---|---|---|
| P1-4 | refuted | Anchor concatenation cosmetic; contained by navigation/virtual-host hardening |
| P1-6 | invalid (informational) | Eager spine text is intentional design for offline search; double regex pass is millisecond-scale |
| P4-2 | refuted | OR-assertion is the test's documented contract (both outcomes acceptable) |
| P4-3 | refuted | Phantom bypass (`epub.local//evil.com` keeps authority `epub.local`); `ReaderUriTests` already covers the predicate |
| R2P1-3 | accepted-no-action | Unterminated `<link` at EOF: HTML5 tokenizer drops partial tags at EOF → no element, no hint; defense-in-depth only |
| R2P4-2 | refuted (downgraded) | `Assert.Equal` on `DefaultMaxResourceBytes` is a tautological pin, not a regression guard |
| R3-2 | refuted | `git ls-files --eol` proves resx blobs are LF; CRLF worktree is `core.autocrlf=true` checkout conversion. Generator writes LF → regeneration is byte-stable against the blobs in any environment |

## Fixes and files changed (main...HEAD)

- `src/EpubLiteReader/EpubDoc.cs` — sanitizer hardening (residual-script truncation, quote-aware link stripping, whitespace rel split), resource cap, CoverImage removal
- `src/EpubLiteReader/MainWindow.xaml.cs` — dispose ordering + skipped-entries message
- `src/EpubLiteReader/BookStateStore.cs` — concurrency contract documentation
- `src/EpubLiteReader/Strings*.resx` (×14) — 2 dead keys removed (28 deletions, no additions)
- `tools/EpubSmoke/Program.cs` — search assertion + try/finally
- `tools/make_localized_resx.py` — synced to 77-key source of truth, orphan removed, trailing newline restored
- `tests/EpubLiteReader.Tests/` — `SanitizerTests` (+9 rows/tests), `ResourceCapTests` (new, 2), `SanitizedExtractionTests` (new, 1), `LocalizationDriftTests` (new, 2), `MaliciousEpubTests` (typed catch), `EpubFixtureBuilder` (binary-resource fixture + head-extra hook)
- `packaging/store-listing.md` — 1.0.5 What's-new blocks, 14 languages

No version bump (none requested; repo rule respected). No schema/dependency changes. No unrelated work, local files, generated noise, or secrets in the diff (verified pass 4: full `main...HEAD` inspection).

## Validation evidence (at HEAD `208db8b`, pre-report commit)

- `dotnet build EpubLiteReader.slnx -c Release` → **0 warnings, 0 errors**
- `dotnet test EpubLiteReader.slnx -c Release` → **131/131 passed** (baseline 115; +16 new regression tests)
- `dotnet run --project tools/EpubSmoke/EpubSmoke.csproj -c Release -- tools/fixtures/sample.epub` → `SearchHits=1`, `OK`, **exit 0**
- `wsl python3 -m py_compile tools/make_localized_resx.py tools/make_demo_epub.py tools/make_icons.py` → OK
- Generator round-trip: regenerated 13 satellite resx semantically identical to checked-in files; resx untouched in git afterwards
- `rg BookmarkAdded|BookmarkRemoved|CoverImage|AboutMessage` → zero references repo-wide
- Independent final skeptic (pass 4): **CONVERGED** — fixes spot-checked in code at HEAD, tests reasoned to fail on revert, no accidental damage

## Convergence history

| Pass | Scope | Candidates | Confirmed | Fixed | Outcome |
|---|---|---|---|---|---|
| 1 | Full manifest, 5 delegated partition reviewers → 3 skeptic verifiers | 14 | 10 | 10 | 7 commits |
| 2 | Full manifest re-review + fix verification, 5 reviewers → 1 skeptic | 8 (dedup 7) | 5 + 1 polish | 6 | 2 commits |
| 3 | Post-pass-2 delta (7 files) | 2 | 1 | 1 | 1 commit; R3-2 refuted with git evidence |
| 4 | Final independent skeptic over full diff | 0 | 0 | — | **CONVERGED** |

## Commits (11 including this report)

```
fdb532c (main, base)
d56c217 fix(security): strip unclosed script tags and network-hint links; cap resource extraction
f667ade fix(ui): dispose previous document only after hosts re-map to the new book
51c826e fix(tools): EpubSmoke asserts search hits and disposes on every path
954b687 test: only accept library rejections in malicious-EPUB traversal test
dfd1ef7 docs: state WriteAtomic concurrency contract (crash-safety, last-writer-wins)
967b053 chore(l10n): remove dead BookmarkAdded/BookmarkRemoved resource keys
945d74a docs(packaging): add 1.0.5 What's-new blocks to the canonical store listing
316fe30 fix(security): quote-aware link-tag matching and full-whitespace rel tokenization
443ce0e fix(tools): sync make_localized_resx.py with the checked-in resx key set
208db8b style(tools): restore trailing newline in make_localized_resx.py (R3-1)
<sha>   docs: full codebase review report 2026-09-24 (this file)
```

## Rollback guidance

Every fix is an isolated commit on the review branch; `main` is untouched. To abandon the run: delete the remote branch and close the PR — nothing else references it. To roll back a single fix after merge, revert the corresponding commit; the new regression tests fail loudly if any sanitizer/cap/smoke fix is reverted, so a bad revert cannot pass `dotnet test` silently.

## Proposal information

- Pull request: `code-review/full-codebase-review-20260924-1325` → `main` on `greenyogainc/epub-lite-reader`, created with `gh pr create` after pushing (URL recorded in the run's final response and the PR itself; a committed report cannot self-record artifacts created after its own commit).
- Proposal only — **not merged**, no auto-merge enabled.

## Pipeline evidence

**NO PIPELINE CONFIGURED.** Proof from repository and provider configuration:

1. `git ls-files` contains no `.github/`, `.gitlab-ci.yml`, `azure-pipelines.yml`, or any `*.yml`/`*.yaml` CI file.
2. `gh api repos/greenyogainc/epub-lite-reader/actions/workflows` → `total_count = 0`.
3. `gh api repos/greenyogainc/epub-lite-reader/contents/.github` → HTTP 404.

There is therefore no pipeline to wait for on the exact final SHA; per protocol this is reported as NO PIPELINE CONFIGURED rather than "passing". Local validation above (build/test/smoke at HEAD) is the strongest available evidence, explicitly labeled as local, not remote-pipeline, evidence. This constraint also applies to the report itself: the committed report cannot record the pipeline result (or PR URL) for its own final SHA without creating another commit.

## Residual risks

1. **Exception-path dispose asymmetry** (`MainWindow.OpenFileAsync` catch block): if a failure occurs after the commit point, `doc?.Dispose()` runs while hosts may be mid-remap — a narrow pre-existing seam, surfaced by the final skeptic, not a regression from this run. User-visible error dialog fires; next open recreates the doc.
2. **VersOne.Epub parse-time memory**: the 64 MB cap bounds extraction writes and retention, but the library itself materializes EPUB content during `ReadBookAsync`; a hostile multi-GB EPUB can still spike memory before the cap applies. Mitigation would require library-level streaming (out of scope).
3. **Store-listing wording**: the inherited 1.0.5 bullet "API-breaking changes are caught automatically" overstates EpubSmoke's integration (manual `dotnet run`, no CI). Inherited from `main`, flagged for editorial correction.
4. **Unterminated `<link` at EOF** survives sanitization but is dropped by the HTML5 tokenizer (R2P1-3, accepted-no-action).

## Merge recommendation

**READY FOR MERGE.** 16 confirmed findings fixed (including one critical pre-existing localization-generator defect and three high-severity security/tooling defects), 7 candidates refuted with evidence, full native validation green at HEAD, independent final skeptic CONVERGED, `main` untouched, diff contains no unrelated work. Caveat: no remote pipeline exists, so exact-final-SHA CI evidence is NO PIPELINE CONFIGURED (local validation only).
