# Partner Center manual submission checklist — EPUB Lite Reader 1.0.6

Every step below is **manual** in Partner Center. Nothing in this pack has
been uploaded or submitted. Product: **9N2L3FKHCV5G**
(PFN `GreenYogaInc.EPUBLiteReader_4k1k80w661cc6`).

## 0. Before you start (external gates)
- [ ] Run **WACK** on a clean machine/VM against `packages/EpubLiteReader-1.0.6-win-x64.msix`
      (and the arm64 package) and confirm it passes:
      `appcert.exe test -appxpackagepath <pkg> -reportoutputpath wack.xml`.
      Do **not** run it on the dev workstation: it has the Store build installed,
      and WACK's install/uninstall cycle would delete the local reading data.
- [ ] Optionally sideload-install the x64 package on a clean machine. Smoke-test
      launch, all three modes, search, themes, About, and Contact Support. Confirm
      an in-place **upgrade from 1.0.5 keeps** reading position, bookmarks, and settings.
- [ ] Optional: `screenshots/6-about.png` (inherited from 1.0.4/1.0.5) shows
      "Version 1.0.4". To show 1.0.6, recapture it with
      `tools\Capture-StoreScreenshots.ps1 -ExePath packaging\out\layout-win-x64\EpubLiteReader.exe`
      and update `SHA256SUMS.txt`.

## 1. Packages
- [ ] Create a new submission for product 9N2L3FKHCV5G.
- [ ] Upload `packages/EpubLiteReader-1.0.6-win-x64.msix`.
- [ ] Upload `packages/EpubLiteReader-1.0.6-win-arm64.msix`.
- [ ] Confirm Partner Center shows version **1.0.6.0** for both, architectures x64 and arm64.
- [ ] Verify the SHA-256 of each uploaded file against `SHA256SUMS.txt`
      (the same files are attached to the GitHub release `v1.0.6`).

## 2. Store listing text (per language)
Description, Product features and Search terms are unchanged from 1.0.5
(source: `../store-listing.md`). Only "What's new" changes.
- [ ] For each of the 14 languages (en-US, es, fr, de, it, pt, pt-BR, ja, ko,
      zh-Hans, zh-Hant, ru, uk, ar), paste the matching **What's new in 1.0.6**
      block from `store-listing.md` into "What's new in this version".

## 3. Shared listing fields (unchanged)
- [ ] Category: **Books & Reference**.
- [ ] Copyright and trademark info: **© 2026 Green Yoga Inc**.
- [ ] Website: **https://greenyogainc.com/**
- [ ] Support contact info: **https://greenyogainc.com/contact/**
- [ ] Privacy policy URL: **https://greenyogainc.com/privacy/**

## 4. Screenshots (per language, Desktop)
Upload the 7 PNGs from `screenshots/` in order 1→7. The captions are in `captions.md`.
- [ ] 1-facing-chapters.png
- [ ] 2-continuous-scroll.png
- [ ] 3-search-highlight.png
- [ ] 4-fullscreen-reading.png
- [ ] 5-theme-dark.png
- [ ] 6-about.png
- [ ] 7-contact-support.png
- [ ] Add the caption for each (≤200 chars, from `captions.md`).
- [ ] Remove any screenshots left from the previous submission, so the set is not mixed.

## 5. Properties / declarations
- [ ] Confirm no new capabilities were added. The manifest declares only `runFullTrust`, unchanged since 1.0.4.
- [ ] Data-collection / privacy answers: unchanged from 1.0.5.
- [ ] Age rating questionnaire: unchanged from 1.0.5.

## 6. Submit
- [ ] Review the submission summary.
- [ ] Submit to certification.
- [ ] After it passes certification, publish (or leave on the configured rollout).

## Notes
- The Store re-signs packages on upload; no developer signature is needed for submission.
- Do not overwrite or relabel the existing 1.0.5 artifacts. 1.0.6 is a new submission of the same product.
