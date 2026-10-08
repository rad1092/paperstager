using PaperStager.Core;
using Xunit;

namespace PaperStager.Core.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task ProjectRoundTripPreservesSourcesBoundariesUnicodeEditsAndSkipReasons()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        project.Documents[0].ManualFields["customer"] = "한글 거래처";
        project.Documents[1].Skip = true;
        project.Documents[1].SkipReason = "다시 확인 필요";
        var path = workspace.PathFor("review.paperstager.json");
        var store = new ProjectStore();
        await store.SaveAsync(path, project);
        var reloaded = await store.LoadAsync(path);

        Assert.Equal(project.Version, reloaded.Version);
        Assert.Equal(project.Sources[0].Path, reloaded.Sources[0].Path);
        Assert.Equal(project.Sources[0].Sha256, reloaded.Sources[0].Sha256);
        Assert.Equal(project.Sources[0].Pages.Count, reloaded.Sources[0].Pages.Count);
        Assert.Equal(project.Documents.Select(d => (d.Id, d.SourceId, d.StartPage, d.EndPage)),
            reloaded.Documents.Select(d => (d.Id, d.SourceId, d.StartPage, d.EndPage)));
        Assert.Equal("한글 거래처", reloaded.Documents[0].ManualFields["customer"]);
        Assert.True(reloaded.Documents[1].Skip);
        Assert.Equal("다시 확인 필요", reloaded.Documents[1].SkipReason);
        Assert.Equal(project.Template.Pattern, reloaded.Template.Pattern);
    }

    [Fact]
    public async Task ReloadedProjectCanBeReviewedAndExportedWithStableFingerprint()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        project.Documents[0].ManualFields["customer"] = "한글 거래처";
        var path = workspace.PathFor("restart-review.json");
        var store = new ProjectStore();
        await store.SaveAsync(path, project);
        var reloaded = await store.LoadAsync(path);
        var plan = new PlanningService().CreatePlan(reloaded);
        Assert.False(plan.HasErrors);
        var result = await new ExportService().ExportAsync(reloaded, plan, workspace.DirectoryFor("exports"), approved: true);
        Assert.Equal(2, result.ExportedCount);
        Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "한글 거래처_A-100.pdf")));
        Assert.True(File.Exists(result.ManifestPath));
    }

    [Fact]
    public async Task TemplateRoundTripRetainsIndependentRulesAndUnicodeName()
    {
        using var workspace = new TestWorkspace();
        var template = (await workspace.CreateProjectAsync()).Template;
        template.Name = "거래 문서";
        var path = workspace.PathFor("template.json");
        var store = new TemplateStore();
        await store.SaveAsync(path, template);
        var reloaded = await store.LoadAsync(path);
        Assert.Equal(template.Name, reloaded.Name);
        Assert.Equal(template.Pattern, reloaded.Pattern);
        Assert.Equal(template.Fields.Select(f => (f.Name, f.Kind, f.Expression, f.Required)),
            reloaded.Fields.Select(f => (f.Name, f.Kind, f.Expression, f.Required)));
    }

    [Fact]
    public async Task CancelledSavePreservesPreviouslySavedProject()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var path = workspace.PathFor("review.json");
        var store = new ProjectStore();
        await store.SaveAsync(path, project);
        var before = await File.ReadAllBytesAsync(path);
        project.Template.Name = "unsaved modification";
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(path, project, new CancellationToken(canceled: true)));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task SavingProjectOrTemplateOverSourcePdfIsRejected()
    {
        using var workspace = new TestWorkspace();
        var project = await workspace.CreateProjectAsync();
        var path = project.Sources[0].Path;
        var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectStore().SaveAsync(path, project));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TemplateStore().SaveAsync(path, project.Template));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task UnknownProjectVersionIsRejectedOnLoad()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.PathFor("future.json");
        await File.WriteAllTextAsync(path, "{\"Version\":999,\"Sources\":[],\"Documents\":[],\"Template\":{}}");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore().LoadAsync(path));
    }

    [Fact]
    public async Task MalformedProjectAndTemplateAreRejectedWithoutModifyingTheFile()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.PathFor("malformed.json");
        const string content = "{\"Version\":1,broken";
        await File.WriteAllTextAsync(path, content);
        await Assert.ThrowsAnyAsync<Exception>(() => new ProjectStore().LoadAsync(path));
        await Assert.ThrowsAnyAsync<Exception>(() => new TemplateStore().LoadAsync(path));
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }
}
