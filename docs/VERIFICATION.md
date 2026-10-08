# Verification record

Verification uses synthetic documents only. It is not a guarantee for every PDF or filesystem.

## Historical 0.1.0 local validation — 2026-10-08

- macOS arm64, .NET SDK 10.0.401: Release build **0 warnings / 0 errors**; **104 Core tests + 10 desktop tests passed**, zero skipped.
- Core covers searchable/malformed/encrypted/rotated PDFs, Unicode and multiline naming, page boundaries, duplicate/path traversal names, stale approval, project/template round trips, source mutation before/during export, repeated export, cancellation, injected disk-write failure and recovery journals.
- PDF safety regressions reproduce raw PDFsharp hidden skipped-page content import through annotation destinations and `/UserUnit` loss. Import and export reject unsupported structures; supported plain-page output is inspected across all decoded objects for the skipped canary. Rotation, boxes and supported transparency group attributes are compared.
- Desktop tests cover immediate dirty state for pattern and all rule controls, Open/Close cancellation, back/repeat flows, safe cancelled/failed demo import, fresh approval after restoration, Unicode field edits and native PDFium rendering.
- Actual native-window `--qa` run passed: 4 pages, 8 thumbnail renders, two exports with 2 PDFs each, unchanged original SHA-256, saved/reopened projects and templates, review edits, close cancellation. [Machine-readable evidence](qa-native-macos.json).
- Release tooling: **6 tests passed**. Managed NuGet audit: 5 projects / 112 package references, 0 reported advisories. Credential-pattern scan: zero findings. Native embedded code is not comprehensively covered by NuGet's advisory database.

## Visible client-area captures

The images below were rendered by Avalonia `RenderTargetBitmap` from the **displayed native application window** during actual automation. They are not OS screenshots. PDF thumbnails came from native PDFium and actual synthetic PDFs.

![Boundaries and independent naming rules](screenshots/boundaries-macos.png)
![Proposed filenames and editable fields](screenshots/review-macos.png)
![Unsaved edit confirmation](screenshots/unsaved-confirmation-macos.png)

External CUA app access failed to return twice and was aborted. It is not retried. **External window controls, native file/folder-picker mouse interaction and OS drag-and-drop remain manually unverified.** Automated project reopening is distinct from package process restart. No OS permission or security setting was changed.

## macOS signature correction in 0.1.1

Independent verification of the installed 0.1.0 macOS package from **62ce6da940fac68d4bd45c29f1b5f5dd024a0ab5** failed `codesign --verify --deep --strict` with `code has no resources but signature indicates they must be present`. Its apphost had a signature, but the assembled application bundle did not have a valid resource seal. Managed DLLs and JSON files in the native-code directory also prevented simply signing the existing layout. Earlier launch/restart results remain valid as launch evidence; they did **not** establish bundle signature integrity.

The 0.1.1 packaging correction assembles the final payload, signs nested native code before signing the complete bundle, verifies it strictly, and then creates file and archive hashes. Extracted macOS packages must pass `codesign --verify --deep --strict` before native launch and restart checks. The release verification also checks the published download in a fresh location. These are signature-integrity checks; an ad-hoc signature does not provide Developer ID identity or Apple notarization.

Local patch validation passed 104 Core tests, 10 desktop tests and 8 release-tool tests. The native signing regression verifies a ZIP round trip and rejects both changed resources and changed nested native code. The actual single-file app package passed strict verification, native import/render/export, shutdown and LaunchServices restart with original PDF hashes preserved. NuGet audit covered 5 projects / 113 package references with zero reported advisories; credential-pattern scanning found zero matches.

The **0.1.1 release tag workflow supplies the final exact source revision, run results and artifact checksums**. Those results are not asserted here in advance. Check the run associated with that tag and its release notes for completed three-OS tests, macOS signature verification, native launch/restart and public-download verification. No 0.1.0 result substitutes for these checks.

## Exact source revision and multi-OS packages

The historical 0.1.0 code candidate **5e761b3154dca443c361dbf2cbe7a12b032aec7d** passed all three OS jobs in [run 37720078722](https://github.com/rad1092/paperstager/actions/runs/37720078722), including tests, dependency/credential checks, package extraction, native launch and restart. That workflow did not check macOS bundle signatures. Each release's version-tag workflow and release notes record its final exact SHA; release publication and re-download verification must both finish successfully in that tag run.

[GitHub Actions](https://github.com/rad1092/paperstager/actions) builds/tests on macOS arm64, Windows x64 and Linux x64. Each job publishes a self-contained archive, verifies/extracts it, starts the actual native app, checks the import→render→review→export marker, closes and starts it again. macOS second launch uses the `.app` through LaunchServices. A 90-second app watchdog applies only to explicit synthetic QA/smoke modes.

The first run, `37713035957` at `596fec2`, failed during locked restore on all three OSes and is **not** a passing multi-OS result. The fix declares all three runtime identifiers and regenerates lock files. Read the successful run associated with the release's **exact commit**; release publication depends on all three jobs succeeding. Release metadata records `sourceCommit`, native architecture and file hashes. The release workflow downloads the published ZIPs again and verifies SHA256SUMS, sidecars and commit identity. The GitHub run and release notes provide the final machine-generated revision/result.

## Practical limits

Disk-write failure is injected; physical power failure, physical disk exhaustion, hostile filesystem races, hostile native-parser exploits and screen-reader parity are not established. Parsing/rendering can only be cancelled between native operations. Advanced PDF structure is conservatively rejected as documented in [Limitations](LIMITATIONS.md). macOS 0.1.1 uses ad-hoc signing, with no Developer ID signature or Apple notarization; Windows has no Authenticode signature. Signature integrity does not establish OS distribution trust. Windows/Linux native package execution is CI automation, not a human desktop session.
