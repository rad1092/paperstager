# Changelog

## 0.1.1 — 2026-10-08

Correct the macOS application bundle signature. The complete bundle and its native code are ad-hoc signed after assembling the payload, then checked with strict recursive signature verification. Package hashes cover the signed files, and extracted macOS packages must pass signature verification before launch testing. The release tag workflow records the exact source revision and validation results.

The 0.1.0 macOS package could launch in the tested environment but had an invalid bundle signature; its earlier launch checks did not establish signature integrity. This patch corrects that packaging defect. Ad-hoc signing provides integrity checking, not a trusted publisher identity. No Developer ID signing, Apple notarization or Windows Authenticode signing is claimed.

## 0.1.0 — 2026-10-08

Initial preview release: native Avalonia desktop import/drop workflow, real PDFium thumbnails, editable document boundaries, multiple label/regex naming fields, reusable JSON templates/projects, explicit reviewed export, immutable sources, portable names and collision checks, atomic batch-folder promotion, recovery journals and source/output manifests. Synthetic demo; Korean quickstart; multi-OS tests and portable packages.

No built-in OCR, automated document classifier, scanner control, folder watcher, account or cloud service. No Developer ID signing, Apple notarization or Windows Authenticode signing. The macOS bundle signature was later found invalid; see the 0.1.1 correction above.
