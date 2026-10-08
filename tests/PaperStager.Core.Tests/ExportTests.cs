using System.Security.Cryptography;
using System.Text.Json;
using PaperStager.Core;
using Xunit;

namespace PaperStager.Core.Tests;

public sealed class ExportTests
{
    [Fact]
    public async Task ApprovalIsRequiredBeforeAnyOutputIsWritten()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var destination = workspace.DirectoryFor("exports");
        var plan = new PlanningService().CreatePlan(project);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExportService().ExportAsync(project, plan, destination, approved: false));
        Assert.False(Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any());
    }

    [Fact]
    public async Task SuccessfulExportPreservesSourcesAndMapsExactPageRangesAndOutputHashes()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var original = await File.ReadAllBytesAsync(project.Sources[0].Path);
        var destination = workspace.DirectoryFor("exports");
        Directory.CreateDirectory(destination);
        var existing = Path.Combine(destination, "keep-existing.txt");
        await File.WriteAllTextAsync(existing, "user-owned existing output");
        var plan = new PlanningService().CreatePlan(project);
        var result = await new ExportService().ExportAsync(project, plan, destination, approved: true);

        Assert.Equal(2, result.ExportedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(destination, Path.GetDirectoryName(result.OutputDirectory));
        Assert.Equal(result.OutputDirectory, Path.GetDirectoryName(result.ManifestPath));
        Assert.Equal("user-owned existing output", await File.ReadAllTextAsync(existing));
        Assert.Equal(original, await File.ReadAllBytesAsync(project.Sources[0].Path));
        var firstOutput = await new PdfImportService().ImportAsync(Path.Combine(result.OutputDirectory, "Acme_A-100.pdf"));
        var secondOutput = await new PdfImportService().ImportAsync(Path.Combine(result.OutputDirectory, "Beta_B-200.pdf"));
        Assert.Equal(2, firstOutput.Pages.Count);
        Assert.Equal(2, secondOutput.Pages.Count);
        Assert.Contains("Customer: Acme", firstOutput.Pages[0].Text);
        Assert.Contains("Continuation one", firstOutput.Pages[1].Text);
        Assert.Contains("Customer: Beta", secondOutput.Pages[0].Text);
        Assert.Contains("Continuation two", secondOutput.Pages[1].Text);

        var manifest = await ReadManifestAsync(result.ManifestPath);
        Assert.Equal(2, manifest.Documents.Count);
        Assert.DoesNotContain(manifest.Status, new[] { "staging", "failed", "cancelled" });
        foreach (var item in manifest.Documents)
        {
            Assert.Equal(project.Sources[0].Path, item.SourcePath);
            Assert.Equal(project.Sources[0].Sha256, item.SourceSha256);
            var planned = Assert.Single(plan.Documents, d => d.Id == item.DocumentId);
            Assert.Equal((planned.StartPage, planned.EndPage), (item.StartPage, item.EndPage));
            var bytes = await File.ReadAllBytesAsync(Path.Combine(result.OutputDirectory, item.FileName));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), item.OutputSha256, ignoreCase: true);
        }
    }

    [Fact]
    public async Task SkipIsRecordedWithReasonAndProducesNoPdfForThatDocument()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        project.Documents[1].Skip = true;
        project.Documents[1].SkipReason = "확인 보류";
        var plan = new PlanningService().CreatePlan(project);
        var result = await new ExportService().ExportAsync(project, plan, workspace.DirectoryFor("exports"), approved: true);
        Assert.Equal(1, result.ExportedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Single(Directory.EnumerateFiles(result.OutputDirectory, "*.pdf"));
        var skipped = Assert.Single((await ReadManifestAsync(result.ManifestPath)).Documents, d => d.DocumentId == project.Documents[1].Id);
        Assert.Equal("skipped", skipped.Status);
        Assert.Contains("확인 보류", skipped.Message);
    }

    [Fact]
    public async Task RepeatExportCreatesSeparateBatchesAndNeverOverwritesEarlierOutput()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var plan = new PlanningService().CreatePlan(project);
        var destination = workspace.DirectoryFor("exports");
        var service = new ExportService();
        var first = await service.ExportAsync(project, plan, destination, approved: true);
        var before = await File.ReadAllBytesAsync(first.ManifestPath);
        var second = await service.ExportAsync(project, plan, destination, approved: true);
        Assert.NotEqual(first.OutputDirectory, second.OutputDirectory);
        Assert.Equal(before, await File.ReadAllBytesAsync(first.ManifestPath));
        Assert.Equal(2, Directory.EnumerateDirectories(destination).Count());
    }

    [Fact]
    public async Task ChangedSourceHashBlocksExportBeforeCreatingACompletedBatch()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var plan = new PlanningService().CreatePlan(project);
        var changedPath = workspace.CreatePdf("source.pdf", ["Replaced after review"]);
        var changedBytes = await File.ReadAllBytesAsync(changedPath);
        var destination = workspace.DirectoryFor("exports");
        await Assert.ThrowsAnyAsync<Exception>(() => new ExportService().ExportAsync(project, plan, destination, approved: true));
        Assert.Equal(changedBytes, await File.ReadAllBytesAsync(changedPath));
        AssertNoCompletedBatch(destination);
    }

    [Fact]
    public async Task SourceChangedDuringOutputWritingBlocksPromotion()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var sourcePath = project.Sources[0].Path;
        var destination = workspace.DirectoryFor("exports");
        // Simulate another application modifying the original after the export read it.
        var writer = new ControlledWriter(afterFirstWrite: () => File.AppendAllTextAsync(sourcePath, "\n% synthetic external change\n"));
        var failure = await Assert.ThrowsAsync<ExportFailureException>(() =>
            new ExportService(writer).ExportAsync(project, new PlanningService().CreatePlan(project), destination, approved: true));
        Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.Equal(failure.RecoveryDirectory, Assert.Single(Directory.EnumerateDirectories(destination)));
        Assert.StartsWith(".paperstager-recovery-", Path.GetFileName(failure.RecoveryDirectory));
        var journal = await ReadManifestAsync(Path.Combine(failure.RecoveryDirectory, "recovery.json"));
        Assert.Equal("failed", journal.Status);
        Assert.DoesNotContain(journal.Documents, d => d.Status == "exported");
        Assert.NotEqual(project.Sources[0].Sha256,
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))));
    }

    [Fact]
    public async Task ChangedProjectRequiresReviewingANewPlan()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var plan = new PlanningService().CreatePlan(project);
        project.Documents[0].ManualFields["customer"] = "Changed after review";
        var destination = workspace.DirectoryFor("exports");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ExportService().ExportAsync(project, plan, destination, approved: true));
        AssertNoCompletedBatch(destination);
    }

    [Fact]
    public async Task TamperedPlanCannotEscapeDestination()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var plan = new PlanningService().CreatePlan(project);
        plan.Documents[0].FileName = "../../escape.pdf";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExportService().ExportAsync(project, plan, workspace.DirectoryFor("exports"), approved: true));
        Assert.False(File.Exists(workspace.PathFor("escape.pdf")));
    }

    [Fact]
    public async Task CancellationBeforeExportLeavesNoOutput()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var destination = workspace.DirectoryFor("exports");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ExportService().ExportAsync(project, new PlanningService().CreatePlan(project), destination,
                approved: true, new CancellationToken(canceled: true)));
        Assert.False(Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any());
    }

    [Fact]
    public async Task DiskFailureLeavesOnlyIdentifiedRecoveryDataAndSourceIsUnchanged()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var before = await File.ReadAllBytesAsync(project.Sources[0].Path);
        var destination = workspace.DirectoryFor("exports");
        var writer = new ControlledWriter(failOnWrite: 2);
        var failure = await Assert.ThrowsAsync<ExportFailureException>(() =>
            new ExportService(writer).ExportAsync(project, new PlanningService().CreatePlan(project), destination, approved: true));
        Assert.True(Directory.Exists(failure.RecoveryDirectory));
        Assert.Equal(failure.RecoveryDirectory, Assert.Single(Directory.EnumerateDirectories(destination)));
        var manifestPath = Assert.Single(Directory.EnumerateFiles(failure.RecoveryDirectory, "*.json"));
        var manifest = await ReadManifestAsync(manifestPath);
        Assert.Equal("failed", manifest.Status);
        Assert.False(string.IsNullOrWhiteSpace(manifest.Message));
        Assert.Equal(before, await File.ReadAllBytesAsync(project.Sources[0].Path));
    }

    [Fact]
    public async Task MidExportCancellationDoesNotPromotePartialBatch()
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var project = await workspace.CreateProjectAsync();
        var before = await File.ReadAllBytesAsync(project.Sources[0].Path);
        var destination = workspace.DirectoryFor("exports");
        var writer = new ControlledWriter(cancelAfterWrite: cancellation);
        var failure = await Assert.ThrowsAsync<ExportCancelledException>(() =>
            new ExportService(writer).ExportAsync(project, new PlanningService().CreatePlan(project), destination,
                approved: true, cancellation.Token));
        Assert.True(Directory.Exists(failure.RecoveryDirectory));
        Assert.Equal(failure.RecoveryDirectory, Assert.Single(Directory.EnumerateDirectories(destination)));
        var manifestPath = Assert.Single(Directory.EnumerateFiles(failure.RecoveryDirectory, "*.json"));
        var manifest = await ReadManifestAsync(manifestPath);
        Assert.Equal("cancelled", manifest.Status);
        Assert.Equal(before, await File.ReadAllBytesAsync(project.Sources[0].Path));
    }

    private static async Task<ExportManifest> ReadManifestAsync(string path) =>
        JsonSerializer.Deserialize<ExportManifest>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static void AssertNoCompletedBatch(string destination)
    {
        if (!Directory.Exists(destination)) return;
        foreach (var manifestPath in Directory.EnumerateFiles(destination, "*.json", SearchOption.AllDirectories))
        {
            var manifest = JsonSerializer.Deserialize<ExportManifest>(File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Contains(manifest!.Status, new[] { "staging", "failed", "cancelled" });
        }
        Assert.Empty(Directory.EnumerateFiles(destination, "*.pdf", SearchOption.TopDirectoryOnly));
    }

    private sealed class ControlledWriter(int failOnWrite = 0, CancellationTokenSource? cancelAfterWrite = null, Func<Task>? afterFirstWrite = null) : IExportFileWriter
    {
        private int writes;
        public async Task WriteAsync(string outputPath, byte[] pdfBytes, CancellationToken cancellationToken)
        {
            writes++;
            if (writes == failOnWrite) throw new IOException("Simulated disk full during synthetic export.");
            await using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await stream.WriteAsync(pdfBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            if (writes == 1 && afterFirstWrite is not null) await afterFirstWrite();
            cancelAfterWrite?.Cancel();
        }
    }
}
