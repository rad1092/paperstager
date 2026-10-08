using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace PaperStager.Core;

/// <summary>Creates a clearly marked, entirely synthetic four-page intake example.</summary>
public static class SyntheticDemo
{
    public static void CreatePdf(string path)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var number = 1; number <= 4; number++)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            var client = number <= 2 ? "Northwind" : "Meadow";
            var reference = number <= 2 ? "INV-1001" : "INV-1002";
            page.AddText("PAPERSTAGER / SYNTHETIC DEMO", 21, new PdfPoint(50, 760), font);
            page.AddText($"Client: {client}", 16, new PdfPoint(50, 695), font);
            page.AddText($"Reference: {reference}", 16, new PdfPoint(50, 662), font);
            page.AddText("Date: 2026-10-08", 16, new PdfPoint(50, 629), font);
            page.DrawLine(new PdfPoint(50, 603), new PdfPoint(545, 603), 1);
            page.AddText($"Document page {(number % 2 == 0 ? 2 : 1)} of 2 / Batch page {number}", 12, new PdfPoint(50, 575), font);
            page.AddText("This is sample data. It contains no real customer information.", 11, new PdfPoint(50, 535), font);
            page.AddText("Review the boundary before page 3, then verify both output names.", 11, new PdfPoint(50, 510), font);
        }
        using var input = new MemoryStream(builder.Build(), writable: false);
        using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        pdf.Pages[3].Rotate = 90;
        pdf.Info.Title = "PaperStager synthetic intake example";
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        pdf.Save(output, closeStream: false);
        output.Flush(flushToDisk: true);
    }

    public static NamingTemplate DemoTemplate() => new()
    {
        Name = "Synthetic invoice labels",
        Pattern = "{date}_{client}_{reference}",
        Fields = [
            new() { Name = "client", Kind = FieldRuleKind.AfterLabel, Expression = "Client:" },
            new() { Name = "reference", Kind = FieldRuleKind.AfterLabel, Expression = "Reference:" },
            new() { Name = "date", Kind = FieldRuleKind.AfterLabel, Expression = "Date:" }
        ]
    };
}
