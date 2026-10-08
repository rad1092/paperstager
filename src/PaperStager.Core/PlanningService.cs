using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PaperStager.Core;

public sealed class PlanningService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(150);
    private static readonly HashSet<string> ReservedFields = new(StringComparer.OrdinalIgnoreCase) { "document", "source", "page", "endpage" };
    private static readonly Regex TokenRegex = new(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant, RegexTimeout);

    public ExportPlan CreatePlan(ReviewProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ProjectValidation.ValidateStructure(project);
        var plan = new ExportPlan { ProjectFingerprint = Fingerprint(project) };
        var templateErrors = ValidateTemplate(project.Template);
        plan.Errors.AddRange(templateErrors);
        if (project.Documents.Count == 0) plan.Errors.Add("Import a PDF and define at least one document.");
        if (project.Documents.All(x => x.Skip)) plan.Errors.Add("At least one document must be selected for export.");
        var sourceMap = project.Sources.ToDictionary(x => x.Id, StringComparer.Ordinal);
        for (var index = 0; index < project.Documents.Count; index++)
        {
            var draft = project.Documents[index];
            var item = new PlannedDocument
            {
                Id = draft.Id, SourceId = draft.SourceId, StartPage = draft.StartPage, EndPage = draft.EndPage,
                Skip = draft.Skip, SkipReason = draft.SkipReason.Trim()
            };
            plan.Documents.Add(item);
            if (!sourceMap.TryGetValue(draft.SourceId, out var source))
            {
                item.Errors.Add("The source PDF is missing from this project.");
                continue;
            }
            if (draft.StartPage < 1 || draft.EndPage < draft.StartPage || draft.EndPage > source.Pages.Count)
            {
                item.Errors.Add("The document's page range is invalid.");
                continue;
            }
            if (draft.Skip)
            {
                if (item.SkipReason.Length == 0) item.Errors.Add("Give a reason for skipping this document.");
                continue;
            }
            if (templateErrors.Count > 0)
            {
                item.Errors.Add("Fix the naming template before previewing filenames.");
                continue;
            }
            item.Fields["document"] = (index + 1).ToString("D3", CultureInfo.InvariantCulture);
            item.Fields["source"] = Path.GetFileNameWithoutExtension(source.Path);
            item.Fields["page"] = draft.StartPage.ToString(CultureInfo.InvariantCulture);
            item.Fields["endpage"] = draft.EndPage.ToString(CultureInfo.InvariantCulture);
            if (templateErrors.Count == 0)
            {
                foreach (var rule in project.Template.Fields)
                {
                    string value;
                    var manual = draft.ManualFields.FirstOrDefault(x => string.Equals(x.Key, rule.Name, StringComparison.OrdinalIgnoreCase));
                    if (manual.Key is not null)
                        value = manual.Value;
                    else
                    {
                        try { value = Extract(source.Pages[draft.StartPage - 1].Text, rule); }
                        catch (RegexMatchTimeoutException)
                        {
                            item.Errors.Add($"Field '{rule.Name}' took too long. Simplify its expression or enter a manual value.");
                            value = "";
                        }
                        catch (ArgumentException)
                        {
                            item.Errors.Add($"Field '{rule.Name}' has an invalid regular expression.");
                            value = "";
                        }
                    }
                    value = NormalizeValue(value);
                    item.Fields[rule.Name] = value;
                    if (rule.Required && value.Length == 0) item.Errors.Add($"Required field '{rule.Name}' is missing.");
                }
            }
            var fileStem = TokenRegex.Replace(project.Template.Pattern, match =>
            {
                var name = match.Groups[1].Value;
                if (item.Fields.TryGetValue(name, out var value)) return value;
                item.Errors.Add($"Template field '{name}' is not defined.");
                return "";
            });
            // Braces are reserved exclusively for placeholders, never output paths.
            if (fileStem.Contains('{') || fileStem.Contains('}')) item.Errors.Add("Use balanced placeholders such as {reference} in the template.");
            item.FileName = fileStem.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? fileStem : fileStem + ".pdf";
            item.Errors.AddRange(PortableFileName.Validate(item.FileName));
        }
        foreach (var group in plan.Documents.Where(x => !x.Skip && x.FileName.Length > 0)
                     .GroupBy(x => x.FileName.Normalize(NormalizationForm.FormC), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            foreach (var item in group) item.Errors.Add("Duplicate filename. Change a field or add {document} to the template.");

        // Every imported page must be deliberately exported or deliberately skipped exactly once.
        foreach (var source in project.Sources)
        {
            var expected = 1;
            foreach (var doc in project.Documents.Where(x => x.SourceId == source.Id).OrderBy(x => x.StartPage))
            {
                if (doc.StartPage != expected)
                {
                    plan.Errors.Add($"Page ranges for '{Path.GetFileName(source.Path)}' overlap or leave unreviewed pages.");
                    break;
                }
                expected = doc.EndPage + 1;
            }
            if (expected != source.Pages.Count + 1)
                plan.Errors.Add($"Every page in '{Path.GetFileName(source.Path)}' must be assigned to a document or explicitly skipped.");
        }
        return plan;
    }

    public static IReadOnlyList<string> ValidateTemplate(NamingTemplate template)
    {
        var errors = new List<string>();
        if (template.Fields is null || template.Pattern is null || template.Name is null)
        {
            errors.Add("Template fields, name, and pattern must not be null.");
            return errors;
        }
        if (template.Pattern.Length is < 1 or > 500) errors.Add("Template pattern must contain 1–500 characters.");
        if (template.Fields.Count > 30) errors.Add("A template can contain at most 30 fields.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in template.Fields)
        {
            if (rule is null || rule.Name is null || rule.Expression is null)
            {
                errors.Add("A field rule is incomplete.");
                continue;
            }
            if (!Regex.IsMatch(rule.Name, @"\A[A-Za-z][A-Za-z0-9_]{0,39}\z", RegexOptions.CultureInvariant, RegexTimeout))
                errors.Add("Field names must start with a letter and use only letters, digits, and underscores (up to 40 characters).");
            if (!names.Add(rule.Name)) errors.Add($"Field '{rule.Name}' appears more than once.");
            if (ReservedFields.Contains(rule.Name)) errors.Add($"'{rule.Name}' is a built-in field and cannot be redefined.");
            if (rule.Expression.Length is < 1 or > 2000) errors.Add($"Field '{rule.Name}' needs an expression of 1–2000 characters.");
            if (!Enum.IsDefined(rule.Kind)) errors.Add($"Field '{rule.Name}' has an unsupported rule type.");
            if (rule.Kind == FieldRuleKind.Regex)
            {
                try { _ = new Regex(rule.Expression, RegexOptions.CultureInvariant | RegexOptions.Multiline, RegexTimeout); }
                catch (ArgumentException) { errors.Add($"Field '{rule.Name}' has an invalid regular expression."); }
            }
        }
        return errors;
    }

    private static string Extract(string text, FieldRule rule)
    {
        if (rule.Kind == FieldRuleKind.AfterLabel)
        {
            // Labels are literal and line anchored. A repeated label is ambiguous, so require review.
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            var values = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith(rule.Expression, StringComparison.OrdinalIgnoreCase)) continue;
                var remainder = trimmed[rule.Expression.Length..];
                if (rule.Expression.Length > 0 && char.IsLetterOrDigit(rule.Expression[^1]) && remainder.Length > 0 && char.IsLetterOrDigit(remainder[0])) continue;
                values.Add(remainder.TrimStart(' ', '\t', ':', '=', '：').Trim());
            }
            return values.Count == 1 ? values[0] : "";
        }
        var matches = Regex.Matches(text, rule.Expression, RegexOptions.CultureInvariant | RegexOptions.Multiline, RegexTimeout);
        if (matches.Count != 1) return "";
        var match = matches[0];
        return match.Groups["value"].Success ? match.Groups["value"].Value : match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

    private static string NormalizeValue(string value) => Regex.Replace(value.Trim().Normalize(NormalizationForm.FormC), @"\s+", " ", RegexOptions.CultureInvariant, RegexTimeout);
    internal static string Fingerprint(ReviewProject project) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(project, JsonStore.Options)));
}

