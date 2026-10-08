using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PaperStager.Core;

/// <summary>
/// Conservative support boundary for ordinary page copying. PDFsharp AddPage copies the
/// transitive closure of resources, contents and annotations; it does not preserve all
/// page or catalog semantics. Reject unsupported structures instead of silently losing
/// their meaning or pulling another page into a supposedly separate document.
/// </summary>
internal static class PdfStructureGuard
{
    private const int MaximumVisitedItems = 1_000_000;
    private static readonly HashSet<string> CatalogKeys = new(StringComparer.Ordinal)
    {
        "/Type", "/Pages", "/Version", "/Metadata", "/Lang", "/PageLayout", "/PageMode", "/ViewerPreferences"
    };
    private static readonly HashSet<string> PageKeys = new(StringComparer.Ordinal)
    {
        "/Type", "/Parent", "/Resources", "/Contents", "/MediaBox", "/CropBox", "/Rotate",
        "/BleedBox", "/TrimBox", "/ArtBox", "/UserUnit", "/Group"
    };
    private static readonly HashSet<string> PageTreeKeys = new(StringComparer.Ordinal)
    {
        "/Type", "/Parent", "/Kids", "/Count", "/Resources", "/MediaBox", "/CropBox", "/Rotate"
    };
    private static readonly HashSet<string> UnsupportedKeys = new(StringComparer.Ordinal)
    {
        "/Annots", "/AcroForm", "/XFA", "/OCProperties", "/OC", "/AA", "/OpenAction",
        "/JavaScript", "/JS", "/EmbeddedFiles", "/EF", "/AF", "/Collection", "/RichMediaContent",
        "/RichMediaSettings", "/Ref", "/OPI", "/Alternates", "/PresSteps"
    };
    private static readonly HashSet<string> UnsupportedTypes = new(StringComparer.Ordinal)
    {
        "/Annot", "/Action", "/Filespec", "/EmbeddedFile", "/OCG", "/OCMD", "/Sig"
    };
    private static readonly HashSet<string> ActionKinds = new(StringComparer.Ordinal)
    {
        "/GoTo", "/GoToR", "/GoToE", "/Launch", "/Thread", "/URI", "/Sound", "/Movie",
        "/Hide", "/Named", "/SubmitForm", "/ResetForm", "/ImportData", "/JavaScript",
        "/SetOCGState", "/Rendition", "/Trans", "/GoTo3DView"
    };
    private static readonly HashSet<string> GroupKeys = new(StringComparer.Ordinal) { "/Type", "/S", "/CS", "/I", "/K" };

    public static void Validate(PdfDocument document, CancellationToken cancellationToken)
    {
        RejectUnknownKeys(document.Internals.Catalog, CatalogKeys, "document catalog");
        // Inspect exactly the structures AddPage copies, independently of the ordinary
        // parent page tree. A reference to any page/catalog here imports its whole closure.
        foreach (var page in document.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectUnknownKeys(page, PageKeys, "page");
            _ = GetSimplePageGroup(page);
            Walk(page.Elements.Where(pair => pair.Key is not "/Parent" and not "/Type")
                .Select(pair => pair.Value).OfType<PdfItem>(), rejectPageReferences: true, cancellationToken);
        }
        Walk(document.Internals.GetAllObjects(), rejectPageReferences: false, cancellationToken);
    }

    public static void PreservePageGroup(PdfPage input, PdfPage output)
    {
        if (GetSimplePageGroup(input) is not { } source) return;
        var group = new PdfDictionary(output.Owner);
        // Validation restricts these values to direct names/booleans, so no foreign
        // object references can enter the destination through this explicit copy.
        foreach (var pair in source.Elements) group.Elements[pair.Key] = pair.Value!.Clone();
        output.Elements["/Group"] = group;
    }

    private static PdfDictionary? GetSimplePageGroup(PdfPage page)
    {
        if (!page.Elements.ContainsKey("/Group")) return null;
        var group = page.Elements.GetDictionary("/Group");
        if (group is null || group.Stream is not null || group.Elements.GetName("/S") != "/Transparency")
            throw Unsupported("/Group (only ordinary transparency groups are supported)", "page");
        RejectUnknownKeys(group, GroupKeys, "page /Group");
        foreach (var pair in group.Elements)
        {
            var valid = pair.Key switch
            {
                "/Type" => pair.Value is PdfName { Value: "/Group" },
                "/S" => pair.Value is PdfName { Value: "/Transparency" },
                "/CS" => pair.Value is PdfName { Value: "/DeviceRGB" or "/DeviceCMYK" or "/DeviceGray" },
                "/I" or "/K" => pair.Value is PdfBoolean,
                _ => false
            };
            if (!valid) throw Unsupported($"/Group {pair.Key} (complex group attributes)", "page");
        }
        return group;
    }

    private static void Walk(IEnumerable<PdfItem> roots, bool rejectPageReferences, CancellationToken token)
    {
        var pending = new Stack<PdfItem>(roots);
        var seen = new HashSet<PdfItem>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(item)) continue;
            if (seen.Count > MaximumVisitedItems)
                throw Unsupported("object graph complexity limit", "PDF");
            switch (item)
            {
                case PdfReference reference:
                    pending.Push(reference.Value);
                    break;
                case PdfArray array:
                    foreach (var child in array.Elements) pending.Push(child);
                    break;
                case PdfDictionary dictionary:
                    var type = dictionary.Elements.GetName("/Type");
                    if (rejectPageReferences && (dictionary is PdfPage || type is "/Page" or "/Pages" or "/Catalog"))
                        throw Unsupported("cross-page reference in copied content or resources", "page");
                    if (type == "/Page") RejectUnknownKeys(dictionary, PageKeys, "page");
                    if (type == "/Pages") RejectUnknownKeys(dictionary, PageTreeKeys, "page tree");
                    if (UnsupportedTypes.Contains(type)) throw Unsupported(type, "object type");
                    if (dictionary.Elements.ContainsKey("/S") && ActionKinds.Contains(dictionary.Elements.GetName("/S")))
                        throw Unsupported(dictionary.Elements.GetName("/S"), "action");
                    foreach (var pair in dictionary.Elements)
                    {
                        if (UnsupportedKeys.Contains(pair.Key)) throw Unsupported(pair.Key, "object");
                        // External stream data cannot be preserved as an ordinary local PDF.
                        if (dictionary.Stream is not null && pair.Key is "/F" or "/FFilter" or "/FDecodeParms")
                            throw Unsupported(pair.Key, "external stream");
                        if (pair.Value is not null) pending.Push(pair.Value);
                    }
                    if (dictionary.Elements.ContainsKey("/UserUnit") && dictionary.Elements.GetReal("/UserUnit") != 1d)
                        throw Unsupported("/UserUnit other than 1 (physical page scale)", "page");
                    break;
            }
        }
    }

    private static void RejectUnknownKeys(PdfDictionary dictionary, HashSet<string> allowed, string context)
    {
        foreach (var key in dictionary.Elements.Keys)
            if (!allowed.Contains(key)) throw Unsupported(key, context);
    }

    private static InvalidDataException Unsupported(string feature, string context) => new(
        $"Unsupported PDF {context} feature: {feature}. PaperStager cannot safely separate this PDF. " +
        "Create and inspect a flattened, ordinary PDF copy in a trusted PDF editor, then import that copy. The source was not changed.");
}
