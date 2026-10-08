using System.Text.Json;

namespace PaperStager.Core;

public sealed class ProjectStore
{
    public Task SaveAsync(string path, ReviewProject project, CancellationToken cancellationToken = default)
    {
        ProjectValidation.ValidateStructure(project);
        var fullPath = Path.GetFullPath(path);
        if (project.Sources.Any(x => string.Equals(Path.GetFullPath(x.Path), fullPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A project cannot be saved over a source PDF.");
        return JsonStore.SaveAsync(path, project, cancellationToken);
    }

    public async Task<ReviewProject> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var project = await JsonStore.LoadAsync<ReviewProject>(path, cancellationToken).ConfigureAwait(false);
        ProjectValidation.ValidateStructure(project);
        return project;
    }
}

public sealed class TemplateStore
{
    public Task SaveAsync(string path, NamingTemplate template, CancellationToken cancellationToken = default)
    {
        Validate(template);
        return JsonStore.SaveAsync(path, template, cancellationToken);
    }

    public async Task<NamingTemplate> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var template = await JsonStore.LoadAsync<NamingTemplate>(path, cancellationToken).ConfigureAwait(false);
        Validate(template);
        return template;
    }

    private static void Validate(NamingTemplate template)
    {
        var errors = PlanningService.ValidateTemplate(template);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));
    }
}

internal static class JsonStore
{
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 32 };
    private const long MaximumJsonBytes = 64L * 1024 * 1024;

    internal static async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Projects and templates must use a .json extension to protect document files.");
        var parent = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Choose an existing folder before saving.");
        cancellationToken.ThrowIfCancellationRequested();
        var temporary = Path.Combine(parent, $".paperstager-save-{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
            if (bytes.LongLength > MaximumJsonBytes) throw new InvalidDataException("Project exceeds the 64 MiB save limit. Use smaller batches.");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static async Task<T> LoadAsync<T>(string path, CancellationToken token) where T : class
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        if (stream.Length > MaximumJsonBytes) throw new InvalidDataException("Project or template exceeds the 64 MiB load limit.");
        try { return await JsonSerializer.DeserializeAsync<T>(stream, Options, token).ConfigureAwait(false) ?? throw new InvalidDataException("The saved file is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("The saved file is invalid or uses unsupported data. No source was changed.", ex); }
    }
}

internal static class ProjectValidation
{
    internal static void ValidateStructure(ReviewProject project)
    {
        if (project.Version != 1) throw new InvalidDataException("This project version is unsupported.");
        if (project.Sources is null || project.Documents is null || project.Template is null)
            throw new InvalidDataException("Project sources, documents, or template are missing.");
        if (project.Sources.Count > 100 || project.Documents.Count > 10000)
            throw new InvalidDataException("Use a smaller batch (at most 100 sources and 10,000 documents).");
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in project.Sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Id) || !sourceIds.Add(source.Id) ||
                string.IsNullOrWhiteSpace(source.Path) || !Path.IsPathFullyQualified(source.Path) ||
                !source.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                source.Sha256 is null || source.Sha256.Length != 64 || !source.Sha256.All(Uri.IsHexDigit) || source.Pages is null ||
                source.Pages.Count is < 1 or > PdfImportService.MaximumPages)
                throw new InvalidDataException("A source record is incomplete or invalid. Import the PDF again.");
            for (var index = 0; index < source.Pages.Count; index++)
            {
                var page = source.Pages[index];
                if (page is null || page.Number != index + 1 || page.Text is null || page.Text.Length > 2_000_000 ||
                    !double.IsFinite(page.WidthPoints) || !double.IsFinite(page.HeightPoints))
                    throw new InvalidDataException("The project contains invalid page data.");
            }
        }
        var documentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in project.Documents)
        {
            if (document is null || string.IsNullOrWhiteSpace(document.Id) || !documentIds.Add(document.Id) || document.SourceId is null ||
                document.ManualFields is null || document.SkipReason is null || document.ManualFields.Count > 100 ||
                document.ManualFields.Any(x => x.Key.Length > 100 || x.Value is null || x.Value.Length > 10000))
                throw new InvalidDataException("The project contains an invalid document or manual field.");
        }
        if (project.Template.Name is null || project.Template.Pattern is null || project.Template.Fields is null ||
            project.Template.Fields.Any(x => x is null || x.Name is null || x.Expression is null))
            throw new InvalidDataException("The project contains an incomplete naming template.");
    }
}
