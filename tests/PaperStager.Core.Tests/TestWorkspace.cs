using System.Text;
using PaperStager.Core;
using PdfSharp.Pdf;

namespace PaperStager.Core.Tests;

/// <summary>All files are synthetic and scoped to a unique test-owned temporary directory.</summary>
internal sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "paperstager-tests", Guid.NewGuid().ToString("N"));
    public TestWorkspace() => Directory.CreateDirectory(Root);
    public string PathFor(string name) => Path.Combine(Root, name);

    public string DirectoryFor(string name) => Directory.CreateDirectory(PathFor(name)).FullName;

    // Standard PDF fonts avoid any machine-installed font or platform rendering dependency.
    public string CreatePdf(string name = "source.pdf", string[]? pages = null, int rotatedPage = 0, bool encrypted = false)
    {
        pages ??= ["Customer: Acme\nReference: A-100", "Continuation one", "Customer: Beta\nReference: B-200", "Continuation two"];
        var path = PathFor(name);
        using var document = new PdfDocument();
        foreach (var (text, index) in pages.Select((text, index) => (text, index)))
        {
            var page = document.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(612);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(792);
            if (index + 1 == rotatedPage) page.Rotate = 90;
            var font = new PdfDictionary(document);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type1");
            font.Elements.SetName("/BaseFont", "/Helvetica");
            var fonts = new PdfDictionary(document);
            fonts.Elements["/F1"] = font;
            page.Resources.Elements["/Font"] = fonts;
            var contents = new StringBuilder("BT /F1 12 Tf 16 TL 48 730 Td\n");
            foreach (var line in text.Split('\n'))
            {
                var escaped = line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                contents.Append('(').Append(escaped).Append(") Tj T*\n");
            }
            contents.Append("ET\n");
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(contents.ToString()));
        }
        if (encrypted)
        {
            document.SecuritySettings.UserPassword = "synthetic-only";
            document.SecuritySettings.OwnerPassword = "synthetic-owner";
        }
        document.Save(path);
        return path;
    }

    public async Task<ReviewProject> CreateProjectAsync(int[]? starts = null)
    {
        var source = await new PdfImportService().ImportAsync(CreatePdf());
        return new ReviewProject
        {
            Sources = [source],
            Documents = DocumentService.Split(source, starts ?? [1, 3]),
            Template = new NamingTemplate
            {
                Name = "Synthetic reference",
                Pattern = "{customer}_{reference}",
                Fields =
                [
                    new FieldRule { Name = "customer", Kind = FieldRuleKind.AfterLabel, Expression = "Customer:" },
                    new FieldRule { Name = "reference", Kind = FieldRuleKind.Regex, Expression = @"Reference:\s*(?<value>[A-Z]-\d+)" }
                ]
            }
        };
    }

    public void Dispose()
    {
        // This path was created by this fixture; user documents are never cleanup targets.
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
