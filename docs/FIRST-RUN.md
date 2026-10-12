# Your first reviewed batch

This exercise is for someone with an existing PDF batch who needs separate,
consistently named documents. Start with **Try a synthetic example**; there is
no scanner, OCR service, account or remote upload to configure.

1. Open the extracted portable app and load the synthetic example. Expect four
   rendered page previews, grouped into two fictitious documents.
2. Visit **Boundaries & names**. Check the page boundaries and suggested fields,
   including the rotated-page example. Names come from the configured text
   rules and your edits; they are not inferred from a business system.
3. Visit **Review & export**. Inspect both proposed filenames. Correct any
   missing field, invalid name or duplicate. After any correction, click
   **Refresh plan after edits** and review the updated names and errors again.
   Editing clears the reviewed plan and its approval; checking the approval
   box alone cannot enable export until this refresh succeeds.
4. Check the approval box and choose a new, empty practice output folder. Open
   the created batch folder: expect two PDFs and `manifest.json`.
5. Open both PDFs in your usual reader. Compare their pages with the previews.
   The manifest records the original source hashes/page ranges and output
   hashes/names, so you can trace each result.
6. Save a project and reopen it immediately to check the round trip. The synthetic
   PDF lives in the operating-system temporary directory: a saved project stores
   its absolute path, not a copy of the PDF. Temporary-file cleanup can leave
   the saved metadata without a source for previews or export. For work you
   need to keep, import PDFs from a durable folder and retain them at those paths.
   Export approval is deliberately not saved; review and approve again before
   another export.

The app reads source PDFs and creates new output files. Do not interpret a
failed or cancelled export as a completed batch. Keep the saved project and
inspect any recovery folder before retrying into a fresh destination.

## If your own batch does not work

| Symptom | Next step |
| --- | --- |
| Thumbnail visible but fields empty | Image-only scans need manual field values or external OCR. PaperStager does not perform OCR. |
| A text rule finds no single value | Repeated labels and multiple regex matches need a narrower rule or a manual value. |
| Duplicate filename | Edit fields/template explicitly; names are compared without case distinction and with Unicode normalization. |
| PDF rejected | Check [supported structures](LIMITATIONS.md#pdf-structure-safety-boundary). Passwords, annotations, forms and active content are outside the copying contract. |
| Export approval disabled | Resolve plan errors and refresh names, then approve the current plan. |
| Linux launch fails | Use an X11 desktop and the runtime libraries in the README. |

Use [Releases](https://github.com/rad1092/paperstager/releases), including the
preview releases, rather than assuming GitHub's `/releases/latest` endpoint
will find this project. macOS currently targets Apple Silicon. The runtime is
included, but trusted publisher signing/notarization is not.

## Reproduced public-package check

On 2026-10-12, the public 0.1.1 macOS arm64 ZIP matched its published checksum
and passed strict ad-hoc signature verification. On macOS 27.0.1 it passed two
native launches, including an app-bundle restart: four actual thumbnails, two
exported documents, project/template round trips and unchanged source hashes.
The source suite passed 104 core and 10 desktop tests. These synthetic checks
do not claim manual testing of every OS dialog or suitability of arbitrary PDFs.
