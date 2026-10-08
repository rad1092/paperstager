using System.Security.Cryptography;
using PaperStager.Core;
using Xunit;

namespace PaperStager.Core.Tests;

public sealed class ImportAndSplitTests
{
    [Fact]
    public async Task ImportReadsEveryPageTextGeometryRotationAndHashWithoutChangingSource()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.CreatePdf(rotatedPage: 4);
        var before = await File.ReadAllBytesAsync(path);
        var source = await new PdfImportService().ImportAsync(path);

        Assert.Equal(path, source.Path);
        Assert.False(string.IsNullOrWhiteSpace(source.Id));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(before)), source.Sha256, ignoreCase: true);
        Assert.Equal([1, 2, 3, 4], source.Pages.Select(p => p.Number));
        Assert.Contains("Customer: Acme", source.Pages[0].Text);
        Assert.Contains("Reference: A-100", source.Pages[0].Text);
        Assert.Contains("Continuation two", source.Pages[3].Text);
        Assert.Equal(612, source.Pages[0].WidthPoints);
        Assert.Equal(792, source.Pages[0].HeightPoints);
        Assert.Equal(90, source.Pages[3].Rotation);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("not a PDF")]
    [InlineData("%PDF-1.7\ntruncated")]
    public async Task MalformedInputIsRejectedWithoutChangingIt(string content)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.PathFor("broken.pdf");
        await File.WriteAllTextAsync(path, content);
        await Assert.ThrowsAnyAsync<Exception>(() => new PdfImportService().ImportAsync(path));
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task PasswordProtectedInputIsRejectedWithoutChangingIt()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.CreatePdf(encrypted: true);
        var before = await File.ReadAllBytesAsync(path);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => new PdfImportService().ImportAsync(path));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CancelledImportDoesNotReadOrChangeSource()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.CreatePdf();
        var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PdfImportService().ImportAsync(path, new CancellationToken(canceled: true)));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task BoundariesProduceInclusiveContiguousRangesAndIncludePageOne()
    {
        using var workspace = new TestWorkspace();
        var source = await new PdfImportService().ImportAsync(workspace.CreatePdf());
        var documents = DocumentService.Split(source, [3]);
        Assert.Collection(documents,
            doc => { Assert.Equal(1, doc.StartPage); Assert.Equal(2, doc.EndPage); },
            doc => { Assert.Equal(3, doc.StartPage); Assert.Equal(4, doc.EndPage); });
        Assert.All(documents, doc => Assert.Equal(source.Id, doc.SourceId));
        Assert.Equal(documents.Count, documents.Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public async Task EmptyBoundaryListMeansOneWholeDocument()
    {
        using var workspace = new TestWorkspace();
        var source = await new PdfImportService().ImportAsync(workspace.CreatePdf());
        var document = Assert.Single(DocumentService.Split(source, []));
        Assert.Equal(1, document.StartPage);
        Assert.Equal(4, document.EndPage);
    }
}
