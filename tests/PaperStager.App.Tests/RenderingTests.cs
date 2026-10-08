using System.Security.Cryptography;
using PaperStager.Core;
using PaperStager.Rendering;
using PdfSharp.Pdf;
using Xunit;

namespace PaperStager.App.Tests;

public sealed class RenderingTests
{
    [Fact]
    public async Task NativeRendererHandlesRotationUnicodeConcurrentCallsAndSourcePreservation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "paperstager-render-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "한글 synthetic.pdf");
            SyntheticDemo.CreatePdf(path);
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            var portrait = PdfRenderer.Render(path, 1);
            var rotated = PdfRenderer.Render(path, 4);
            Assert.Equal(260, portrait.Width);
            Assert.True(portrait.Height > portrait.Width);
            Assert.True(rotated.Width > rotated.Height);
            Assert.Equal(portrait.Width * 4, portrait.Stride);
            Assert.Equal(portrait.Stride * portrait.Height, portrait.Bgra.Length);
            Assert.Contains(portrait.Bgra.Where((_, i) => i % 4 != 3), b => b < 200);
            Assert.All(portrait.Bgra.Where((_, i) => i % 4 == 3), alpha => Assert.Equal(255, alpha));
            var repeat = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => PdfRenderer.Render(path, 1))));
            Assert.All(repeat, p => Assert.Equal(portrait.Bgra, p.Bgra));
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void NativeRendererReportsBadEncryptedAndOutOfRangeInputsAndRecovers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "paperstager-render-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var good = Path.Combine(directory, "synthetic.pdf");
            var bad = Path.Combine(directory, "malformed.pdf");
            var encrypted = Path.Combine(directory, "encrypted.pdf");
            SyntheticDemo.CreatePdf(good);
            File.WriteAllText(bad, "This is not a PDF.");
            Assert.Contains("malformed", Assert.Throws<PdfRenderException>(() => PdfRenderer.Render(bad, 1)).Message);
            File.WriteAllText(bad, "");
            Assert.Contains("empty", Assert.Throws<PdfRenderException>(() => PdfRenderer.Render(bad, 1)).Message);
            using (var document = new PdfDocument())
            {
                document.AddPage();
                document.SecuritySettings.UserPassword = "synthetic-only";
                document.SecuritySettings.OwnerPassword = "synthetic-owner";
                document.Save(encrypted);
            }
            Assert.Contains("encrypted", Assert.Throws<PdfRenderException>(() => PdfRenderer.Render(encrypted, 1)).Message);
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderer.Render(good, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderer.Render(good, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderer.Render(good, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderer.Render(good, 1, 4097));
            Assert.NotEmpty(PdfRenderer.Render(good, 1).Bgra);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
