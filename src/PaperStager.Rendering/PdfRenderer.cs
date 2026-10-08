using System.Reflection;
using System.Runtime.InteropServices;

namespace PaperStager.Rendering;

/// <summary>A top-down, opaque BGRA thumbnail. The caller owns the returned array.</summary>
public sealed record PageImage(int Width, int Height, int Stride, byte[] Bgra);

/// <summary>Reads PDF pages without modifying the source or running document actions.</summary>
public static class PdfRenderer
{
    private const int MaximumDimension = 4096;
    private const long MaximumDocumentBytes = 512L * 1024 * 1024;
    private const string LibraryName = "paperstager_pdfium";
    // PDFium requires serialization of all API calls, including initialization and cleanup.
    private static readonly object NativeLock = new();

    static PdfRenderer()
    {
        NativeLibrary.SetDllImportResolver(typeof(PdfRenderer).Assembly, ResolveLibrary);
    }

    /// <summary>
    /// Renders one page with its inherited PDF rotation. Width is at most maxWidth and height
    /// at most 4096 pixels. A document may be at most 512 MiB. Passwords are not accepted.
    /// </summary>
    /// <remarks>
    /// Run this synchronous operation away from the UI thread. Individual native renders
    /// cannot be interrupted; callers can cancel between pages and discard stale results.
    /// </remarks>
    public static PageImage Render(string path, int oneBasedPage, int maxWidth = 260)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (oneBasedPage < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedPage), "Page numbers start at 1.");
        if (maxWidth is < 1 or > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Thumbnail width must be between 1 and 4096 pixels.");

        lock (NativeLock)
        {
            // Keep the byte snapshot alive and fixed until the document has closed. Loading
            // from memory avoids platform-dependent native filename encoding and callbacks.
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length == 0)
                throw new PdfRenderException("The PDF is empty.");
            if (input.Length > MaximumDocumentBytes)
                throw new PdfRenderException("The PDF exceeds the 512 MiB thumbnail limit.");
            var bytes = new byte[checked((int)input.Length)];
            input.ReadExactly(bytes);

            var sourcePin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var pixelPin = default(GCHandle);
            nint document = 0, page = 0, bitmap = 0;
            var initialized = false;
            try
            {
                Native.FPDF_InitLibrary();
                initialized = true;
                document = Native.FPDF_LoadMemDocument64(sourcePin.AddrOfPinnedObject(), (nuint)bytes.Length, 0);
                if (document == 0)
                    throw LoadError(Native.FPDF_GetLastError());

                var pageCount = Native.FPDF_GetPageCount(document);
                if (pageCount < 1)
                    throw new PdfRenderException("The PDF has no readable pages.");
                if (oneBasedPage > pageCount)
                    throw new ArgumentOutOfRangeException(nameof(oneBasedPage), $"The PDF contains {pageCount} pages.");

                page = Native.FPDF_LoadPage(document, oneBasedPage - 1);
                if (page == 0)
                    throw new PdfRenderException($"PDF page {oneBasedPage} could not be read; it may be malformed or unsupported.");

                // These dimensions already include the page's intrinsic /Rotate value.
                var pageWidth = (double)Native.FPDF_GetPageWidthF(page);
                var pageHeight = (double)Native.FPDF_GetPageHeightF(page);
                if (!double.IsFinite(pageWidth) || !double.IsFinite(pageHeight) || pageWidth <= 0 || pageHeight <= 0)
                    throw new PdfRenderException($"PDF page {oneBasedPage} has invalid dimensions.");

                var scale = Math.Min(maxWidth / pageWidth, MaximumDimension / pageHeight);
                var width = Math.Clamp((int)Math.Round(pageWidth * scale), 1, maxWidth);
                var height = Math.Clamp((int)Math.Round(pageHeight * scale), 1, MaximumDimension);
                var stride = checked(width * 4);
                var pixels = new byte[checked(stride * height)];
                // Opaque white, including empty margins and transparent PDF backgrounds.
                Array.Fill(pixels, (byte)255);
                pixelPin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                bitmap = Native.FPDFBitmap_CreateEx(width, height, 4, pixelPin.AddrOfPinnedObject(), stride);
                if (bitmap == 0)
                    throw new PdfRenderException("PDFium could not allocate a thumbnail bitmap.");

                // 0 means no *additional* rotation. Render annotations, limit image caching.
                // No form-fill, JavaScript, launch, URI, or external-resource callbacks exist.
                Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 0x01 | 0x200);
                return new PageImage(width, height, stride, pixels);
            }
            catch (DllNotFoundException ex)
            {
                throw new PdfRenderException("The PDF thumbnail engine is missing or a native dependency could not be loaded. Reinstall the package for this operating system and architecture.", ex);
            }
            catch (BadImageFormatException ex)
            {
                throw new PdfRenderException("The PDF thumbnail engine does not match this operating system or processor architecture.", ex);
            }
            finally
            {
                if (bitmap != 0) Native.FPDFBitmap_Destroy(bitmap);
                if (pixelPin.IsAllocated) pixelPin.Free();
                if (page != 0) Native.FPDF_ClosePage(page);
                if (document != 0) Native.FPDF_CloseDocument(document);
                if (initialized) Native.FPDF_DestroyLibrary();
                sourcePin.Free();
            }
        }
    }

    private static PdfRenderException LoadError(nuint error) => error switch
    {
        2 => new PdfRenderException("The PDF could not be read."),
        3 => new PdfRenderException("The file is not a valid PDF or is malformed."),
        4 => new PdfRenderException("This PDF is encrypted and requires a password. Open an unencrypted copy instead."),
        5 => new PdfRenderException("This PDF uses an unsupported encryption or security scheme."),
        6 => new PdfRenderException("The PDF has missing or malformed page data."),
        _ => new PdfRenderException($"The PDF could not be opened by the thumbnail engine (error {error}).")
    };

    private static nint ResolveLibrary(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != LibraryName) return 0;
        var architecture = RuntimeInformation.ProcessArchitecture;
        var relativePath = (OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), architecture) switch
        {
            (true, _, _, Architecture.X64) => Path.Combine("_win64", "pdfium.dll"),
            (true, _, _, Architecture.X86) => Path.Combine("_win32", "pdfium.dll"),
            (true, _, _, Architecture.Arm64) => Path.Combine("_winarm", "pdfium.dll"),
            (_, true, _, Architecture.X64) => Path.Combine("_mac", "libpdfium.dylib"),
            (_, true, _, Architecture.Arm64) => Path.Combine("_macarm", "libpdfium.dylib"),
            (_, _, true, Architecture.X64) => Path.Combine("_linux", "libpdfium.so"),
            (_, _, true, Architecture.Arm64) => Path.Combine("_linuxarm", "libpdfium.so"),
            _ => throw new PlatformNotSupportedException($"PDF thumbnails are unsupported on {RuntimeInformation.OSDescription} / {architecture}.")
        };
        // Only load the application-bundled file. Never search the current directory/PATH.
        return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, relativePath));
    }

    private static class Native
    {
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern void FPDF_InitLibrary();
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern void FPDF_DestroyLibrary();
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern nint FPDF_LoadMemDocument64(nint data, nuint length, nint password);
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern nuint FPDF_GetLastError();
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern int FPDF_GetPageCount(nint document);
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern nint FPDF_LoadPage(nint document, int pageIndex);
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern float FPDF_GetPageWidthF(nint page);
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern float FPDF_GetPageHeightF(nint page);
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern nint FPDFBitmap_CreateEx(int width, int height, int format, nint firstScan, int stride);
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern void FPDF_RenderPageBitmap(nint bitmap, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern void FPDFBitmap_Destroy(nint bitmap);
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern void FPDF_ClosePage(nint page);
        [DllImport(LibraryName, ExactSpelling = true)]
        internal static extern void FPDF_CloseDocument(nint document);
    }
}

/// <summary>A readable failure to open or render a PDF thumbnail.</summary>
public sealed class PdfRenderException : IOException
{
    public PdfRenderException(string message) : base(message) { }
    public PdfRenderException(string message, Exception innerException) : base(message, innerException) { }
}
