# EPUB Lite Reader 1.0.4 — Store submission pack

Self-contained handoff for the Microsoft Store update. Built from git rev
`afbe075e9af195b4d49ca81032ab435b83e48e64` (branch `claude/release-1.0.4`).

Nothing here has been uploaded or submitted — this is the manual
Partner Center handoff only.

## Contents

- `packages/` — the two release MSIX packages (x64, arm64), identity `1.0.4.0`.
- `screenshots/` — the 7 Desktop listing screenshots (see `captions.md`).
- `manifest-evidence/` — the **unpacked** `AppxManifest.xml` from each MSIX
  (proof the embedded manifest, not just source XML, is correct incl. the
  em-dash/encoding fix).
- `store-listing.md` — full listing copy for all 14 languages + "What's new".
- `captions.md` — screenshot captions (≤200 chars each).
- `validation-report.md` — build/test/smoke results, package verification,
  runtime validation, and the external gates (WACK, installed-package test).
- `partner-center-checklist.md` — the exact manual submission steps.
- `SHA256SUMS.txt` — SHA-256 of every package and screenshot.

## Quick verify

```powershell
Get-FileHash packages\*.msix, screenshots\*.png -Algorithm SHA256
# compare against SHA256SUMS.txt
```
