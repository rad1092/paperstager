using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PaperStager.Core;
using PaperStager.Rendering;

[assembly: InternalsVisibleTo("PaperStager.App.Tests")]

namespace PaperStager.App;

public sealed class MainWindow : Window
{
    private readonly StackPanel _body = new() { Spacing = 16, Margin = new Thickness(28, 22) };
    private readonly TextBlock _status = new() { Text = "Your documents stay on this computer.", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _navigation = new() { Spacing = 8 };
    private readonly List<Bitmap> _bitmaps = [];
    private readonly List<RuleEditor> _rules = [];
    private readonly Button _cancel = new() { Content = "Cancel operation", IsVisible = false, Name = "CancelOperationButton" };
    private readonly Func<string, CancellationToken, Task<SourcePdf>> _importPdf;
    private CancellationTokenSource? _operation;
    private TextBox? _pattern;
    private TextBox? _sourceText;
    private CheckBox? _approval;
    private Button? _export;
    private bool _dirty, _allowClose, _closePrompt;
    private int _step, _previewGeneration, _pageOffset;
    private SourcePdf? _selectedSource;
    private string? _projectPath;
    private readonly List<string> _importIssues = [];
    public ReviewProject Project { get; private set; } = new();
    public ExportPlan? ReviewPlan { get; private set; }
    public int CurrentStep => _step;
    public bool IsApproved => _approval?.IsChecked == true;
    public int RenderedThumbnailCount { get; private set; }
    internal bool HasUnsavedChanges => _dirty;
    internal string DiagnosticStatus => _status.Text + " " + string.Join("; ", _importIssues);

    public MainWindow() : this((path, token) => new PdfImportService().ImportAsync(path, token)) { }

    internal MainWindow(Func<string, CancellationToken, Task<SourcePdf>> importPdf)
    {
        _importPdf = importPdf;
        Title = "PaperStager — Review before filing"; Width = 1240; Height = 840; MinWidth = 1000; MinHeight = 700;
        Background = Brush("#F4F6FA");
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), RowDefinitions = new RowDefinitions("*,Auto") };
        var sidebar = new StackPanel { Spacing = 28, Margin = new Thickness(20, 30) };
        sidebar.Children.Add(new TextBlock { Text = "PaperStager", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Brushes.White });
        sidebar.Children.Add(new TextBlock { Text = "A review desk for\nyour PDF batches", Foreground = Brush("#B9C5DA"), FontSize = 14 });
        string[] labels = ["1  Import PDFs", "2  Boundaries & names", "3  Review & export", "4  Results"];
        for (var i = 0; i < labels.Length; i++) { var step = i; var button = ActionButton(labels[i], () => NavigateTo(step), $"Step{i}Button"); button.HorizontalAlignment = HorizontalAlignment.Stretch; _navigation.Children.Add(button); }
        sidebar.Children.Add(_navigation);
        sidebar.Children.Add(ActionButton("Open project…", () => _ = RunUiAsync(OpenProjectAsync), "OpenProjectButton"));
        sidebar.Children.Add(ActionButton("Save project…", () => _ = RunUiAsync(SaveProjectAsync)));
        sidebar.Children.Add(new TextBlock { Text = "OFFLINE BY DESIGN\nNo account · No upload\nOriginals stay untouched", Foreground = Brush("#B9C5DA"), TextWrapping = TextWrapping.Wrap, LineHeight = 23, FontSize = 12 });
        foreach (var button in sidebar.Children.OfType<Button>().Concat(_navigation.Children.OfType<Button>()))
        {
            button.Foreground = Brush("#F0F4FC"); button.Background = Brush("#2D4162");
            button.Padding = new Thickness(9, 9); button.FontSize = 13;
        }
        var side = new Border { Background = Brush("#192840"), Child = sidebar }; Grid.SetRowSpan(side, 2); shell.Children.Add(side);
        var scroller = new ScrollViewer { Content = _body, Name = "WorkspaceScrollViewer" }; Grid.SetColumn(scroller, 1); shell.Children.Add(scroller);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(24, 12) };
        footer.Children.Add(_status); _cancel.Click += (_, _) => _operation?.Cancel(); Grid.SetColumn(_cancel, 1); footer.Children.Add(_cancel);
        Grid.SetColumn(footer, 1); Grid.SetRow(footer, 1); shell.Children.Add(footer); Content = shell;
        DragDrop.SetAllowDrop(this, true); AddHandler(DragDrop.DropEvent, OnDrop);
        Closing += OnClosing; Closed += (_, _) => { _previewGeneration++; foreach (var b in _bitmaps) b.Dispose(); _bitmaps.Clear(); };
        NavigateTo(0);
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
    private static TextBlock Text(string value, int size = 14) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static Button ActionButton(string text, Action action, string? name = null)
    { var b = new Button { Content = text, Name = name, Padding = new Thickness(12, 9) }; b.Click += (_, _) => action(); return b; }
    private static Border Card(Control child) => new() { Child = child, Background = Brushes.White, CornerRadius = new CornerRadius(9), Padding = new Thickness(18), BorderBrush = Brush("#DDE3EC"), BorderThickness = new Thickness(1) };
    private void Heading(string title, string detail) { _body.Children.Add(Text(title, 28)); _body.Children.Add(Text(detail)); }
    private void Changed() { _dirty = true; ReviewPlan = null; if (_approval != null) _approval.IsChecked = false; }
    private void SetStatus(string status) => _status.Text = status;

    public void NavigateTo(int step)
    {
        if (_operation != null) return;
        if (step is 1 or 2 && Project.Sources.Count == 0) { SetStatus("Import at least one PDF first."); return; }
        if (step == 3 && _result == null) { SetStatus("Results appear after an approved export."); return; }
        if (_step == 1) ApplyRules();
        _step = step; _pattern = null; ClearBody();
        switch (step) { case 0: ShowImport(); break; case 1: ShowBoundaries(); break; case 2: ShowReview(); break; case 3: ShowResults(); break; }
        for (var i = 0; i < _navigation.Children.Count; i++) ((Button)_navigation.Children[i]).FontWeight = i == step ? FontWeight.Bold : FontWeight.Normal;
    }

    private void ShowImport()
    {
        Heading("Turn a batch into files you can trust", "Open combined PDFs, decide where each document starts, then review every proposed name before saving.");
        var entry = new StackPanel { Spacing = 14 };
        entry.Children.Add(Text("Drop PDF files anywhere in this window", 20));
        entry.Children.Add(Text("Searchable PDFs work immediately. For image-only scans, add a text layer with NAPS2 first, or enter naming fields manually."));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(ActionButton("Choose PDFs…", () => _ = RunUiAsync(PickPdfsAsync), "ImportButton"));
        actions.Children.Add(ActionButton("Try a synthetic example", () => _ = RunUiAsync(LoadDemoAsync), "DemoButton")); entry.Children.Add(actions); _body.Children.Add(Card(entry));
        foreach (var source in Project.Sources)
        {
            var line = new StackPanel { Spacing = 6 }; line.Children.Add(Text(Path.GetFileName(source.Path), 18));
            line.Children.Add(Text($"{source.Pages.Count} pages · {source.Pages.Count(p => string.IsNullOrWhiteSpace(p.Text))} pages without text · SHA-256 verified on export"));
            line.Children.Add(Text(source.Path, 12));
            line.Children.Add(ActionButton("Remove from this project", () => { Project.Sources.Remove(source); Project.Documents.RemoveAll(d => d.SourceId == source.Id); Changed(); NavigateTo(0); })); _body.Children.Add(Card(line));
        }
        foreach (var issue in _importIssues) _body.Children.Add(Text(issue));
        if (Project.Sources.Count > 0) _body.Children.Add(ActionButton("Continue to boundaries & names →", () => NavigateTo(1), "ContinueButton"));
    }

    private async Task PickPdfsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import PDF batches", AllowMultiple = true, FileTypeFilter = [new FilePickerFileType("PDF documents") { Patterns = ["*.pdf"] }] });
        await LoadPathsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(x => x.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths is { Length: > 0 }) await RunUiAsync(() => LoadPathsAsync(paths));
    }

    public async Task LoadPathsAsync(IEnumerable<string> paths)
    {
        await LoadPathsCoreAsync(paths);
    }

    private async Task<List<SourcePdf>> LoadPathsCoreAsync(IEnumerable<string> paths)
    {
        List<SourcePdf> imported = [];
        if (_operation != null) return imported;
        await OperationAsync(async token => {
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (Project.Sources.Any(s => Path.GetFullPath(s.Path) == Path.GetFullPath(path))) { SetStatus("That PDF is already in this project."); continue; }
                try {
                    SetStatus($"Reading {Path.GetFileName(path)}…");
                    var source = await _importPdf(path, token);
                    token.ThrowIfCancellationRequested();
                    Project.Sources.Add(source); Project.Documents.AddRange(DocumentService.Split(source, [1])); imported.Add(source); Changed();
                } catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _importIssues.Add($"Could not import {Path.GetFileName(path)}: {ex.Message}"); }
            }
        });
        NavigateTo(0);
        return imported;
    }

    public async Task LoadDemoAsync()
    {
        if (_operation != null) return;
        var directory = Path.Combine(Path.GetTempPath(), "paperstager-demo-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "synthetic-batch.pdf");
        try {
            SyntheticDemo.CreatePdf(path);
            var source = (await LoadPathsCoreAsync([path])).SingleOrDefault();
            if (source == null) return;
            Project.Template = SyntheticDemo.DemoTemplate();
            Project.Documents.RemoveAll(d => d.SourceId == source.Id); Project.Documents.AddRange(DocumentService.Split(source, [1, 3]));
            _selectedSource = source; Changed(); NavigateTo(1);
        } finally {
            if (!Project.Sources.Any(s => s.Path == path)) Directory.Delete(directory, recursive: true);
        }
    }

    private void ShowBoundaries()
    {
        Heading("Check the pages. Give each document a name.", "The first page always starts a document. Toggle other boundaries below; no page is removed. Rules read the first page of each document.");
        _selectedSource = Project.Sources.FirstOrDefault(s => s.Id == _selectedSource?.Id) ?? Project.Sources[0];
        var chooser = new ComboBox { ItemsSource = Project.Sources.Select(s => Path.GetFileName(s.Path)).ToArray(), SelectedIndex = Project.Sources.IndexOf(_selectedSource), HorizontalAlignment = HorizontalAlignment.Stretch };
        chooser.SelectionChanged += (_, _) => { if (chooser.SelectedIndex >= 0) { ApplyRules(); _selectedSource = Project.Sources[chooser.SelectedIndex]; ShowBoundaryRefresh(); } }; _body.Children.Add(chooser);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 18 };
        var left = new StackPanel { Spacing = 10 }; left.Children.Add(Text("Document boundaries", 18));
        var pageList = new StackPanel { Spacing = 12 };
        var paging = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        paging.Children.Add(ActionButton("← Previous pages", () => { ApplyRules(); _pageOffset = Math.Max(0, _pageOffset - 12); ShowBoundaryRefresh(); }));
        paging.Children.Add(ActionButton("Next pages →", () => { ApplyRules(); _pageOffset = Math.Min(((_selectedSource.Pages.Count - 1) / 12) * 12, _pageOffset + 12); ShowBoundaryRefresh(); }));
        left.Children.Add(paging);
        _pageOffset = Math.Min(_pageOffset, ((_selectedSource.Pages.Count - 1) / 12) * 12);
        left.Children.Add(Text($"Pages {_pageOffset + 1}–{Math.Min(_pageOffset + 12, _selectedSource.Pages.Count)} of {_selectedSource.Pages.Count}", 12));
        foreach (var page in _selectedSource.Pages.Skip(_pageOffset).Take(12))
        {
            var starts = Project.Documents.Any(d => d.SourceId == _selectedSource.Id && d.StartPage == page.Number);
            var tile = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), ColumnSpacing = 12 };
            var preview = new Image { Width = 120, Height = 160, Stretch = Stretch.Uniform }; tile.Children.Add(preview);
            var info = new StackPanel { Spacing = 8 }; info.Children.Add(Text($"Page {page.Number}", 18));
            var boundary = new CheckBox { Content = "New document starts here", IsChecked = starts, IsEnabled = page.Number != 1 };
            boundary.IsCheckedChanged += (_, _) => ToggleBoundary(_selectedSource.Id, page.Number, boundary.IsChecked == true); info.Children.Add(boundary);
            info.Children.Add(ActionButton("Read page text", () => { if (_sourceText != null) _sourceText.Text = page.Text; }));
            info.Children.Add(Text(string.IsNullOrWhiteSpace(page.Text) ? "No searchable text — manual fields needed" : $"{page.Text.Length} text characters", 12)); Grid.SetColumn(info, 1); tile.Children.Add(info); pageList.Children.Add(Card(tile));
            _ = LoadThumbnailAsync(_selectedSource.Path, page.Number, preview);
        }
        left.Children.Add(new ScrollViewer { Content = pageList, Height = 530 }); layout.Children.Add(left);
        var right = new StackPanel { Spacing = 10 };
        right.Children.Add(Text("Source text · select a label to create a rule", 18));
        _sourceText = new TextBox { Text = _selectedSource.Pages[0].Text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 155, Name = "SourceText" }; right.Children.Add(_sourceText);
        right.Children.Add(Text("Naming fields", 18));
        _rules.Clear(); var ruleContainer = new StackPanel { Spacing = 8 };
        void AddRule(FieldRule rule) { var row = new RuleEditor(rule, Changed); _rules.Add(row); ruleContainer.Children.Add(row.Panel); row.Remove.Click += (_, _) => { _rules.Remove(row); ruleContainer.Children.Remove(row.Panel); Changed(); }; }
        foreach (var rule in Project.Template.Fields) AddRule(rule);
        right.Children.Add(ruleContainer);
        right.Children.Add(ActionButton("+ Add field from selected text", () => { AddRule(new FieldRule { Name = "field" + (_rules.Count + 1), Kind = FieldRuleKind.AfterLabel, Expression = _sourceText.SelectedText ?? "" }); Changed(); }));
        right.Children.Add(Text("Template · use {field} tokens for the PDF file name.", 12));
        _pattern = new TextBox { Text = Project.Template.Pattern, PlaceholderText = "{date}_{client}_{reference}", Name = "TemplatePattern" }; right.Children.Add(_pattern);
        var pattern = _pattern; var previousPattern = pattern.Text;
        pattern.TextChanged += (_, _) => { if (pattern.Text == previousPattern) return; previousPattern = pattern.Text; Changed(); };
        var templateActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        templateActions.Children.Add(ActionButton("Save template…", () => _ = RunUiAsync(SaveTemplateAsync)));
        templateActions.Children.Add(ActionButton("Load template…", () => _ = RunUiAsync(LoadTemplateAsync))); right.Children.Add(templateActions);
        Grid.SetColumn(right, 1); layout.Children.Add(right); _body.Children.Add(layout);
        _body.Children.Add(ActionButton("Review proposed files →", () => NavigateTo(2), "ReviewButton"));
    }
    private void ClearBody() { _previewGeneration++; foreach (var bitmap in _bitmaps) bitmap.Dispose(); _bitmaps.Clear(); _body.Children.Clear(); }
    private void ShowBoundaryRefresh() { ClearBody(); ShowBoundaries(); }

    public void ToggleBoundary(string sourceId, int page, bool starts)
    {
        var source = Project.Sources.Single(s => s.Id == sourceId);
        var boundaries = Project.Documents.Where(d => d.SourceId == sourceId).Select(d => d.StartPage).ToHashSet();
        if (starts) boundaries.Add(page); else if (page != 1) boundaries.Remove(page);
        var old = Project.Documents.Where(d => d.SourceId == sourceId).ToList();
        var docs = DocumentService.Split(source, boundaries);
        foreach (var doc in docs) { var match = old.FirstOrDefault(d => d.StartPage == doc.StartPage && d.EndPage == doc.EndPage); if (match != null) { doc.ManualFields = new(match.ManualFields); doc.Skip = match.Skip; doc.SkipReason = match.SkipReason; } }
        Project.Documents.RemoveAll(d => d.SourceId == sourceId); Project.Documents.AddRange(docs); Changed(); SetStatus($"{Project.Documents.Count} document groups. Confirm boundaries before export.");
    }

    private async Task LoadThumbnailAsync(string path, int page, Image image)
    {
        var generation = _previewGeneration;
        try {
            var rendered = await Task.Run(() => PdfRenderer.Render(path, page, 240));
            if (generation != _previewGeneration) return;
            var bitmap = new WriteableBitmap(new PixelSize(rendered.Width, rendered.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock()) {
                for (var row = 0; row < rendered.Height; row++) Marshal.Copy(rendered.Bgra, row * rendered.Stride, buffer.Address + row * buffer.RowBytes, rendered.Width * 4);
            }
            _bitmaps.Add(bitmap); image.Source = bitmap; RenderedThumbnailCount++;
        } catch (Exception ex) { if (generation != _previewGeneration) return; ToolTip.SetTip(image, $"Preview unavailable: {ex.Message}"); SetStatus($"Page {page} preview unavailable. {ex.Message}"); }
    }

    private void ApplyRules()
    {
        if (_pattern == null) return;
        var before = JsonSerializer.Serialize(Project.Template);
        Project.Template.Pattern = _pattern.Text ?? ""; Project.Template.Fields = _rules.Select(r => r.Value).ToList();
        if (before != JsonSerializer.Serialize(Project.Template)) Changed();
    }

    private void ShowReview()
    {
        Heading("Review every file before export", "Correct field values or skip a document with a reason. Nothing is saved until you approve this plan and choose an export folder.");
        ReviewPlan = new PlanningService().CreatePlan(Project);
        foreach (var error in ReviewPlan.Errors) _body.Children.Add(Text("Needs review: " + error));
        var summary = Text($"{ReviewPlan.Documents.Count(d => !d.Skip)} files planned · {ReviewPlan.Documents.Count(d => d.Skip)} skipped · {ReviewPlan.Documents.Count(d => d.Errors.Count > 0)} need attention", 19); _body.Children.Add(summary);
        foreach (var planned in ReviewPlan.Documents)
        {
            var draft = Project.Documents.Single(d => d.Id == planned.Id); var source = Project.Sources.Single(s => s.Id == draft.SourceId);
            var panel = new StackPanel { Spacing = 10 }; panel.Children.Add(Text($"{Path.GetFileName(source.Path)}  /  pages {draft.StartPage}–{draft.EndPage}", 13));
            panel.Children.Add(Text(planned.Skip ? "Skipped" : planned.FileName, 20));
            var fields = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var rule in Project.Template.Fields)
            {
                var field = new StackPanel { Width = 200, Margin = new Thickness(0, 0, 12, 6) }; field.Children.Add(Text(rule.Name, 12));
                var value = new TextBox { Text = planned.Fields.GetValueOrDefault(rule.Name, ""), PlaceholderText = "Enter verified value" };
                var previousValue = value.Text;
                value.TextChanged += (_, _) => { if (value.Text == previousValue) return; previousValue = value.Text; draft.ManualFields[rule.Name] = value.Text ?? ""; InvalidateApproval(); }; field.Children.Add(value); fields.Children.Add(field);
            }
            panel.Children.Add(fields);
            var skip = new CheckBox { Content = "Skip this document", IsChecked = draft.Skip }; var reason = new TextBox { Text = draft.SkipReason, PlaceholderText = "Reason for skipping (recorded in manifest)" };
            skip.IsCheckedChanged += (_, _) => { if (draft.Skip == (skip.IsChecked == true)) return; draft.Skip = skip.IsChecked == true; InvalidateApproval(); };
            reason.TextChanged += (_, _) => { if (draft.SkipReason == (reason.Text ?? "")) return; draft.SkipReason = reason.Text ?? ""; InvalidateApproval(); }; panel.Children.Add(skip); panel.Children.Add(reason);
            foreach (var error in planned.Errors) panel.Children.Add(new TextBlock { Text = "Needs review: " + error, Foreground = Brush("#A12B31"), TextWrapping = TextWrapping.Wrap });
            _body.Children.Add(Card(panel));
        }
        _body.Children.Add(ActionButton("Refresh plan after edits", () => NavigateTo(2), "RefreshPlanButton"));
        _approval = new CheckBox { Content = "I checked the boundaries, field values and all proposed paths.", Name = "ApprovalCheckBox" };
        _export = ActionButton("Choose folder & export approved files…", () => _ = RunUiAsync(PickExportAsync), "ExportButton"); _export.IsEnabled = false;
        _approval.IsCheckedChanged += (_, _) => {
            _export.IsEnabled = _approval.IsChecked == true && ReviewPlan?.HasErrors == false;
            if (_export.IsEnabled) SetStatus("Plan approved. Choose an output folder to export this reviewed batch.");
        };
        _body.Children.Add(_approval); _body.Children.Add(_export);
        SetStatus(ReviewPlan.HasErrors ? "Fix the review errors before approving export." : "Review the proposed names and approve when ready.");
        _body.Children.Add(Text("A new PaperStager batch folder will contain all PDFs and a mapping manifest. Existing files are never overwritten.", 12));
    }
    private void InvalidateApproval() { Changed(); if (_export != null) _export.IsEnabled = false; SetStatus("Plan changed. Refresh it and approve the updated paths."); }
    public void SetApproval(bool approved) { if (_approval != null) _approval.IsChecked = approved; }
    private ExportResult? _result;
    private async Task PickExportAsync()
    {
        var directories = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose parent folder for a new PaperStager batch" });
        var path = directories.FirstOrDefault()?.TryGetLocalPath(); if (path != null) await ExportToAsync(path);
    }
    public async Task<ExportResult?> ExportToAsync(string parent)
    {
        if (!IsApproved || ReviewPlan == null || ReviewPlan.HasErrors) throw new InvalidOperationException("Refresh the plan, fix errors and explicitly approve it first.");
        var plan = ReviewPlan; _result = null;
        await OperationAsync(async token => { SetStatus("Exporting a new batch. Your source PDFs remain untouched…"); _result = await new ExportService().ExportAsync(Project, plan, parent, true, token); });
        SetApproval(false); if (_result != null) NavigateTo(3); return _result;
    }
    private void ShowResults()
    {
        if (_result == null) return;
        Heading("Your reviewed batch is ready", $"{_result.ExportedCount} PDF files exported · {_result.SkippedCount} documents skipped");
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(Text("Output folder", 18)); panel.Children.Add(Text(_result.OutputDirectory));
        panel.Children.Add(Text("Source → output mapping", 18)); panel.Children.Add(Text(_result.ManifestPath));
        panel.Children.Add(Text("The manifest records source hashes, page ranges, output names and skipped documents. Keep it with the batch.")); _body.Children.Add(Card(panel));
        try {
            var manifest = JsonSerializer.Deserialize<ExportManifest>(File.ReadAllText(_result.ManifestPath));
            if (manifest != null) foreach (var item in manifest.Documents) _body.Children.Add(Text($"{item.Status.ToUpperInvariant()} · {Path.GetFileName(item.SourcePath)} p{item.StartPage}–{item.EndPage} → {item.FileName} {item.Message}"));
        } catch (Exception ex) { _body.Children.Add(Text("Could not display manifest: " + ex.Message)); }
        _body.Children.Add(ActionButton("Review this batch again", () => NavigateTo(2)));
        _body.Children.Add(ActionButton("Import more PDFs", () => NavigateTo(0)));
        _body.Children.Add(Text("Exporting again creates a separate batch folder. No existing file is replaced."));
    }

    private async Task SaveProjectAsync()
    {
        if (_step == 1) ApplyRules();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save PaperStager project", SuggestedFileName = "intake.paperstager.json", DefaultExtension = "json", ShowOverwritePrompt = true });
        if (file?.TryGetLocalPath() is { } path) await SaveProjectToPathAsync(path);
    }
    internal async Task SaveProjectToPathAsync(string path)
    {
        if (_step == 1) ApplyRules();
        await new ProjectStore().SaveAsync(path, Project); _projectPath = path; _dirty = false; SetStatus($"Project saved: {Path.GetFileName(path)}");
    }
    internal async Task<bool> PrepareToOpenProjectAsync()
    {
        if (_step == 1) ApplyRules();
        return !_dirty || await ConfirmAsync("Open another project?", "Unsaved edits will be lost. Save this project first if you need them.", "Discard edits & open");
    }
    private async Task OpenProjectAsync()
    {
        if (!await PrepareToOpenProjectAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open PaperStager project", FileTypeFilter = [new FilePickerFileType("JSON project") { Patterns = ["*.json"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await RestoreProjectAsync(path);
    }
    internal async Task RestoreProjectAsync(string path)
    {
        var restored = await new ProjectStore().LoadAsync(path);
        SetApproval(false); ReviewPlan = null; _approval = null; _export = null;
        Project = restored; _projectPath = path; _selectedSource = null; _dirty = false; _pattern = null; _result = null; _pageOffset = 0; NavigateTo(0); SetStatus("Project restored. Source hashes will be rechecked before export; review approval was cleared.");
    }
    private async Task SaveTemplateAsync()
    {
        ApplyRules(); var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save reusable naming template", SuggestedFileName = "naming-template.json", ShowOverwritePrompt = true });
        if (file?.TryGetLocalPath() is { } path) { await new TemplateStore().SaveAsync(path, Project.Template); SetStatus("Naming template saved."); }
        ShowBoundaryRefresh();
    }
    private async Task LoadTemplateAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open naming template", FileTypeFilter = [new FilePickerFileType("JSON template") { Patterns = ["*.json"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) { Project.Template = await new TemplateStore().LoadAsync(path); _pattern = null; Changed(); ShowBoundaryRefresh(); }
    }
    private async Task OperationAsync(Func<CancellationToken, Task> operation)
    {
        if (_operation != null) throw new InvalidOperationException("Another operation is still running.");
        _operation = new CancellationTokenSource(); _cancel.IsVisible = true; _body.IsEnabled = false;
        try { await operation(_operation.Token); SetStatus("Operation complete. Review the result before continuing."); }
        catch (OperationCanceledException ex) { SetStatus("Cancelled. " + ex.Message); }
        finally { _operation.Dispose(); _operation = null; _cancel.IsVisible = false; _body.IsEnabled = true; }
    }
    private async Task RunUiAsync(Func<Task> operation)
    {
        if (_operation != null) { SetStatus("Wait for this operation or cancel it before continuing."); return; }
        try { await operation(); }
        catch (Exception ex) { SetStatus(ex.Message); await MessageAsync("Could not complete this operation", ex.Message); }
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        if (_operation != null) { e.Cancel = true; _operation.Cancel(); SetStatus("Cancelling safely. Close the window again after the operation stops."); return; }
        if (_step == 1) ApplyRules();
        if (!_dirty) return;
        e.Cancel = true; if (_closePrompt) return; _closePrompt = true;
        if (await ConfirmAsync("Close PaperStager?", "You have unsaved project edits. Exported PDFs are already saved. Choose Cancel to return and save your project.", "Discard edits & close")) { _allowClose = true; Close(); }
        _closePrompt = false;
    }
    private async Task<bool> ConfirmAsync(string title, string message, string confirm)
    {
        var dialog = new Window { Title = title, Width = 480, Height = 240, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 18 }; panel.Children.Add(Text(message));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(ActionButton("Cancel", () => dialog.Close(false))); buttons.Children.Add(ActionButton(confirm, () => dialog.Close(true))); panel.Children.Add(buttons); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(this);
    }
    private async Task MessageAsync(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 540, Height = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 18 }; panel.Children.Add(Text(message)); panel.Children.Add(ActionButton("Return to review", () => dialog.Close())); dialog.Content = panel; await dialog.ShowDialog(this);
    }

    // Internal release QA: exercises the displayed controls, then renders their client area.
    // It deliberately does not claim to exercise OS file pickers, desktop drag-and-drop,
    // window decorations, accessibility permissions, or a screen capture API.
    public async Task RunQaAsync(string? directory, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var root = directory ?? Path.Combine(Path.GetTempPath(), "paperstager-qa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var captures = new List<string>();
        var checks = new List<string>();
        try {
            if (!IsVisible) throw new InvalidOperationException("QA requires a displayed native window.");
            await CaptureAsync("01-import.png");
            ClickControl("DemoButton");
            await WaitAsync(() => CurrentStep == 1 && _operation == null && RenderedThumbnailCount >= 4, "Demo import and native PDF thumbnails");
            if (Project.Documents.Count != 2) throw new InvalidOperationException("Demo document boundaries are incorrect.");
            checks.Add("Displayed demo button imported four pages; four PDF thumbnails rendered.");
            await CaptureAsync("02-boundaries.png");
            var boundaries = this.GetVisualDescendants().OfType<CheckBox>().Where(c => Equals(c.Content, "New document starts here")).ToArray();
            boundaries[2].IsChecked = false;
            if (Project.Documents.Count != 1 || Project.Documents[0].EndPage != 4) throw new InvalidOperationException("Boundary merge failed.");
            boundaries[2].IsChecked = true;
            if (Project.Documents.Count != 2) throw new InvalidOperationException("Boundary split failed.");
            GetControl<TextBox>("TemplatePattern").Text = "reviewed_{client}_{reference}";
            await NextFrameAsync();
            for (var pass = 0; pass < 2; pass++) {
                ClickControl("Step0Button"); ClickControl("ContinueButton");
                if (GetControl<TextBox>("TemplatePattern").Text != "reviewed_{client}_{reference}") throw new InvalidOperationException("Back navigation lost naming edits.");
            }
            checks.Add("Boundary merge/split and repeated back navigation preserved edits.");
            ScrollWorkspaceToEnd(); await CaptureAsync("03-naming-template.png");
            var projectPath = Path.Combine(root, "project.json");
            await SaveProjectToPathAsync(projectPath);
            await NextFrameAsync();
            if (_dirty) throw new InvalidOperationException("Saving a project left pending edits.");
            GetControl<TextBox>("TemplatePattern").Text = "reviewed_{client}_{reference}_checked";
            await NextFrameAsync();
            if (!_dirty) throw new InvalidOperationException("Template input did not immediately mark unsaved edits.");
            ClickControl("OpenProjectButton");
            await WaitAsync(() => OwnedWindows.Any(w => w.Title == "Open another project?"), "Unsaved project confirmation");
            var dialog = OwnedWindows.Single(w => w.Title == "Open another project?");
            await CaptureWindowAsync(dialog, Path.Combine(root, "04-unsaved-open-confirmation.png")); captures.Add("04-unsaved-open-confirmation.png");
            ClickDialog(dialog, "Cancel");
            await WaitAsync(() => !OwnedWindows.Any(), "Cancelled project open");
            if (Project.Template.Pattern != "reviewed_{client}_{reference}_checked") throw new InvalidOperationException("Cancelling open lost current naming edits.");
            checks.Add("Editing a saved naming template prompted before Open; Cancel preserved edits.");
            await SaveProjectToPathAsync(projectPath);
            await new TemplateStore().SaveAsync(Path.Combine(root, "template.json"), Project.Template);
            await RestoreProjectAsync(projectPath);
            Project.Template = await new TemplateStore().LoadAsync(Path.Combine(root, "template.json"));
            ClickControl("Step2Button"); await NextFrameAsync();
            if (ReviewPlan?.HasErrors != false || IsApproved) throw new InvalidOperationException("Restored project did not require a fresh valid review.");
            await CaptureAsync("05-review.png");
            var approval = GetControl<CheckBox>("ApprovalCheckBox"); approval.IsChecked = true;
            var field = this.GetVisualDescendants().OfType<TextBox>().First(t => t.PlaceholderText == "Enter verified value");
            field.Text = "검토완료";
            await NextFrameAsync();
            if (IsApproved || ReviewPlan != null || GetControl<Button>("ExportButton").IsEnabled) throw new InvalidOperationException("Review edit did not invalidate approval.");
            ClickControl("RefreshPlanButton"); await NextFrameAsync();
            if (ReviewPlan?.HasErrors != false || !ReviewPlan.Documents[0].FileName.Contains("검토완료", StringComparison.Ordinal)) throw new InvalidOperationException("Unicode field was not retained in the reviewed name.");
            GetControl<CheckBox>("ApprovalCheckBox").IsChecked = true;
            ScrollWorkspaceToEnd(); await CaptureAsync("06-approved-review.png");
            var first = await ExportToAsync(root) ?? throw new InvalidOperationException("Approved export failed.");
            if (first.ExportedCount != 2 || !File.Exists(first.ManifestPath) || IsApproved) throw new InvalidOperationException("Export output or approval state is incorrect.");
            await CaptureAsync("07-results.png");
            checks.Add("Save/restore cleared approval; Unicode review edit invalidated approval; approved export recorded two PDFs and manifest.");
            ClickControl("Step2Button"); await NextFrameAsync();
            if (IsApproved) throw new InvalidOperationException("Returning to review retained approval.");
            GetControl<CheckBox>("ApprovalCheckBox").IsChecked = true;
            var second = await ExportToAsync(root) ?? throw new InvalidOperationException("Repeated export failed.");
            if (first.OutputDirectory == second.OutputDirectory || !File.Exists(first.ManifestPath)) throw new InvalidOperationException("Repeated export replaced the prior batch.");
            Close();
            await WaitAsync(() => OwnedWindows.Any(w => w.Title == "Close PaperStager?"), "Unsaved close confirmation");
            dialog = OwnedWindows.Single(w => w.Title == "Close PaperStager?");
            ClickDialog(dialog, "Cancel"); await WaitAsync(() => !OwnedWindows.Any(), "Cancelled close");
            if (!IsVisible) throw new InvalidOperationException("Cancelled close hid the window.");
            await SaveProjectToPathAsync(projectPath);
            await RestoreProjectAsync(projectPath); ClickControl("Step2Button"); await NextFrameAsync();
            if (ReviewPlan?.HasErrors != false || IsApproved) throw new InvalidOperationException("Project reopen failed or retained export approval.");
            var unchanged = Project.Sources.All(s => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(s.Path))).Equals(s.Sha256, StringComparison.OrdinalIgnoreCase));
            if (!unchanged) throw new InvalidOperationException("Source changed.");
            checks.Add("Repeat export used a new folder; close Cancel preserved the window; saved project reopened and source hashes match.");
            await File.WriteAllTextAsync(Path.Combine(root, "qa-result.json"), JsonSerializer.Serialize(new {
                success = true, nativeWindow = IsVisible, captureKind = "Avalonia RenderTargetBitmap of displayed client area (not OS screenshot)",
                sourcePreserved = unchanged, importedPages = 4, exportedDocumentsPerBatch = 2, thumbnailsRendered = RenderedThumbnailCount,
                captures, checks, notExercised = new[] { "OS file/folder picker interaction", "OS drag-and-drop", "OS window decorations", "process restart (project reopen is covered)", "operation cancellation (deterministic headless regression covers import cancellation)" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            _allowClose = true; lifetime.Shutdown(0);
        } catch (Exception ex) {
            await File.WriteAllTextAsync(Path.Combine(root, "qa-result.json"), JsonSerializer.Serialize(new { success = false, error = ex.ToString(), captures, checks }, new JsonSerializerOptions { WriteIndented = true }));
            _allowClose = true; lifetime.Shutdown(1);
        }

        async Task CaptureAsync(string name) { await CaptureWindowAsync(this, Path.Combine(root, name)); captures.Add(name); }
        void ClickControl(string name) => GetControl<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        T GetControl<T>(string name) where T : Control => this.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        void ScrollWorkspaceToEnd() { var scroll = GetControl<ScrollViewer>("WorkspaceScrollViewer"); scroll.Offset = new Vector(0, scroll.Extent.Height); }
        static void ClickDialog(Window dialog, string label) => dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task WaitAsync(Func<bool> done, string stage) {
            for (var attempt = 0; attempt < 200 && !done(); attempt++) await Task.Delay(50);
            if (!done()) throw new TimeoutException(stage + " did not complete within ten seconds. " + DiagnosticStatus);
        }
    }

    private static async Task NextFrameAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(100);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static async Task CaptureWindowAsync(Window window, string path)
    {
        await NextFrameAsync();
        if (!window.IsVisible || window.ClientSize.Width <= 0 || window.ClientSize.Height <= 0) throw new InvalidOperationException("Cannot capture an undisplayed window.");
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.ClientSize.Width), (int)Math.Ceiling(window.ClientSize.Height)), new Vector(96, 96));
        image.Render(window); image.Save(path, PngBitmapEncoderOptions.Default);
    }

    public async Task RunSmokeAsync(string? directory, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var root = directory ?? Path.Combine(Path.GetTempPath(), "paperstager-smoke-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            await LoadDemoAsync();
            for (var attempt = 0; attempt < 100 && RenderedThumbnailCount < 4; attempt++) await Task.Delay(50);
            if (RenderedThumbnailCount < 4) throw new InvalidOperationException("Actual PDF thumbnails did not render.");
            NavigateTo(0); NavigateTo(1); NavigateTo(2);
            if (ReviewPlan?.HasErrors != false) throw new InvalidOperationException("Synthetic plan has errors: " + string.Join("; ", ReviewPlan?.Documents.SelectMany(x => x.Errors) ?? []));
            var store = new ProjectStore(); await store.SaveAsync(Path.Combine(root, "project.json"), Project); Project = await store.LoadAsync(Path.Combine(root, "project.json"));
            await new TemplateStore().SaveAsync(Path.Combine(root, "template.json"), Project.Template); Project.Template = await new TemplateStore().LoadAsync(Path.Combine(root, "template.json")); NavigateTo(2); SetApproval(true);
            var result = await ExportToAsync(root) ?? throw new InvalidOperationException("Export did not finish.");
            if (result.ExportedCount != 2 || !File.Exists(result.ManifestPath)) throw new InvalidOperationException("Wrong exported document count or missing manifest.");
            var unchanged = Project.Sources.All(s => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(s.Path))).Equals(s.Sha256, StringComparison.OrdinalIgnoreCase));
            if (!unchanged) throw new InvalidOperationException("Source changed.");
            await File.WriteAllTextAsync(Path.Combine(root, "smoke-result.json"), JsonSerializer.Serialize(new { success = true, importedPages = 4, exportedDocuments = 2, thumbnailsRendered = RenderedThumbnailCount, sourcePreserved = unchanged, nativeWindow = IsVisible }));
            _allowClose = true; lifetime.Shutdown(0);
        } catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(root, "smoke-result.json"), JsonSerializer.Serialize(new { success = false, error = ex.ToString() })); _allowClose = true; lifetime.Shutdown(1); }
    }

    private sealed class RuleEditor
    {
        private readonly TextBox _name, _expression;
        private readonly ComboBox _kind;
        private readonly CheckBox _required;
        public StackPanel Panel { get; } = new() { Spacing = 5 };
        public Button Remove { get; } = new() { Content = "Remove", Padding = new Thickness(6, 3) };
        public RuleEditor(FieldRule rule, Action changed)
        {
            _name = new TextBox { Text = rule.Name, PlaceholderText = "Field name", Width = 120 };
            _kind = new ComboBox { ItemsSource = new[] { "After label", "Regex" }, SelectedIndex = rule.Kind == FieldRuleKind.AfterLabel ? 0 : 1, Width = 120 };
            _required = new CheckBox { Content = "Required", IsChecked = rule.Required };
            var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; top.Children.Add(_name); top.Children.Add(_kind); top.Children.Add(_required); top.Children.Add(Remove); Panel.Children.Add(top);
            _expression = new TextBox { Text = rule.Expression, PlaceholderText = "Label (Client:) or regular expression with capture group", AcceptsReturn = true, MaxHeight = 75 }; Panel.Children.Add(_expression);
            var previous = Value;
            void TrackChange()
            {
                var current = Value;
                if (current.Name == previous.Name && current.Kind == previous.Kind && current.Expression == previous.Expression && current.Required == previous.Required) return;
                previous = current; changed();
            }
            _name.TextChanged += (_, _) => TrackChange(); _expression.TextChanged += (_, _) => TrackChange();
            _kind.SelectionChanged += (_, _) => TrackChange(); _required.IsCheckedChanged += (_, _) => TrackChange();
        }
        public FieldRule Value => new() { Name = _name.Text ?? "", Kind = _kind.SelectedIndex == 0 ? FieldRuleKind.AfterLabel : FieldRuleKind.Regex, Expression = _expression.Text ?? "", Required = _required.IsChecked == true };
    }
}
