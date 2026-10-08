# Current limitations

PaperStager's first release is a manual, review-first PDF intake tool. It splits contiguous page ranges from existing PDFs, suggests filename fields from searchable text, and exports approved files into an ordinary new batch folder. The [verification record](VERIFICATION.md) describes what was actually tested; supported build targets do not by themselves establish compatibility with every machine or PDF.

## Documents and text

- Input must be a local `.pdf` file, at most **256 MiB and 5,000 pages per file**. Malformed, encrypted/password-protected, unsupported or inconsistent PDFs are rejected. There is no password-entry workflow or PDF repair tool.
- Searchable text is preferred. Image-only PDFs can have previews but need manual field values or external OCR before import. No OCR, barcode separator, automatic boundary classifier or LLM runs inside this release.
- Rules read only the **first page of each document group**. Text extraction order can differ from visual order, especially with columns, tables, unusual font encodings and rotated text. A selectable PDF is not a guarantee of correct field extraction.
- An after-label rule reads the remainder of one line after a literal, line-start label. Repeated labels or multiple regex matches produce an empty suggestion. Multiline values need a suitable regex or manual entry. Values are trimmed, Unicode-normalized and whitespace-collapsed; extracted whitespace is not preserved verbatim.
- Regexes have a 150 ms timeout. Their syntax is .NET regex syntax; matching is case-sensitive unless the expression opts out. Invalid or timed-out rules require correction or manual review.
- Previews respect PDF rendering and stored rotation, but the UI has no rotate, deskew, crop or page-editing command. Thumbnails are small previews, not full-resolution fidelity inspections.
- Every page belongs to one contiguous group. You can split or combine adjacent groups within one source, then export or explicitly skip each group. There is no page reordering, deletion, merging across sources or annotation migration workflow.

## Naming and review

- Templates generate flat filenames inside a new batch folder. They do not create arbitrary subdirectories, move originals or export directly over existing files. Built-in fields are `{document}`, `{source}`, `{page}` and `{endpage}`.
- Up to 30 naming fields are accepted. Field keys use ASCII letters, digits and underscores; values and output filenames may contain Korean and other Unicode text. Names are limited to 180 .NET string characters and 220 UTF-8 bytes, including `.pdf`. Platform-reserved characters/names, path separators, leading dots, surrounding spaces and text-direction controls are rejected.
- Duplicate names are compared after Unicode normalization and without case distinction, including on case-sensitive filesystems. Correct them explicitly; the app does not silently rename duplicates.
- There must be at least one non-skipped document. Each skipped document needs a reason. Required missing fields and other planning errors block export; the app does not invent missing values.
- Changing a document's page range can reset its manual values and skip settings. Review those again after editing boundaries. Modifying the plan clears approval. Reopening a project also requires fresh approval.
- The desktop UI is currently English, with a [Korean quickstart](QUICKSTART.ko.md). It requires a window of at least 1,000 × 700 logical pixels. It shows 12 source-page previews at a time. Very large batches and many naming fields may be awkward even below the file limits.

## PDF structure safety boundary

PDFsharp's raw page-copy operation can follow a link annotation to another page and copy that page's hidden objects even when the page is absent from the visible output page count. PaperStager therefore rejects unsupported structures at import and again from the original bytes during export, including projects saved by older versions. It does not silently remove annotations or active document features.

Rejected inputs include all page annotations, forms/XFA, optional-content layers, actions/JavaScript, attachments, named destinations, outlines, tagged-document structure, PDF/A output intents, unsupported page groups/extensions, external streams, and references from copied page content/resources into another page or catalog. Non-default `/UserUnit` physical page scaling is rejected rather than silently changing page size. Unknown page or catalog keys are conservatively rejected. The diagnostic names the unsupported feature; create and inspect an ordinary flattened copy in a trusted editor if needed. The original remains untouched.

The regression suite reproduces the raw-library annotation leak using a secret canary on the skipped second page and examines every decoded output object, not just visible pages or compressed bytes. The guarded app refuses that input, and a supported plain-page fixture proves omitted-page canary absence and retained boxes/rotation. This is a limited structural guard, not an independent PDF sanitizer or proof against every possible hidden-data technique.

## Output fidelity

