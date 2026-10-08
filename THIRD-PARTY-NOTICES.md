# Third-party software notices

PaperStager uses the components below. Their respective owners retain their copyrights. PaperStager's own license does not replace these licenses. Distribute this file and the complete `third_party/` directory with binary releases, together with the applicable .NET runtime notices in a self-contained package.

## App and PDF dependencies

| Component | Version | License and retained text |
| --- | --- | --- |
| Avalonia UI packages | 12.1.3 | MIT; [license and upstream third-party notice collection](third_party/packages/Avalonia-12.1.3/) |
| Avalonia.Fonts.Inter wrapper | 12.1.3 | MIT under Avalonia's license; the embedded font has a separate license below. |
| Inter font | Embedded version 3.019, source revision `0a5106e0b` | SIL Open Font License 1.1; [font license and inspected font metadata](third_party/packages/Avalonia.Fonts.Inter-12.1.3/) |
| PdfPig | 0.1.16 | Apache-2.0; [license and NOTICES.txt](third_party/packages/PdfPig-0.1.16/) |
| PDFsharp, Core build | 6.2.4 | MIT; [license and included BigGustave notice](third_party/packages/PDFsharp-6.2.4/) |
| NAPS2.Pdfium.Binaries packaging | 1.3.0 | Apache-2.0; [package license](third_party/packages/NAPS2.Pdfium.Binaries-1.3.0/) |
| PDFium native engine and embedded dependencies | 152.0.7947.0 | PDFium's BSD-style and Apache terms, plus the component-specific texts in the [exact binary-release license collection](third_party/pdfium-152.0.7947.0/licenses/) |
| SkiaSharp managed bindings | 3.119.4 | MIT; [package license](third_party/packages/SkiaSharp-3.119.4/) |
| HarfBuzzSharp managed bindings | 8.3.1.3 | MIT; [package license](third_party/packages/HarfBuzzSharp-8.3.1.3/) |
| SkiaSharp and HarfBuzzSharp native assets | 3.119.4 / 8.3.1.3 | Upstream package license and compiled-component notices retained for each platform under `third_party/packages/`; includes FreeType, HarfBuzz, Skia and their dependencies. |
| Avalonia ANGLE Windows native assets | 2.1.27548.20260419 | BSD-style and third-party terms; [package and native dependency notices](third_party/packages/Avalonia.Angle.Windows.Natives-2.1.27548.20260419/) |
| MicroCom.Runtime | 0.11.6 | MIT; [license at the package's source revision](third_party/packages/MicroCom.Runtime-0.11.6/) |
| Tmds.DBus.Protocol | 0.94.1 | MIT; [license at the package's source revision](third_party/packages/Tmds.DBus.Protocol-0.94.1/) |
| Microsoft.Extensions.DependencyInjection.Abstractions / Microsoft.Extensions.Logging.Abstractions / System.Security.Cryptography.Pkcs | 8.0.2 / 8.0.3 / 8.0.1 | MIT and applicable third-party notices; exact package texts retained under `third_party/packages/`. |
| .NET self-contained runtime | Actual patch recorded by each package | MIT and third-party terms; [10.0.12 runtime notices](third_party/dotnet-runtime-10.0.12/) retained from the restored runtime. Packaging must also retain the notices for the exact runtime it ships. |

The [dependency inventory](third_party/dependency-inventory.json) records all 32 resolved app lock-file dependencies, their NuGet content hashes, source revisions, license declarations and retained notice hashes. Individual package download provenance is also retained in `third_party/packages/*/provenance.json` where available. Additional upstream license files are recorded in [upstream-license-provenance.json](third_party/upstream-license-provenance.json). The actual package lock files and published dependency manifest remain authoritative for a particular binary release. The notice collection conservatively includes assets for platforms that a particular desktop archive may not contain.

The ANGLE collection includes xxHash, Abseil, zlib/Chromium compression utilities, ASTC Encoder, RapidJSON and SPIR-V/Vulkan header notices selected from its [pinned build metadata](https://github.com/AvaloniaUI/angle/tree/1c89805903c1482166356d3b950d474973180e61). Upstream's RapidJSON license file also describes its separate `bin/jsonchecker` test component; PaperStager does not ship that test program. Retaining an upstream notice collection does not imply that every component mentioned in it is in every published archive.

## PDFium native binaries

The NAPS2 binary package's Apache license describes its packaging code; it does **not** replace licenses for PDFium or the libraries compiled into PDFium. The [NAPS2 packaging commit](https://github.com/cyanfish/naps2-pdfium/commit/2b189e0edae6c0b4f9bdd97681bde8eea8b44661) identifies version 152.0.7947.0. The corresponding [upstream binary release](https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium/7947) provides its compiled-component license texts.

For each of macOS ARM64/x64, Linux ARM64/x64 and Windows ARM64/x64/x86, the downloaded upstream library's Git blob hash was compared with the corresponding library recorded in that NAPS2 commit. All seven matched. Archive SHA-256, library SHA-256, Git blob hashes, license SHA-256 and platform mappings are recorded in [PDFium provenance.json](third_party/pdfium-152.0.7947.0/provenance.json). This identifies the license source; it is not an assertion that every architecture has been tested by PaperStager.

Retained notices cover PDFium, Abseil, Anti-Grain Geometry, fast_float, FreeType, ICU, Little CMS, libjpeg-turbo/Independent JPEG Group, OpenJPEG, libpng, libtiff, LLVM libc, simdutf and zlib. Different platform archives contain some byte-distinct versions of the same notice. All 24 distinct files are retained, with a short content-hash suffix to avoid collisions. No native executable or library is stored in `third_party/`; NuGet supplies the runtime binaries.

Required acknowledgments:

> This software is based in part on the work of the FreeType Team.

> This software is based in part on the work of the Independent JPEG Group.

PDFium and native third-party license notices must accompany redistributed binaries. Do not imply endorsement by Google, Foxit, NAPS2 or the other upstream projects. The complete component texts, rather than this summary, govern permissions and conditions.

## Distribution requirements

- **MIT and BSD-style components:** retain their copyright, permission/conditions and disclaimer texts with the distributed software. Include component notices, not just PaperStager's own license.
- **Apache-2.0 components:** provide the license, preserve relevant attribution/NOTICE material and identify modified upstream files if modifications are distributed. The initial app consumes upstream packages without modifying their source.
- **Inter / SIL OFL 1.1:** keep the font copyright and OFL text with the font, including when embedded in the app. The font is not sold separately; this project does not modify it or claim its name as its own.
- **Graphics native assets:** retain the license/notice files from the resolved SkiaSharp and HarfBuzzSharp native asset packages. Managed wrapper MIT notices alone are not a replacement for their compiled native dependencies' notices.
- **Self-contained .NET releases:** retain the .NET runtime's license and third-party notices for the actual runtime patch shipped. Runtime updates are the distributor's responsibility for self-contained applications. See the [.NET distribution policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) and [runtime source notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).
- **Package changes:** update the lock files, provenance and retained notices together. Verify that package scripts copy this document and `third_party/` into every archive or app distribution. Native license obligations are not established by a managed dependency vulnerability scan.

## NAPS2 SDK, scanner app and OCR are not bundled

PaperStager includes `NAPS2.Pdfium.Binaries`, not the NAPS2 scanner application or `NAPS2.Sdk`. The [NAPS2 repository](https://github.com/cyanfish/naps2#license) licenses its application under GPL-2.0-or-later and listed SDK/image/internal projects under LGPL-2.1-or-later. Those are separate from this binary packaging project's Apache-2.0 license.

A future SDK integration would need an LGPL compliance plan, including its license and notices, required library source availability and the recipient's ability to replace/relink the LGPL library as applicable; do not assume the application's own license satisfies those obligations. No paid PDF SDK, ImageSharp package, Tesseract engine, OCR language model or NAPS2 application is part of the initial distribution. Users can separately create searchable PDFs with software they choose.

## Development dependencies

Test runners, test SDKs and headless testing packages are development dependencies and must not be copied into the user application. If a future distribution includes them, retain their applicable license texts as well. Test-only dependencies are still subject to the repository's dependency/security checks.

Avalonia.BuildServices 11.3.2 is a build dependency in the app's lock file; its [MIT license](third_party/packages/Avalonia.BuildServices-11.3.2/) is retained as well. These notices do not assert Developer ID signing, Windows Authenticode signing or Apple notarization; the current project does not provide those credentials.
