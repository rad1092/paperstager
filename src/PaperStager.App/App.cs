using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
namespace PaperStager.App;
public sealed class App : Application
{
    public override void Initialize() { Name = "PaperStager"; Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(); desktop.MainWindow = window;
            window.Opened += async (_, _) => {
                var args = desktop.Args ?? [];
                using var watchdog = new CancellationTokenSource();
                var automaticMode = args.Contains("--qa") ? "--qa" : args.Contains("--smoke") ? "--smoke" : null;
                if (automaticMode != null) _ = StopStalledAutomationAsync(args, automaticMode, watchdog.Token);
                try
                {
                    if (args.Contains("--qa")) await window.RunQaAsync(args.SkipWhile(x => x != "--qa").Skip(1).FirstOrDefault(), desktop);
                    else if (args.Contains("--smoke")) await window.RunSmokeAsync(args.SkipWhile(x => x != "--smoke").Skip(1).FirstOrDefault(), desktop);
                    else if (args.Contains("--demo")) await window.LoadDemoAsync();
                }
                finally { watchdog.Cancel(); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    // Applies only to explicit synthetic automation modes, never an interactive user session.
    // LaunchServices owns a macOS bundle process, so its own watchdog must end a stalled run.
    private static async Task StopStalledAutomationAsync(string[] args, string mode, CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(90), token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        try
        {
            var directory = args.SkipWhile(x => x != mode).Skip(1).FirstOrDefault();
            if (directory != null && Directory.Exists(directory))
                await File.WriteAllTextAsync(Path.Combine(directory, mode == "--qa" ? "qa-result.json" : "smoke-result.json"),
                    "{\"success\":false,\"error\":\"Synthetic automation exceeded its 90-second limit.\"}").ConfigureAwait(false);
        }
        catch (IOException) { }
        finally { Environment.Exit(124); }
    }
}
