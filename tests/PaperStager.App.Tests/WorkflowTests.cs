using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PaperStager.Core;
using Xunit;

namespace PaperStager.App.Tests;

public sealed class WorkflowTests
{
    [AvaloniaFact]
    public async Task SavedProjectRuleEditsPromptBeforeOpenAndCancelPreservesEdits()
    {
        var window = new MainWindow();
        var saved = Path.Combine(Path.GetTempPath(), "paperstager-open-test-" + Guid.NewGuid().ToString("N") + ".json");
        window.Show();
        try
        {
            await window.LoadDemoAsync();
            Assert.True(window.Project.Sources.Count == 1, window.DiagnosticStatus);
            await WaitForAsync(() => window.RenderedThumbnailCount >= 4);
            await window.SaveProjectToPathAsync(saved);
            await PumpAsync();
            Assert.False(window.HasUnsavedChanges);
            window.NavigateTo(0); window.NavigateTo(1);
            await PumpAsync();
            Assert.False(window.HasUnsavedChanges); // Creating editors must not look like an edit.

            Action[] edits = [
                () => Find<TextBox>(window, "TemplatePattern").Text = "edited_{client}_{reference}",
                () => window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Field name").Text = "edited_client",
                () => window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Label (Client:) or regular expression with capture group").Text = "Updated label:",
                () => window.GetVisualDescendants().OfType<ComboBox>().First(c => c.Width == 120).SelectedIndex = 1,
                () => window.GetVisualDescendants().OfType<CheckBox>().First(c => Equals(c.Content, "Required")).IsChecked = false
            ];
            foreach (var edit in edits)
            {
                edit(); await PumpAsync();
                Assert.True(window.HasUnsavedChanges);
                var opening = window.PrepareToOpenProjectAsync();
                await PumpAsync();
                var dialog = Assert.Single(window.OwnedWindows);
                Assert.Equal("Open another project?", dialog.Title);
                var editedTemplate = JsonSerializer.Serialize(window.Project.Template);
                Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
                Assert.False(await opening);
                Assert.Equal(editedTemplate, JsonSerializer.Serialize(window.Project.Template));
                Assert.Equal(1, window.CurrentStep);
                await window.SaveProjectToPathAsync(saved);
                Assert.False(window.HasUnsavedChanges);
            }
        }
        finally { await CloseAndDeleteDemoAsync(window); File.Delete(saved); }
    }

    [AvaloniaFact]
    public async Task CancelledDemoImportLeavesEmptyAndExistingProjectsUnchanged()
    {
        foreach (var existing in new[] { false, true })
        {
            var blockImport = false; var attempts = 0; string? cancelledPath = null;
            var window = new MainWindow(async (path, token) => {
                if (!blockImport) return await new PdfImportService().ImportAsync(path, token);
                attempts++; cancelledPath = path;
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("Cancelled import unexpectedly resumed.");
            });
            window.Show();
            try
            {
                if (existing) { await window.LoadDemoAsync(); await WaitForAsync(() => window.RenderedThumbnailCount >= 4); }
                var before = JsonSerializer.Serialize(window.Project);
                blockImport = true;
                var importing = window.LoadDemoAsync();
                await WaitForAsync(() => cancelledPath != null);
                await window.LoadDemoAsync(); // A second click cannot alter a running import.
                Assert.Equal(1, attempts);
                Click(Find<Button>(window, "CancelOperationButton"));
                await importing;
                Assert.Equal(before, JsonSerializer.Serialize(window.Project));
                Assert.NotNull(cancelledPath);
                Assert.False(Directory.Exists(Path.GetDirectoryName(cancelledPath)));
            }
            finally { await CloseAndDeleteDemoAsync(window); }
        }
    }

