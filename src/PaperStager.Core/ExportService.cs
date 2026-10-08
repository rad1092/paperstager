using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PaperStager.Core;

/// <summary>Separates PDF output writes for deterministic disk-failure tests.</summary>
public interface IExportFileWriter
{
    Task WriteAsync(string outputPath, byte[] pdfBytes, CancellationToken cancellationToken);
}

public sealed class ExportFailureException(string message, string recoveryDirectory, Exception innerException)
    : IOException(message, innerException)
{
    public string RecoveryDirectory { get; } = recoveryDirectory;
}

public sealed class ExportCancelledException(string message, string recoveryDirectory, CancellationToken token)
    : OperationCanceledException(message, token)
{
    public string RecoveryDirectory { get; } = recoveryDirectory;
}

public sealed class ExportService(IExportFileWriter? writer = null)
{
    private readonly IExportFileWriter _writer = writer ?? new ExportFileWriter();

    public Task<ExportResult> ExportAsync(ReviewProject project, ExportPlan plan, string destination, bool approved,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
        {
            if (!approved) throw new InvalidOperationException("Export requires explicit approval of the reviewed plan.");
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(plan);
            cancellationToken.ThrowIfCancellationRequested();
            // Snapshot both models before asynchronous work. UI edits cannot alter an in-progress export.
            var snapshot = JsonSerializer.Deserialize<ReviewProject>(JsonSerializer.SerializeToUtf8Bytes(project, JsonStore.Options), JsonStore.Options)!;
            var currentPlan = new PlanningService().CreatePlan(snapshot);
            if (currentPlan.HasErrors) throw new InvalidOperationException("Fix or skip documents marked for review before exporting.");
            if (!JsonSerializer.Serialize(plan, JsonStore.Options).Equals(JsonSerializer.Serialize(currentPlan, JsonStore.Options), StringComparison.Ordinal))
                throw new InvalidOperationException("The project or plan changed after review. Review the current names and approve again.");
            var destinationPath = Path.GetFullPath(destination);
            if (!Directory.Exists(destinationPath)) throw new DirectoryNotFoundException("The selected export folder does not exist.");
            var batchId = Guid.NewGuid().ToString("N");
            var finalFolderName = $"PaperStager-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{batchId[..8]}";
            var stagingPath = Path.Combine(destinationPath, $".paperstager-recovery-{batchId}");
            var finalPath = Path.Combine(destinationPath, finalFolderName);
            if (Directory.Exists(stagingPath) || Directory.Exists(finalPath) || File.Exists(stagingPath) || File.Exists(finalPath))
                throw new IOException("The generated batch folder already exists. Retry export to create another batch.");
            var sourceMap = snapshot.Sources.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var manifest = new ExportManifest
            {
                BatchId = batchId, FinalFolderName = finalFolderName,
                Documents = currentPlan.Documents.Select(x => new ManifestDocument
                {
                    DocumentId = x.Id, SourcePath = sourceMap[x.SourceId].Path, SourceSha256 = sourceMap[x.SourceId].Sha256,
                    StartPage = x.StartPage, EndPage = x.EndPage, FileName = x.FileName,
                    Status = x.Skip ? "skipped" : "pending", Message = x.Skip ? x.SkipReason : ""
                }).ToList()
            };
            ManifestDocument? active = null;
            Directory.CreateDirectory(stagingPath);
            try
            {
                await WriteJournalAsync(stagingPath, manifest, cancellationToken).ConfigureAwait(false);
                var recordMap = manifest.Documents.ToDictionary(x => x.DocumentId, StringComparer.Ordinal);
                foreach (var sourceGroup in currentPlan.Documents.Where(x => !x.Skip).GroupBy(x => x.SourceId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = sourceMap[sourceGroup.Key];
                    var sourceBytes = await PdfImportService.ReadSourceAsync(source.Path, cancellationToken).ConfigureAwait(false);
                    VerifySourceHash(source, sourceBytes);
                    using var sourceStream = new MemoryStream(sourceBytes, writable: false);
                    using var input = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Import);
                    if (input.SecuritySettings.IsEncrypted || input.PageCount != source.Pages.Count)
                        throw new InvalidDataException("The source PDF no longer matches the reviewed page data. Import it again.");
                    foreach (var document in sourceGroup)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        active = recordMap[document.Id];
                        using var output = new PdfDocument();
                        output.Info.Creator = "PaperStager";
                        output.Info.Title = Path.GetFileNameWithoutExtension(document.FileName);
                        for (var number = document.StartPage; number <= document.EndPage; number++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            output.AddPage(input.Pages[number - 1]);
                        }
                        using var outputBytes = new MemoryStream();
                        output.Save(outputBytes, closeStream: false);
                        var bytes = outputBytes.ToArray();
                        var outputPath = Path.Combine(stagingPath, document.FileName);
                        if (!string.Equals(Path.GetDirectoryName(outputPath), stagingPath, StringComparison.Ordinal))
                            throw new InvalidDataException("The output path escapes the batch folder.");
                        await _writer.WriteAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
                        active.OutputSha256 = PdfImportService.Hash(bytes);
                        active.Status = "staged";
                        await WriteJournalAsync(stagingPath, manifest, cancellationToken).ConfigureAwait(false);
                        active = null;
                    }
                }
                // Detect edits to originals during processing as well as before reading them.
                foreach (var source in currentPlan.Documents.Where(x => !x.Skip).Select(x => sourceMap[x.SourceId]).Distinct())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    VerifySourceHash(source, await PdfImportService.ReadSourceAsync(source.Path, cancellationToken).ConfigureAwait(false));
                }
                manifest.Status = "ready_to_commit";
                manifest.Message = "All PDFs are staged. A folder whose name begins .paperstager-recovery- is still uncommitted; keep or remove it after checking this record, then retry export from the project.";
                await WriteJournalAsync(stagingPath, manifest, cancellationToken).ConfigureAwait(false);
                // This manifest describes the final batch. The recovery journal remains authoritative
                // if the process stops between this write and the same-volume directory rename.
                var finalManifest = JsonSerializer.Deserialize<ExportManifest>(JsonSerializer.SerializeToUtf8Bytes(manifest, JsonStore.Options), JsonStore.Options)!;
                finalManifest.Status = "complete";
                finalManifest.Message = "Committed export. Source PDFs were read only; output filenames map to the source pages below.";
                foreach (var item in finalManifest.Documents.Where(x => x.Status == "staged")) item.Status = "exported";
                await JsonStore.SaveAsync(Path.Combine(stagingPath, "manifest.json"), finalManifest, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stagingPath, finalPath);
                // Once promoted the operation is complete, even if cancellation arrives now.
                return new ExportResult
                {
                    OutputDirectory = finalPath, ManifestPath = Path.Combine(finalPath, "manifest.json"),
                    ExportedCount = currentPlan.Documents.Count(x => !x.Skip), SkippedCount = currentPlan.Documents.Count(x => x.Skip)
                };
            }
            catch (OperationCanceledException)
            {
                manifest.Status = "cancelled";
                manifest.Message = "Export was cancelled. No final batch was committed. This recovery folder may contain staged PDFs; originals were not changed. Review the project and export again to a new batch.";
                if (active is not null) { active.Status = "cancelled"; active.Message = "The current PDF may be incomplete; do not treat it as an exported document."; }
                await BestEffortJournalAsync(stagingPath, manifest).ConfigureAwait(false);
                throw new ExportCancelledException($"Export cancelled. No final batch was committed. Recovery record: {stagingPath}", stagingPath, cancellationToken);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                manifest.Status = "failed";
                manifest.Message = $"Export failed: {ex.Message} No final batch was committed. Keep this folder for inspection or remove it manually, then export again from the reviewed project.";
                if (active is not null) { active.Status = "failed"; active.Message = ex.Message; }
                await BestEffortJournalAsync(stagingPath, manifest).ConfigureAwait(false);
                throw new ExportFailureException($"Export failed; no final batch was committed. {ex.Message} Recovery folder: {stagingPath}", stagingPath, ex);
            }
        }, cancellationToken);

    private static void VerifySourceHash(SourcePdf source, byte[] bytes)
    {
        if (!string.Equals(source.Sha256, PdfImportService.Hash(bytes), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Source '{Path.GetFileName(source.Path)}' changed after import. Import it again and review all boundaries and names.");
    }

    private static Task WriteJournalAsync(string directory, ExportManifest manifest, CancellationToken token) =>
        JsonStore.SaveAsync(Path.Combine(directory, "recovery.json"), manifest, token);

    private static async Task BestEffortJournalAsync(string directory, ExportManifest manifest)
    {
        // A full or disconnected disk may reject the update. Never replace the original failure;
        // the last durable journal and explicit recovery folder name still identify incomplete work.
        try { await WriteJournalAsync(directory, manifest, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed class ExportFileWriter : IExportFileWriter
    {
        public async Task WriteAsync(string outputPath, byte[] pdfBytes, CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
            await stream.WriteAsync(pdfBytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
    }
}