PDFsharp copies selected pages into newly written PDF documents; it does not preserve the original file byte-for-byte. Stored page rotation, page boxes and normal page content are carried through the page-copy path. Basic transparency groups with direct DeviceRGB/DeviceCMYK/DeviceGray color spaces and boolean isolation/knockout values are validated and explicitly preserved; complex groups are rejected. Catalog metadata, language and presentation/viewer preferences are accepted but intentionally not copied to new documents. The output's creator is `PaperStager` and its title is the output filename stem. Originals are never rewritten.

Do not assume preservation of digital signatures, document-level metadata, bookmarks, internal links, interactive forms, attachments, tagged-PDF accessibility structure or PDF/A conformance. This is not a sanitization/redaction tool, an annotation migration utility or an archival compliance validator. Check representative outputs in a PDF viewer before adopting a workflow that depends on advanced PDF features.

## Saving and recovery

- No autosave exists. A saved project references original absolute paths and stores extracted page text, not PDF bytes. Moving or editing sources can break a restored project. The app checks source hashes before copying and again before committing export; changed sources must be imported and reviewed again. It has no source-relocation wizard.
- Project/template JSON is limited to 64 MiB. Project validation also limits source count to 100 and document count to 10,000. These are guardrails, not performance promises. JSON files are unencrypted and may contain sensitive information; see [Privacy](PRIVACY.md).
- Export writes to a sibling `.paperstager-recovery-…` directory first, then promotes that directory to a unique `PaperStager-…` name only after all selected documents are staged. This avoids presenting normally interrupted work as a completed batch. It is not a filesystem snapshot or a power-loss durability guarantee.
- A directory still named `.paperstager-recovery-…` is **uncommitted**, even if it contains a `manifest.json` whose status says `complete`: the process may have stopped before the final rename. Inspect `recovery.json` and the directory name. In a final `PaperStager-…` directory, `manifest.json` is the completed mapping; the retained recovery journal reflects the earlier staging phase.
- Failed/cancelled exports can leave complete staged PDFs, a partial current PDF and temporary JSON files. On disk-full or disconnected storage, writing the latest failure journal may also fail. A hard crash may leave only the last successful journal, or an empty recovery folder if it occurred very early. No automatic cleanup, resume or recovery-folder import is provided. Inspect, correct the cause, reopen the saved project and export a newly reviewed batch. Remove unwanted recovery folders manually only after checking them.
- Cancellation is cooperative and may wait for an ongoing PDF-library operation. Once the batch folder has been promoted, export is complete even if cancellation arrives immediately afterwards. Closing during processing requests cancellation and leaves the window open until the operation stops; close it again afterwards.
- Export promotion relies on same-parent directory rename behavior. Network mounts, removable storage and externally synchronized folders may have different failure semantics. Use a reliable local destination and ordinary backups for important workflows.

## Runtime and security boundaries

Release archives include .NET and the selected native PDF/graphics libraries; the whole package must stay together. Linux needs the desktop dependencies listed in the [README](../README.md). A separate .NET installation does not update a self-contained archive: install a newer PaperStager release when its runtime/dependencies are updated. There is no automatic updater.

Starting with 0.1.1, the complete macOS bundle and native code are ad-hoc signed and checked with `codesign --verify --deep --strict` after packaging and extraction. This checks that the signed contents remain intact; it does not certify the publisher or establish Gatekeeper trust. The invalid macOS bundle signature shipped in 0.1.0 is corrected in 0.1.1. See the [verification record](VERIFICATION.md) for the historical failure and release evidence.

This release has no macOS Developer ID signature, Apple notarization or Windows Authenticode signature. Some systems or organization policies may prevent launching it even when signature integrity checks pass. The project does not provide instructions to disable those protections.

PDF parsing and rendering use third-party libraries in the app process, not an isolated security sandbox. File-size limits, regex timeouts and filename checks do not make hostile PDFs safe or guarantee recovery from a native-engine crash or exhausted memory. The embedded PDFium version and full retained notices are listed in [Third-party notices](../THIRD-PARTY-NOTICES.md); a managed NuGet advisory check does not audit all native code compiled into those libraries.

No scanning hardware control, account system, watched-folder service, unattended batch processing, document search index or cloud synchronization is included.
