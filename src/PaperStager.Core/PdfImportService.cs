using System.Security.Cryptography;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PaperStager.Core;

public sealed class PdfImportService
{
    public const long MaximumFileBytes = 256L * 1024 * 1024;
    public const int MaximumPages = 5000;

    public Task<SourcePdf> ImportAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            if (!fullPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Import requires a .pdf filename. Rename a separate copy if the PDF uses another extension.");
            var bytes = await ReadSourceAsync(fullPath, cancellationToken).ConfigureAwait(false);
            try
            {
                using var sharpStream = new MemoryStream(bytes, writable: false);
                using var sharp = PdfReader.Open(sharpStream, PdfDocumentOpenMode.Import);
                if (sharp.SecuritySettings.IsEncrypted)
                    throw new InvalidDataException("Encrypted PDFs are not supported. Import an unencrypted copy that you are authorized to use.");
                if (sharp.PageCount is < 1 or > MaximumPages)
                    throw new InvalidDataException($"Import requires 1–{MaximumPages} pages.");
                PdfStructureGuard.Validate(sharp, cancellationToken);
                using var pig = UglyToad.PdfPig.PdfDocument.Open(bytes);
                if (sharp.PageCount != pig.NumberOfPages)
                    throw new InvalidDataException("The PDF engines disagree about the page count. Repair a separate copy before importing.");
                var result = new SourcePdf { Path = fullPath, Sha256 = Hash(bytes) };
                for (var number = 1; number <= pig.NumberOfPages; number++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = pig.GetPage(number);
                    result.Pages.Add(new PdfPageInfo
                    {
                        Number = number,
                        Text = ContentOrderTextExtractor.GetText(page),
                        WidthPoints = page.Width,
                        HeightPoints = page.Height,
                        Rotation = sharp.Pages[number - 1].Rotate
                    });
                }
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new InvalidDataException("Cannot import this PDF. It may be malformed, encrypted, or use an unsupported feature. The source was not changed.", ex);
            }
        }, cancellationToken);

    internal static async Task<byte[]> ReadSourceAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumFileBytes)
            throw new InvalidDataException("This PDF exceeds the 256 MiB import limit. Split a separate copy before importing.");
        if (stream.Length < 8)
            throw new InvalidDataException("The file is too short to be a valid PDF.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.Length != bytes.Length)
            throw new IOException("The source changed while it was being read. Import it again.");
        return bytes;
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

public static class DocumentService
{
    public static List<DocumentDraft> Split(SourcePdf source, IEnumerable<int> startPages)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(startPages);
        if (source.Pages.Count == 0) throw new ArgumentException("The source contains no pages.", nameof(source));
        var starts = startPages.Append(1).Distinct().Order().ToArray();
        if (starts.Any(x => x < 1 || x > source.Pages.Count))
            throw new ArgumentOutOfRangeException(nameof(startPages), "A boundary must be an existing page number.");
        return starts.Select((start, index) => new DocumentDraft
        {
            SourceId = source.Id,
            StartPage = start,
            EndPage = index + 1 == starts.Length ? source.Pages.Count : starts[index + 1] - 1
        }).ToList();
    }
}
