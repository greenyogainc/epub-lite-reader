# EPUB Lite Reader 1.0.6 — Store submission pack

A self-contained handoff for the Microsoft Store update. Built from git rev
`20f4868b6da8e77340892fd8f67aebd85fffb5ad` (branch
`claude/code-review-release-1-0-6-8139aa`, PR #4). The executable's
`InformationalVersion` is `1.0.6+20f4868b6da8e77340892fd8f67aebd85fffb5ad`.

Nothing here has been uploaded or submitted. This pack is the manual
Partner Center handoff only.

## Contents

- `packages/` — the two release MSIX packages (x64, arm64), identity `1.0.6.0`.
  Not tracked in git; they are attached to the `v1.0.6` GitHub release.
- `screenshots/` — the 7 Desktop listing screenshots (see `captions.md`).
- `manifest-evidence/` — the **unpacked** `AppxManifest.xml` from each MSIX,
  as proof that the embedded manifest is correct.
- `store-listing.md` — the "What's new in 1.0.6" text for all 14 languages.
- `captions.md` — screenshot captions (≤200 chars each).
- `validation-report.md` — build, test and smoke results, package verification,
  and the runtime validation gates.
- `partner-center-checklist.md` — the exact manual submission steps.
- `SHA256SUMS.txt` — SHA-256 of every package and screenshot.

## Quick verify

```powershell
Get-FileHash packages\*.msix, screenshots\*.png -Algorithm SHA256
# compare against SHA256SUMS.txt
```