public static class PortableFileName
{
    public static IReadOnlyList<string> Validate(string fileName)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) errors.Add("A filename cannot be empty.");
        if (fileName.Length > 180 || Encoding.UTF8.GetByteCount(fileName) > 220) errors.Add("Filename is too long (maximum 180 characters and 220 UTF-8 bytes).");
        if (fileName.Any(c => c < 32 || c is >= '\u007f' and <= '\u009f' || "<>:\"/\\|?*".Contains(c)))
            errors.Add("Filename contains a path separator or a character that is unsafe on Windows, macOS, or Linux.");
        if (fileName.Any(c => c is >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069')) errors.Add("Filename contains invisible text-direction control characters.");
        if (fileName.StartsWith('.') || fileName.EndsWith('.') || fileName.EndsWith(' ') || fileName != fileName.Trim()) errors.Add("Filename cannot start with a dot or have surrounding spaces or a trailing dot.");
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.EndsWith('.') || stem.EndsWith(' ')) errors.Add("Filename stem cannot end with a dot or space.");
        var deviceName = fileName.Split('.')[0].TrimEnd(' ', '.');
        if (Regex.IsMatch(deviceName, @"\A(CON|CONIN\$|CONOUT\$|CLOCK\$|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            errors.Add("Filename is reserved by Windows. Choose another value.");
        if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || fileName.Equals("recovery.json", StringComparison.OrdinalIgnoreCase)) errors.Add("Filename is reserved for the export record.");
        return errors;
    }
}
