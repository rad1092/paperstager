# Verification record

This is a preview release. Verification is against synthetic documents only; it cannot establish fidelity for every possible PDF.

- macOS arm64 / .NET10.0.401: Core 63 tests passed, including malformed/encrypted/searchable/rotated PDFs, Unicode, multiline extraction, duplicate/path traversal names, source changes, cancellation and simulated disk-write failure, recovery manifests and project/template round trips.
- Actual macOS native Avalonia window smoke: imported4pages, rendered4PDFium thumbnails, restored project/template, exported2documents with unchanged source SHA-256; success marker recorded. Headless UI suite and packaged multi-OS results are recorded after CI.
- Physical power failure/disk exhaustion, hostile filesystem race attacks and accessibility screen-reader parity are not established by these tests. Disk-write failure is injected. Individual native PDF parsing/render calls cannot be interrupted midway.
- Windows/Linux package launch is validated by GitHub Actions, not by a human interactive session on those desktops. Native macOS screenshots use only the generated synthetic demo.

See the exact commit's GitHub Actions run for machine-produced build/test/package logs. Release archives contain source-commit metadata and SHA-256 sidecars. No signing/notarization claim is made.
