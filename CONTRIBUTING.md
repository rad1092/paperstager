# Contributing

PaperStager is a C#/.NET desktop app. These rules also guide AI coding agents
working in this repository.

- Only edit this repository. Source PDFs must never be changed.
- Export requires explicit approval of a reviewed plan; never overwrite existing files.
- Use synthetic documents only. Never commit build caches, credentials or private PDFs.
- Keep the scope honest: naming rules read an existing text layer. Do not
  describe PaperStager as an OCR or scanning tool unless that ships.
- Run the core tests, the headless desktop tests and the package launch smoke
  before a release.

`global.json` pins .NET SDK 10.0.401: the lock files record the SDK's trimming package version, so a
different SDK fails the locked restore.

```sh
dotnet restore PaperStager.slnx --locked-mode
dotnet build PaperStager.slnx -c Release --no-restore
dotnet test PaperStager.slnx -c Release --no-build
```

Report issues without attaching private PDFs; a reduced synthetic reproduction is ideal.
