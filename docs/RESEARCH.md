# Why PaperStager exists

Research checked 2026-10-08. This is a product rationale and dependency decision record, not a claim of measured market size or completed operating-system certification.

PaperStager is a standalone review desk for an existing PDF batch: inspect pages, correct document boundaries, choose several naming fields from the source text, review proposed filenames, then explicitly export separate PDFs to an ordinary folder. The opportunity is an accessible open-source desktop workflow across macOS, Windows and Linux. Splitting, content-based naming and approval before saving already exist elsewhere.

## Evidence of the task people need to complete

| Source | Observed need | Design consequence |
| --- | --- | --- |
| [NAPS2 discussion #35](https://github.com/cyanfish/naps2/discussions/35), August–September 2023 comments | Users requested an OCR-derived filename placeholder and barcode-derived batch names. One commenter offered to seek funding for implementation. | Naming should use document content, permit reusable templates and remain separate from scanner drivers. This is a qualitative signal, not a purchase commitment to PaperStager. |
| [Paperless-ngx discussion #1848](https://github.com/paperless-ngx/paperless-ngx/discussions/1848), October–November 2022 | Users wanted to inspect and split combined scans before consumption. An independent preprocessing tool that hands finished files to an ordinary consume folder was explicitly discussed. | Finish at a normal folder, with a source/page mapping. Do not require a document-management server or an integration account. |
| [User request about 300 invoices](https://www.reddit.com/r/software/comments/1d98aca/pdf_splitter_able_to_rename_files_from_data_in/), June 2024 | A user described splitting hundreds of invoices by a recurring marker and then spending time manually renaming them from invoice identifiers; they were willing to pay for a solution. | Boundary review and content-based filenames must form one short workflow. Missing identifiers must remain visible for correction. |

These are independent public reports of a repetitive task. They establish a plausible problem, but do not establish adoption, willingness to pay at a particular price, a total addressable market, or a promise that all source documents can be processed automatically.

## Existing products and the narrower position

| Product | Relevant overlap | Reason to build PaperStager |
| --- | --- | --- |
| [ScanRoute Local](https://automatalabs.ca/products/scanroute/) | Already splits, names, previews, commits and records output hashes. Its published requirements are Windows 10/11 x64; it uses one OCR zone, which can contain several extracted fields. It is sold through the Microsoft Store. The current product page limits sources/destinations to local folders and its added OCR text layer to upright scanned pages. | Offer a publicly maintained standalone C# app across three desktop operating systems, with naming rules over existing searchable text and independent fields. Review, offline use and source preservation are shared expectations, not unique inventions. |
| [AutoSplit](https://evermap.com/autosplit.asp) | Provides sophisticated content-based splitting, naming and batch operations. | Its vendor specifies Windows and the full Adobe Acrobat Standard/Pro application. PaperStager's basic workflow should run without Acrobat or a paid PDF SDK. |
| [NAPS2](https://github.com/cyanfish/naps2) | Open-source scanner application with OCR and PDF import/export on Windows, macOS and Linux. | Reuse existing PDF engines and accept PDFs produced by NAPS2. Concentrate on review, reusable naming and safe export rather than building scanner drivers or another general OCR application. |
| [Paperless-ngx](https://github.com/paperless-ngx/paperless-ngx) | A document-management system and a potential downstream destination. | Produce ordinary files suitable for manual handoff, without requiring Paperless installation or taking ownership of its consume directory. |

The initial target is a person who already has combined, preferably searchable PDFs and currently switches between a splitter and manual filename editing. The first release deliberately omits unattended processing, folder watchers, cloud upload, document accounts and PDF annotation migration. Users of a scanner or DMS should be able to retain those tools.

## Selected implementation

| Component | Pinned package/version | Role and reason |
| --- | --- | --- |
| Runtime | .NET 10 target | Current LTS; Microsoft lists support through 2028-11-14. .NET 8 and 9 reach end of support in November 2026. Self-contained release packages remove a separate runtime installation step. [Support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) |
| Desktop UI | Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter 12.1.3 | C# desktop UI, drag/drop and platform file pickers. The desktop minimum is .NET 8. The versioned dependencies use SkiaSharp 3.119.4. [Package](https://www.nuget.org/packages/Avalonia/12.1.3), [platform support](https://docs.avaloniaui.net/docs/supported-platforms), [graphics dependencies](https://www.nuget.org/packages/Avalonia.Skia/12.1.3) |
| Text extraction | PdfPig 0.1.16 | Existing text and word positions, without sending documents to a service. Use layout-aware extraction, not raw internal content order. Pin the version because upstream warns that pre-1.0 minor releases can change APIs. [Package](https://www.nuget.org/packages/PdfPig/0.1.16), [upstream guidance](https://github.com/UglyToad/PdfPig) |
| Page subset export | PDFsharp 6.2.4, Core flavor | Managed C# import/write path across the three platforms. Copy existing PDF pages; thumbnails are never used as export source. PDFsharp itself does not render PDF pages. [Package](https://www.nuget.org/packages/PDFsharp/6.2.4), [Core support](https://docs.pdfsharp.net/PDFsharp/Overview/Specifications.html), [rendering limitation](https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html) |
| Actual page thumbnails | NAPS2.Pdfium.Binaries 1.3.0 with a small C# P/Invoke adapter | Redistributable PDFium native binaries for macOS, Windows and Linux, including ARM64 assets. No scanner SDK or NAPS2 app is embedded. This package contains PDFium 152.0.7947.0. [Package](https://www.nuget.org/packages/NAPS2.Pdfium.Binaries/1.3.0), [exact source commit](https://github.com/cyanfish/naps2-pdfium/commit/2b189e0edae6c0b4f9bdd97681bde8eea8b44661) |

Native PDFium calls must be serialized: the [official public header](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdfview.h) states that its API is not thread safe. Keep thumbnail dimensions bounded and release document/page/bitmap handles on every path. The NAPS2 package uses `_macarm`, `_mac`, `_linuxarm`, `_linux`, `_winarm`, `_win64` and `_win32` output subdirectories, so its native loader must resolve the matching packaged asset explicitly. See its [MSBuild targets](https://github.com/cyanfish/naps2-pdfium/blob/2b189e0edae6c0b4f9bdd97681bde8eea8b44661/NAPS2.Pdfium.Binaries/NAPS2.Pdfium.Binaries.targets).

The current PDFium package is a reproducibly identified version, not a claim that it is the newest upstream PDFium. Native library updates require a new package/hash review, retained licenses and the same renderer smoke tests; a managed NuGet vulnerability result alone does not audit embedded native code.

## Alternatives considered

- The full [NAPS2.Sdk 1.4.0](https://www.nuget.org/packages/NAPS2.Sdk/1.4.0) supports multiple platforms and OCR, but requires an image backend and brings scanning-related dependencies. Its SDK is LGPL-2.1-or-later; the NAPS2 application is GPL-2.0-or-later. Those licenses must not be confused with the Apache-2.0 native-binary packaging project. NAPS2.Images.ImageSharp also depends on a [SixLabors ImageSharp 3.x package](https://github.com/cyanfish/naps2/blob/master/NAPS2.Images.ImageSharp/NAPS2.Images.ImageSharp.csproj), whose own [license conditions](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE) need separate consideration. None of these components is included in the initial app.
- [PdfPig.Rendering.Skia 0.1.16.4](https://www.nuget.org/packages/PdfPig.Rendering.Skia/0.1.16.4) can render through SkiaSharp and shares the text parser. It is a viable alternative, but PDFium was chosen for thumbnail rendering. Its upstream notes that system font substitution can differ across operating systems. [Renderer source](https://github.com/BobLd/PdfPig.Rendering.Skia)
- [PDFtoImage 5.4.0](https://www.nuget.org/packages/PDFtoImage/5.4.0) already wraps PDFium, but pulls SkiaSharp 4.150.1. Avalonia 12.1.3 uses SkiaSharp 3.119.4, so adopting it would require explicit managed/native compatibility validation rather than silently changing the UI's graphics engine.
- PdfPig's writer is not the page-preservation path: upstream lists copying annotations, metadata and document structure among its limitations. PDFsharp subset export still needs fixtures and explicit limitations; it must not be described as a lossless preservation of every PDF feature.

## Product and release checks

Searchable text is the first-release input for automatic field suggestions. Image-only pages still have real thumbnails and can be named manually; users may OCR a separate copy with NAPS2 first. OCR is not silently downloaded or run. PaperStager never invents invoice identifiers, dates or amounts.

Check mixed portrait/rotated pages, multiline and Korean text, missing fields, duplicate names, invalid paths, malformed/encrypted PDFs, multiple documents in one source, source hashes, cancelled/failed writes, saved-project reloads, and repeated GUI operations. A package's compatibility table does not substitute for Windows/Linux/macOS execution evidence. Signed/notarized status and unsupported PDF features belong in release limitations, with exact CI commit and artifact checksums recorded separately.

The [third-party notices](../THIRD-PARTY-NOTICES.md) and `third_party/` directory contain redistribution information. They are part of the release payload, not development-only documentation.
