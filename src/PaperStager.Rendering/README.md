# PDF thumbnails

`PdfRenderer.Render(path, oneBasedPage, maxWidth: 260)` returns a top-down, opaque
BGRA byte array with an explicit stride. Run it on a worker thread. The source is
opened read-only and loaded as a memory snapshot, so Unicode paths do not depend
on the native platform filename encoding. PDFium preserves inherited page
rotation. All native operations are serialized, and document, page, bitmap,
pinned arrays, and PDFium's global state are released before returning.

This component invokes only PDFium's initialization, load, page measurement,
bitmap render, and cleanup APIs. It does not initialize forms, invoke document
actions/JavaScript, or supply external-resource callbacks. It renders existing
annotations but does not edit or relocate them.

Limits: a 512 MiB input snapshot; requested width 1–4096 pixels; height at most
4096 pixels while retaining the page aspect ratio (rounded to whole pixels).
Password-protected inputs that cannot be opened without a password produce a
readable `PdfRenderException`. Individual native rendering calls cannot be
canceled midway; callers should cancel between pages and discard stale results.
This is an in-process native renderer, not an operating-system sandbox.

## Native distribution

The exact NuGet dependency is `NAPS2.Pdfium.Binaries` 1.3.0. Its package targets
copy the native engine to the following application-relative paths:

| Platform / process architecture | Path |
| --- | --- |
| macOS arm64 | `_macarm/libpdfium.dylib` |
| macOS x64 | `_mac/libpdfium.dylib` |
| Linux arm64 | `_linuxarm/libpdfium.so` |
| Linux x64 | `_linux/libpdfium.so` |
| Windows arm64 | `_winarm/pdfium.dll` |
| Windows x64 | `_win64/pdfium.dll` |
| Windows x86 | `_win32/pdfium.dll` |

The resolver loads only that application-bundled path. RID-specific publishing
must retain its matching native directory; moving the managed DLL alone is not
a complete deployment. The NuGet packaging is Apache-2.0; PDFium and its bundled
third-party components carry separate licenses. Release packages must include
the repository's `third_party` notices.

Sources: [package source at its published commit](https://github.com/cyanfish/naps2-pdfium/tree/2b189e0edae6c0b4f9bdd97681bde8eea8b44661),
[package targets](https://github.com/cyanfish/naps2-pdfium/blob/2b189e0edae6c0b4f9bdd97681bde8eea8b44661/NAPS2.Pdfium.Binaries/NAPS2.Pdfium.Binaries.targets),
[PDFium API](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdfview.h),
[upstream prebuilt PDFium](https://github.com/bblanchon/pdfium-binaries).
