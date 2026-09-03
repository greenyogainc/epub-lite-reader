# EPUB Lite Reader 1.0.5 — Store submission pack

Self-contained handoff for the Microsoft Store update. Built from git rev
`60d2beb0700d9177251e916a7551d97479c9f51f` (branch
`code-review/full-codebase-review-20260903-0927`).

Nothing here has been uploaded or submitted — this is the manual
Partner Center handoff only.

## Contents

- `packages/` — the two release MSIX packages (x64, arm64), identity `1.0.5.0`.
- `screenshots/` — the 7 Desktop listing screenshots (see `captions.md`).
- `manifest-evidence/` — the **unpacked** `AppxManifest.xml` from each MSIX
  (proof the embedded manifest is correct).
- `store-listing.md` — full listing copy for all 14 languages + "What's new".
- `captions.md` — screenshot captions (≤200 chars each).
- `validation-report.md` — build/test/smoke results, package verification,
  and runtime validation gates.
- `partner-center-checklist.md` — the exact manual submission steps.
- `SHA256SUMS.txt` — SHA-256 of every package and screenshot.

## Quick verify

```powershell
Get-FileHash packages\*.msix, screenshots\*.png -Algorithm SHA256
# compare against SHA256SUMS.txt
```
