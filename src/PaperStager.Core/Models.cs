using System.Text.Json.Serialization;

namespace PaperStager.Core;

public sealed class SourcePdf
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public List<PdfPageInfo> Pages { get; set; } = [];
}

public sealed class PdfPageInfo
{
    public int Number { get; set; }
    public string Text { get; set; } = "";
    public double WidthPoints { get; set; }
    public double HeightPoints { get; set; }
    public int Rotation { get; set; }
}

public sealed class ReviewProject
{
    public int Version { get; set; } = 1;
    public List<SourcePdf> Sources { get; set; } = [];
    public List<DocumentDraft> Documents { get; set; } = [];
    public NamingTemplate Template { get; set; } = new();
}

public sealed class DocumentDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceId { get; set; } = "";
    public int StartPage { get; set; } = 1;
    public int EndPage { get; set; } = 1;
    public Dictionary<string, string> ManualFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Skip { get; set; }
    public string SkipReason { get; set; } = "";
}

[JsonConverter(typeof(JsonStringEnumConverter<FieldRuleKind>))]
public enum FieldRuleKind { AfterLabel, Regex }

public sealed class NamingTemplate
{
    public string Name { get; set; } = "Untitled";
    public string Pattern { get; set; } = "{document}";
    public List<FieldRule> Fields { get; set; } = [];
}

public sealed class FieldRule
{
    public string Name { get; set; } = "";
    public FieldRuleKind Kind { get; set; }
    public string Expression { get; set; } = "";
    public bool Required { get; set; } = true;
}

public sealed class ExportPlan
{
    public List<PlannedDocument> Documents { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public string ProjectFingerprint { get; set; } = "";
    [JsonIgnore] public bool HasErrors => Errors.Count > 0 || Documents.Any(x => x.Errors.Count > 0);
}

public sealed class PlannedDocument
{
    public string Id { get; set; } = "";
    public string SourceId { get; set; } = "";
    public int StartPage { get; set; }
    public int EndPage { get; set; }
    public string FileName { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Errors { get; set; } = [];
    public bool Skip { get; set; }
    public string SkipReason { get; set; } = "";
}

public sealed class ExportResult
{
    public string OutputDirectory { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public int ExportedCount { get; init; }
    public int SkippedCount { get; init; }
}

public sealed class ExportManifest
{
    public int Version { get; set; } = 1;
    public string BatchId { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "staging";
    public string Message { get; set; } = "Incomplete staging folder; this is not a committed export.";
    public string FinalFolderName { get; set; } = "";
    public List<ManifestDocument> Documents { get; set; } = [];
}

public sealed class ManifestDocument
{
    public string DocumentId { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public int StartPage { get; set; }
    public int EndPage { get; set; }
    public string FileName { get; set; } = "";
    public string OutputSha256 { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Message { get; set; } = "";
}
