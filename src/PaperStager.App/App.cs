using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
namespace PaperStager.App;
public sealed class App : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(); desktop.MainWindow = window;
            window.Opened += async (_, _) => {
                var args = desktop.Args ?? [];
                if (args.Contains("--smoke")) await window.RunSmokeAsync(args.SkipWhile(x => x != "--smoke").Skip(1).FirstOrDefault(), desktop);
                else if (args.Contains("--demo")) await window.LoadDemoAsync();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
