# Partner Center manual submission checklist — EPUB Lite Reader 1.0.4

Everything below is a **manual** step in Partner Center. Nothing in this
pack has been uploaded or submitted. Product: **9N2L3FKHCV5G**
(PFN `GreenYogaInc.EPUBLiteReader_4k1k80w661cc6`).

## 0. Before you start (external gates)
- [ ] Run **WACK** on a clean machine/VM against `packages/EpubLiteReader-1.0.4-win-x64.msix`
      (and the arm64 package) and confirm it passes:
      `appcert.exe test -appxpackagepath <pkg> -reportoutputpath wack.xml`.
      (Not run here — would disturb the Store 1.0.3 installed on the dev machine; see validation-report.md.)
- [ ] Optionally sideload-install the x64 package on a clean machine to smoke-test
      launch, all three modes, search, themes, About, and Contact Support, and to
      confirm an in-place **upgrade from 1.0.3 preserves** reading position, bookmarks, and settings.

## 1. Packages
- [ ] Create a new submission for product 9N2L3FKHCV5G.
- [ ] Upload `packages/EpubLiteReader-1.0.4-win-x64.msix`.
- [ ] Upload `packages/EpubLiteReader-1.0.4-win-arm64.msix`.
- [ ] Confirm Partner Center shows version **1.0.4.0** for both, architectures x64 and arm64.
- [ ] Verify the SHA-256 of each uploaded file against `SHA256SUMS.txt`.

## 2. Store listing text (per language)
Source copy: `../store-listing.md`. There is one **Store listing** page per
language in Partner Center; paste the matching block into each.
- [ ] English (en-US) — Description, Product features, Search terms, What's new.
- [ ] es, fr, de, it, pt, pt-BR, ja, ko, zh-Hans, zh-Hant, ru, uk, ar — same four fields each.
- [ ] Paste the **What's new in 1.0.4** block into each language's "What's new in this version" field.

## 3. Shared listing fields
- [ ] Category: **Books & Reference**.
- [ ] Copyright and trademark info: **© 2026 Green Yoga Inc**.
- [ ] Website: **https://greenyogainc.com/**
- [ ] Support contact info: **https://greenyogainc.com/contact/**
- [ ] Privacy policy URL: **https://greenyogainc.com/privacy/** (required — the app can load a remote page).

## 4. Screenshots (per language, Desktop)
Upload the 7 PNGs from `screenshots/` in order 1→7. Captions in `captions.md`.
- [ ] 1-facing-chapters.png
- [ ] 2-continuous-scroll.png
- [ ] 3-search-highlight.png
- [ ] 4-fullscreen-reading.png
- [ ] 5-theme-dark.png
- [ ] 6-about.png
- [ ] 7-contact-support.png
- [ ] Add the caption for each (≤200 chars, from `captions.md`).
- [ ] Remove any screenshots from the previous (1.0.3) submission so the set is not mixed.

## 5. Properties / declarations
- [ ] Confirm no new capabilities were added (manifest declares only `runFullTrust`; unchanged from 1.0.3).
- [ ] Data-collection / privacy answers: the reader makes no network request; the
      optional Contact Support page loads greenyogainc.com only on explicit user
      action. Answer the privacy questionnaire accordingly and ensure the privacy
      policy URL (step 3) is set.
- [ ] Age rating questionnaire: unchanged from 1.0.3 (no data collection, no user-generated content, no ads).

## 6. Submit
- [ ] Review the submission summary.
- [ ] Submit to certification.
- [ ] After it passes certification, publish (or leave on the configured rollout).

## Notes
- The Store re-signs packages on upload; no developer signature is required for submission.
- Do not overwrite or relabel the existing 1.0.3 artifacts — 1.0.4 is a new submission of the same product.