    [AvaloniaFact]
    public async Task FailedDemoImportDoesNotReplaceExistingTemplateOrBoundaries()
    {
        var failImport = false; string? failedPath = null;
        var window = new MainWindow((path, token) => {
            if (!failImport) return new PdfImportService().ImportAsync(path, token);
            failedPath = path; throw new InvalidDataException("Synthetic import failure");
        });
        window.Show();
        try
        {
            await window.LoadDemoAsync();
            await WaitForAsync(() => window.RenderedThumbnailCount >= 4);
            var before = JsonSerializer.Serialize(window.Project);
            failImport = true;
            await window.LoadDemoAsync();
            Assert.Equal(before, JsonSerializer.Serialize(window.Project));
            Assert.NotNull(failedPath);
            Assert.False(Directory.Exists(Path.GetDirectoryName(failedPath)));
        }
        finally { await CloseAndDeleteDemoAsync(window); }
    }

    [AvaloniaFact]
    public async Task EmptyProjectCannotSkipImportOrExport()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.NavigateTo(1);
            Assert.Equal(0, window.CurrentStep);
            window.NavigateTo(2);
            Assert.Equal(0, window.CurrentStep);
            Assert.Null(window.ReviewPlan);
            Assert.False(window.IsApproved);
            await Assert.ThrowsAsync<InvalidOperationException>(() => window.ExportToAsync(Path.GetTempPath()));
        }
        finally { await CloseAsync(window); }
    }

    [AvaloniaFact]
    public async Task ReviewEditViaTextInputClearsApprovalUntilPlanIsRefreshed()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await window.LoadDemoAsync();
            await WaitForAsync(() => window.RenderedThumbnailCount >= 4);
            Assert.Equal(4, Assert.Single(window.Project.Sources).Pages.Count);
            Assert.Equal(2, window.Project.Documents.Count);
            Click(Find<Button>(window, "ReviewButton"));
            await PumpAsync();
            Assert.Equal(2, window.CurrentStep);
            Assert.NotNull(window.ReviewPlan);
            Assert.False(window.ReviewPlan.HasErrors);
            Assert.Contains(window.ReviewPlan.Documents, d => d.FileName.Contains("Northwind", StringComparison.Ordinal));

            var approval = Find<CheckBox>(window, "ApprovalCheckBox");
            approval.IsChecked = true;
            Assert.True(window.IsApproved);
            Assert.True(Find<Button>(window, "ExportButton").IsEnabled);
            var value = window.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Enter verified value");
            value.Focus();
            value.SelectAll();
            window.KeyTextInput("검토완료");
            await PumpAsync();
            Assert.False(window.IsApproved);
            Assert.Null(window.ReviewPlan);
            Assert.False(Find<Button>(window, "ExportButton").IsEnabled);

            Click(Find<Button>(window, "RefreshPlanButton"));
            await PumpAsync();
            Assert.NotNull(window.ReviewPlan);
            Assert.False(window.ReviewPlan.HasErrors);
            Assert.Contains("검토완료", window.ReviewPlan.Documents[0].FileName);
            Assert.False(window.IsApproved);
        }
        finally { await CloseAndDeleteDemoAsync(window); }
    }

    [AvaloniaFact]
    public async Task RepeatedBackNavigationPreservesRulesAndBoundaryCoverage()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await window.LoadDemoAsync();
            await WaitForAsync(() => window.RenderedThumbnailCount >= 4);
            var originalFields = window.Project.Template.Fields.Select(f => (f.Name, f.Kind, f.Expression)).ToArray();
            Find<TextBox>(window, "TemplatePattern").Text = "reviewed_{client}_{reference}";
            var source = Assert.Single(window.Project.Sources);
            var expectedThumbnails = 4;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                window.NavigateTo(0);
                window.NavigateTo(1);
                expectedThumbnails += 4;
                await WaitForAsync(() => window.RenderedThumbnailCount >= expectedThumbnails);
                Assert.Equal("reviewed_{client}_{reference}", Find<TextBox>(window, "TemplatePattern").Text);
                Assert.Equal(originalFields, window.Project.Template.Fields.Select(f => (f.Name, f.Kind, f.Expression)).ToArray());
                var boundaries = window.GetVisualDescendants().OfType<CheckBox>().Where(c => Equals(c.Content, "New document starts here")).ToArray();
                Assert.Equal(4, boundaries.Length);
                Assert.False(boundaries[0].IsEnabled);
                boundaries[2].IsChecked = false;
                var joined = Assert.Single(window.Project.Documents);
                Assert.Equal((1, 4), (joined.StartPage, joined.EndPage));
                boundaries[2].IsChecked = true;
                Assert.Equal(new[] { (1, 2), (3, 4) }, window.Project.Documents.Select(d => (d.StartPage, d.EndPage)).ToArray());
            }
            Click(Find<Button>(window, "ReviewButton"));
            await PumpAsync();
            Assert.NotNull(window.ReviewPlan);
            Assert.False(window.ReviewPlan.HasErrors);
            Assert.All(window.ReviewPlan.Documents, d => Assert.StartsWith("reviewed_", d.FileName));
            Assert.All(window.Project.Documents, d => Assert.Equal(source.Id, d.SourceId));
        }
        finally { await CloseAndDeleteDemoAsync(window); }
    }

    [AvaloniaFact]
    public async Task ApprovedExportCreatesMappingAndPreservesSourceAcrossRepeat()
    {
        var window = new MainWindow();
        var output = Path.Combine(Path.GetTempPath(), "paperstager-app-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        window.Show();
        try
        {
            await window.LoadDemoAsync();
            await WaitForAsync(() => window.RenderedThumbnailCount >= 4);
            var source = Assert.Single(window.Project.Sources);
            var original = SHA256.HashData(File.ReadAllBytes(source.Path));
            window.NavigateTo(2);
            await PumpAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => window.ExportToAsync(output));
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
            window.SetApproval(true);
            var first = await window.ExportToAsync(output);
            Assert.NotNull(first);
            Assert.Equal(2, first.ExportedCount);
            Assert.True(File.Exists(first.ManifestPath));
            Assert.Equal(3, window.CurrentStep);
            Assert.False(window.IsApproved);
            Assert.Equal(2, Directory.GetFiles(first.OutputDirectory, "*.pdf", SearchOption.AllDirectories).Length);

            window.NavigateTo(2);
            await PumpAsync();
            Assert.False(window.IsApproved);
            window.SetApproval(true);
            var second = await window.ExportToAsync(output);
            Assert.NotNull(second);
            Assert.NotEqual(first.OutputDirectory, second.OutputDirectory);
            Assert.True(File.Exists(first.ManifestPath));
            Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(source.Path)));
        }
        finally
        {
            await CloseAndDeleteDemoAsync(window);
            Directory.Delete(output, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task ClosingUnsavedProjectCanBeCancelledAndThenConfirmed()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await window.LoadDemoAsync();
            await WaitForAsync(() => window.RenderedThumbnailCount >= 4);
            window.Close();
            await PumpAsync();
            Assert.True(window.IsVisible);
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("Close PaperStager?", dialog.Title);
            Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
            await PumpAsync();
            Assert.True(window.IsVisible);
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(2, window.Project.Documents.Count);

            window.Close();
            await PumpAsync();
            dialog = Assert.Single(window.OwnedWindows);
            Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Discard edits & close")));
            await PumpAsync();
            Assert.False(window.IsVisible);
        }
        finally { await CloseAndDeleteDemoAsync(window); }
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Task PumpAsync() => Task.Delay(30);
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(25);
        Assert.True(condition(), "The asynchronous desktop operation did not finish within five seconds.");
    }
    private static async Task CloseAsync(MainWindow window)
    {
        if (!window.IsVisible) return;
        window.Close();
        await PumpAsync();
        foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(true);
        await PumpAsync();
    }
    private static async Task CloseAndDeleteDemoAsync(MainWindow window)
    {
        var directories = window.Project.Sources.Select(s => Path.GetDirectoryName(s.Path)!)
            .Where(d => Path.GetFileName(d).StartsWith("paperstager-demo-", StringComparison.Ordinal)).Distinct().ToArray();
        await CloseAsync(window);
        foreach (var directory in directories) if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
