using WinTracer.Services;
using WinTracer.Views.Pages;
using Wpf.Ui.Controls;
using WinTracer.Localization;

namespace WinTracer.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(IServiceProvider services, ThemeService theme)
    {
        InitializeComponent();

        if (SystemShell.IsElevated)
            TitleBar.Title = L.T("App.AdminTitle");

        RootNavigation.SetServiceProvider(services);
        theme.Attach(this);

        Loaded += (_, _) => RootNavigation.Navigate(StartPage());
    }

    private static Type StartPage()
    {
        var arg = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        return arg?["--page=".Length..].ToLowerInvariant() switch
        {
            "cleanup" => typeof(CleanupPage),
            "startup" => typeof(StartupPage),
            "registry" => typeof(RegistryPage),
            "tweaks" => typeof(TweaksPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(DiskAnalyzerPage),
        };
    }
}
