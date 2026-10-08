# PaperStager v0.1

## Objective
An open-source standalone desktop review desk for already scanned combined PDF batches. Import PDFs, inspect actual page thumbnails, edit document boundaries, extract multiple naming fields from searchable text, save templates and projects, inspect all names and errors, and explicitly approve export to ordinary folders. Every output has a source/page mapping. Originals are read only. This is not a scanner, DMS, PDF annotation migration tool or unattended pipeline.

## Decisions
C# is the app and processing language. Avalonia desktop on .NET 10; PDF dependencies are existing open-source engines with redistributable licenses. Text fields support independent literal labels or regular expression rules and manual correction. All auto suggestions require review. Optional OCR is deferred to importing an already OCRed PDF (e.g. from NAPS2) unless a distributable cross-platform engine can be shipped and tested. No model invents names or amounts.

Export writes into a newly created batch subfolder within the selected destination, so sources and existing output never get overwritten. Staged files and manifest are fully written before folder promotion; interruption leaves a clearly identified recovery folder. Source hash change blocks export. Documents can be skipped with a reason. Saved project contains local source paths and hashes, boundaries, edits and template, never approval state.

## Commands
- `dotnet restore PaperStager.slnx`
- `dotnet build PaperStager.slnx -c Release --no-restore`
- `dotnet test PaperStager.slnx -c Release --no-build`
- `dotnet run --project src/PaperStager.App`
- `dotnet publish src/PaperStager.App -c Release -r osx-arm64 --self-contained true`

## Structure and style
`src/PaperStager.Core` models/text/template/planning/export/persistence, `src/PaperStager.App` Avalonia desktop, `tests` synthetic integration and GUI tests, `docs` user/research/privacy/release evidence, `scripts` packaging. Nullable C#, explicit failure messages, async UI work, cancellation checkpoints.

```csharp
if (plan.HasErrors) throw new InvalidOperationException("Fix or skip documents marked for review.");
```

## Acceptance and testing
Multiple imported PDFs; original thumbnail/rotation; editable boundary before page; first-page text rule extraction and multi-page documents; manual values; Unicode/Korean and multiline values; invalid/malformed/encrypted input rejected clearly; portable names, traversal and duplicate detection; save/load template/project; cancel/back/close/restart; source hash preservation; manifest statuses; no partial final batch on failed/cancelled export; simulated disk failure; native launch plus headless UI interactions; Windows/Linux/macOS CI on exact release SHA. Unsupported annotations/forms/layers/actions/non-default UserUnit and unknown structural semantics fail closed at import and export; decoded-object canary regression proves the raw-library hidden-page hazard is blocked. No signing or notarization claim without evidence.

## Boundaries
Always preserve existing data, use synthetic fixtures, verify dependency licenses and security, report unsupported operations. Ask only for new accounts/credentials/payments or non-official code execution. Never modify sibling repositories, source documents, system security settings, or publish secrets. User already authorized repository creation/publication and multi-OS CI.
