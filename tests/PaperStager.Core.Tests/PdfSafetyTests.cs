using System.Security.Cryptography;
using System.Text;
using PaperStager.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PaperStager.Core.Tests;

public sealed class PdfSafetyTests
{
    private const string Canary = "UNIQUE_SECRET_ONLY_ON_SKIPPED_PAGE";

    [Fact]
    public async Task BuiltInDemoStaysWithinTheSupportedPdfProfile()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.PathFor("demo.pdf");
        SyntheticDemo.CreatePdf(path);
        var source = await new PdfImportService().ImportAsync(path);
        Assert.Equal(4, source.Pages.Count);
        Assert.Equal(90, source.Pages[3].Rotation);
    }

    [Fact]
    public void RawPdfSharpPageImportCanCopyHiddenSkippedPageContent()
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, AddCrossPageAnnotation);
        using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();
        output.AddPage(input.Pages[0]);
        var outputPath = workspace.PathFor("unsafe-raw-library-export.pdf");
        output.Save(outputPath);
        using var reopened = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.PageCount);
        // Counting visible pages or searching the compressed file bytes misses this leak.
        Assert.DoesNotContain(Canary, Encoding.Latin1.GetString(File.ReadAllBytes(outputPath)));
        Assert.Contains(Canary, ReadAllDecodedObjects(reopened));
    }

    [Fact]
    public void RawPdfSharpPageImportDropsUserUnitAndChangesPhysicalSize()
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf => pdf.Pages[0].Elements.SetReal("/UserUnit", 2));
        using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();
        output.AddPage(input.Pages[0]);
        var outputPath = workspace.PathFor("unsafe-scaled-export.pdf");
        output.Save(outputPath);
        using var reopened = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(2, input.Pages[0].Elements.GetReal("/UserUnit"));
        Assert.False(reopened.Pages[0].Elements.ContainsKey("/UserUnit"));
        Assert.Equal(input.Pages[0].MediaBox, reopened.Pages[0].MediaBox);
    }

    [Fact]
    public async Task AnnotationDestinationCannotImportSkippedContentIntoAppOutput()
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, AddCrossPageAnnotation);
        var before = await File.ReadAllBytesAsync(path);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImportService().ImportAsync(path));
        Assert.Contains("/Annots", failure.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        await AssertOldProjectExportRejected(workspace, path, "/Annots");
    }

    [Theory]
    [InlineData("/AcroForm")]
    [InlineData("/OCProperties")]
    [InlineData("/OpenAction")]
    [InlineData("/AA")]
    [InlineData("/Names")]
    [InlineData("/Outlines")]
    [InlineData("/StructTreeRoot")]
    [InlineData("/OutputIntents")]
    [InlineData("/Collection")]
    [InlineData("/Perms")]
    [InlineData("/UnexpectedCatalogExtension")]
    public async Task UnsupportedCatalogStructureIsRejectedAtImportAndExport(string key)
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf => pdf.Internals.Catalog.Elements[key] = new PdfDictionary(pdf));
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImportService().ImportAsync(path));
        Assert.Contains(key, failure.Message);
        await AssertOldProjectExportRejected(workspace, path, key);
    }

    [Theory]
    [InlineData("/Annots")]
    [InlineData("/AA")]
    [InlineData("/Group")]
    [InlineData("/Metadata")]
    [InlineData("/PieceInfo")]
    [InlineData("/UnexpectedPageExtension")]
    public async Task UnsupportedPageStructureIsRejectedAtImportAndExport(string key)
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf => pdf.Pages[1].Elements[key] = new PdfDictionary(pdf));
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImportService().ImportAsync(path));
        Assert.Contains(key, failure.Message);
        await AssertOldProjectExportRejected(workspace, path, key);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UnsupportedUserUnitIsRejectedRatherThanChangingPhysicalSize(double userUnit)
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf => pdf.Pages[0].Elements.SetReal("/UserUnit", userUnit));
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImportService().ImportAsync(path));
        Assert.Contains("/UserUnit", failure.Message);
        await AssertOldProjectExportRejected(workspace, path, "/UserUnit");
    }

    [Fact]
    public async Task PageReferenceHiddenInResourcesIsRejected()
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf => pdf.Pages[0].Resources.Elements["/Unexpected"] = pdf.Pages[1].Reference!);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImportService().ImportAsync(path));
        Assert.Contains("page reference", failure.Message, StringComparison.OrdinalIgnoreCase);
        await AssertOldProjectExportRejected(workspace, path, "page reference");
    }

    [Theory]
    [InlineData("/AA")]
    [InlineData("/EmbeddedFiles")]
    [InlineData("/EF")]
    [InlineData("/AF")]
    [InlineData("/OC")]
    [InlineData("/JavaScript")]
    public async Task UnsupportedNestedStructuresAreRejected(string key)
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf => pdf.Pages[0].Resources.Elements[key] = new PdfDictionary(pdf));
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImportService().ImportAsync(path));
        Assert.Contains(key, failure.Message);
        await AssertOldProjectExportRejected(workspace, path, key);
    }

    [Theory]
    [InlineData("/Action")]
    [InlineData("/Filespec")]
    [InlineData("/EmbeddedFile")]
    [InlineData("/OCG")]
    [InlineData("/OCMD")]
    [InlineData("/Sig")]
    public async Task UnsupportedNestedObjectTypesAreRejected(string type)
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf =>
        {
            var nested = new PdfDictionary(pdf);
            nested.Elements.SetName("/Type", type);
            pdf.Pages[0].Resources.Elements["/Nested"] = nested;
        });
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImportService().ImportAsync(path));
        Assert.Contains(type, failure.Message);
        await AssertOldProjectExportRejected(workspace, path, type);
    }

    [Fact]
    public async Task ActionWithoutOptionalTypeIsRejected()
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf =>
        {
            var action = new PdfDictionary(pdf);
            action.Elements.SetName("/S", "/Launch");
            pdf.Pages[0].Resources.Elements["/A"] = action;
        });
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImportService().ImportAsync(path));
        Assert.Contains("/Launch", failure.Message);
        await AssertOldProjectExportRejected(workspace, path, "/Launch");
    }

    [Fact]
    public async Task ComplexPageTransparencyGroupIsRejected()
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf =>
            pdf.Pages[0].Elements.GetDictionary("/Group")!.Elements["/CS"] =
                new PdfArray(pdf, new PdfName("/ICCBased"), new PdfDictionary(pdf)));
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfImportService().ImportAsync(path));
        Assert.Contains("/Group /CS", failure.Message);
        await AssertOldProjectExportRejected(workspace, path, "/Group /CS");
    }

    [Fact]
    public async Task PageWithoutGroupDoesNotAcquireNewTransparencySemanticsOnExport()
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf =>
        {
            pdf.Options.ColorMode = PdfColorMode.Undefined;
            foreach (var page in pdf.Pages) page.Elements.Remove("/Group");
        });
        var source = await new PdfImportService().ImportAsync(path);
        var project = ProjectFor(source);
        var result = await new ExportService().ExportAsync(project, new PlanningService().CreatePlan(project), workspace.DirectoryFor("no-group-exports"), true);
        using var output = PdfReader.Open(Assert.Single(Directory.EnumerateFiles(result.OutputDirectory, "*.pdf")), PdfDocumentOpenMode.Import);
        Assert.False(output.Pages[0].Elements.ContainsKey("/Group"));
        Assert.DoesNotContain(Canary, ReadAllDecodedObjects(output));
    }

    [Fact]
    public async Task OrdinaryPagesPreserveGeometryAndExcludeCanaryFromAllDecodedObjects()
    {
        using var workspace = new TestWorkspace();
        var path = CreateFixture(workspace, pdf =>
        {
            var page = pdf.Pages[0];
            page.Elements.SetReal("/UserUnit", 1);
            page.Rotate = 90;
            page.CropBox = new PdfRectangle(new XPoint(20, 30), new XPoint(592, 762));
            page.TrimBox = new PdfRectangle(new XPoint(30, 40), new XPoint(582, 752));
            page.BleedBox = new PdfRectangle(new XPoint(25, 35), new XPoint(587, 757));
            page.ArtBox = new PdfRectangle(new XPoint(35, 45), new XPoint(577, 747));
            var group = page.Elements.GetDictionary("/Group")!;
            group.Elements.SetName("/CS", "/DeviceCMYK");
            group.Elements.SetBoolean("/I", true);
            group.Elements.SetBoolean("/K", true);
        });
        var before = await File.ReadAllBytesAsync(path);
        var source = await new PdfImportService().ImportAsync(path);
        var project = ProjectFor(source);
        var result = await new ExportService().ExportAsync(project, new PlanningService().CreatePlan(project), workspace.DirectoryFor("safe-exports"), true);
        var outputPath = Assert.Single(Directory.EnumerateFiles(result.OutputDirectory, "*.pdf"));
        using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        using var output = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(1, output.PageCount);
        Assert.Equal(input.Pages[0].Rotate, output.Pages[0].Rotate);
        foreach (var key in new[] { "/MediaBox", "/CropBox", "/TrimBox", "/BleedBox", "/ArtBox" })
            Assert.Equal(input.Pages[0].Elements.GetRectangle(key), output.Pages[0].Elements.GetRectangle(key));
        var outputGroup = output.Pages[0].Elements.GetDictionary("/Group")!;
        Assert.Equal("/DeviceCMYK", outputGroup.Elements.GetName("/CS"));
        Assert.True(outputGroup.Elements.GetBoolean("/I"));
        Assert.True(outputGroup.Elements.GetBoolean("/K"));
        Assert.DoesNotContain(Canary, ReadAllDecodedObjects(output));
        Assert.Contains("PUBLIC_FIRST_PAGE", ReadAllDecodedObjects(output));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    private static string CreateFixture(TestWorkspace workspace, Action<PdfDocument> configure)
    {
        var seed = workspace.CreatePdf("seed.pdf", ["PUBLIC_FIRST_PAGE", Canary]);
        var path = workspace.PathFor("fixture.pdf");
        using var pdf = PdfReader.Open(seed, PdfDocumentOpenMode.Modify);
        configure(pdf);
        pdf.Save(path);
        return path;
    }

    private static void AddCrossPageAnnotation(PdfDocument pdf)
    {
        var annotation = new PdfDictionary(pdf);
        annotation.Elements.SetName("/Type", "/Annot");
        annotation.Elements.SetName("/Subtype", "/Link");
        annotation.Elements.SetRectangle("/Rect", new PdfRectangle(new XPoint(0, 0), new XPoint(100, 100)));
        annotation.Elements["/Dest"] = new PdfArray(pdf, pdf.Pages[1].Reference!, new PdfName("/Fit"));
        pdf.Internals.AddObject(annotation);
        pdf.Pages[0].Elements["/Annots"] = new PdfArray(pdf, annotation.Reference!);
    }

    private static ReviewProject ProjectFor(SourcePdf source)
    {
        var project = new ReviewProject
        {
            Sources = [source], Documents = DocumentService.Split(source, [1, 2]),
            Template = new NamingTemplate { Pattern = "reviewed-{document}" }
        };
        project.Documents[1].Skip = true;
        project.Documents[1].SkipReason = "Only the first page was approved.";
        return project;
    }

    private static async Task AssertOldProjectExportRejected(TestWorkspace workspace, string path, string feature)
    {
        // A project saved by an earlier app version (or manually constructed) must not bypass validation.
        var before = await File.ReadAllBytesAsync(path);
        var project = ProjectFor(new SourcePdf
        {
            Path = path, Sha256 = Convert.ToHexString(SHA256.HashData(before)),
            Pages = [new PdfPageInfo { Number = 1 }, new PdfPageInfo { Number = 2 }]
        });
        var destination = workspace.DirectoryFor("blocked-exports");
        var failure = await Assert.ThrowsAsync<ExportFailureException>(() =>
            new ExportService().ExportAsync(project, new PlanningService().CreatePlan(project), destination, true));
        Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.Contains(feature, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(destination, "*.pdf", SearchOption.AllDirectories));
        Assert.All(Directory.EnumerateDirectories(destination), folder => Assert.StartsWith(".paperstager-recovery-", Path.GetFileName(folder)));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    private static string ReadAllDecodedObjects(PdfDocument pdf)
    {
        var text = new StringBuilder();
        var pending = new Stack<PdfItem>(pdf.Internals.GetAllObjects());
        var seen = new HashSet<PdfItem>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var item))
        {
            if (!seen.Add(item)) continue;
            if (item is PdfReference reference) pending.Push(reference.Value);
            else if (item is PdfDictionary dictionary)
            {
                foreach (var child in dictionary.Elements.Values)
                    if (child is not null) pending.Push(child);
                if (dictionary.Stream is not null)
                    text.AppendLine(Encoding.Latin1.GetString(dictionary.Stream.UnfilteredValue));
            }
            else if (item is PdfArray array)
                foreach (var child in array.Elements) pending.Push(child);
            else text.AppendLine(item.ToString());
        }
        return text.ToString();
    }
}
