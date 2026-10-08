# Privacy and offline policy

PaperStager processes PDFs on your computer. The shipped app has no account system, cloud upload, analytics collection, LLM connection, automatic updater, folder watcher or background ingestion service. Import, text extraction, thumbnails, naming rules and export do not require an internet connection. The app does not contain an OCR engine; any software you separately use to prepare searchable PDFs has its own privacy policy.

Downloading releases, opening documentation links and restoring dependencies while building source use the relevant internet services. Those activities are separate from document processing. Choosing a network drive or a folder synchronized by another application can expose files to that service even though PaperStager itself does not upload them.

## Information kept locally

| Location | Contents and retention |
| --- | --- |
| Memory during a session | PDF bytes, rendered page images, extracted page text, paths, rules and review values. Closing the app ends the session; the app does not guarantee secure erasure of process memory, swap or operating-system caches. |
| Project JSON, only when you save it | Absolute source paths, source SHA-256 hashes, **extracted text from every imported page**, dimensions/rotation, document boundaries, manual field values, skip reasons and naming template. Source PDF bytes are not embedded. Approval is not saved. |
| Template JSON, only when you save it | Template name, filename pattern and field rules. It does not include source paths or extracted page text, but text you type into a rule may itself be sensitive. |
| Completed export folder | New PDFs plus `manifest.json` and a retained `recovery.json` staging journal. The manifest maps absolute source paths and hashes, page ranges, output names and hashes, and exported/skipped statuses and reasons. Filenames can contain the identifiers you selected. PDF page content may retain embedded metadata or other PDF objects copied by the writer. |
| `.paperstager-recovery-…` folder after an interrupted export | The last successfully written recovery journal, any PDFs already staged, and possibly incomplete output or a temporary JSON file. These files are not automatically deleted or resumed. |
| Operating-system temporary directory | Choosing **Try a synthetic example** creates `paperstager-demo-…/synthetic-batch.pdf`. This contains only fictitious example data. The app does not automatically remove these demo folders. |

There is no automatic project save, private document database or app-managed persistent thumbnail cache. You choose where to save projects, templates and exports. Explicit project/template saves can replace the JSON file you select; PDF export always creates a new batch folder and never overwrites source PDFs or previous outputs.

Projects, manifests and recovery journals are readable JSON, **not encrypted storage**. Anyone with access to those files may read their text and paths without opening the original PDF. PaperStager relies on your filesystem permissions and any disk encryption you use; it does not manage keys, securely shred files or provide retention controls.

## Errors and sharing

The UI displays local error messages, which can include paths. The app enables Avalonia diagnostic trace output but does not implement automatic diagnostic upload. A debugger, terminal, operating-system crash reporter or separately enabled system logging may capture diagnostics. Automated developer smoke checks also write synthetic test artifacts to their requested output directory.

Before sharing a screenshot, project, manifest, recovery journal or error report, inspect it for private text, filenames, paths and identifiers. Prefer a small synthetic PDF that reproduces the problem. Do not attach personal or confidential documents to a public issue.

## Removing data

**Remove from this project** removes the app's reference, not the source file. Closing without saving discards unsaved review state. To remove persistent app-created data, delete the projects, templates, exports, recovery folders and demo folders you no longer need using your file manager. PaperStager does not delete originals, synchronize deletions, clear operating-system backups or remove copies made by other software.
