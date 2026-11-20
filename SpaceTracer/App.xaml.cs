using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WinTracer.Core.Scanning;
using WinTracer.Services;
using WinTracer.ViewModels;
using WinTracer.Views;
using WinTracer.Views.Pages;
using WinTracer.Localization;

namespace WinTracer;

public partial class App : Application
{
    private readonly IHost _host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton<ThemeService>();
            services.AddSingleton<IDiskScanner, DiskScanner>();

            services.AddSingleton<MainWindow>();

            services.AddSingleton<DiskAnalyzerViewModel>();
            services.AddSingleton<DiskAnalyzerPage>();
            services.AddSingleton<StartupViewModel>();
            services.AddSingleton<StartupPage>();
            services.AddSingleton<CleanupViewModel>();
            services.AddSingleton<CleanupPage>();
            services.AddSingleton<RegistryViewModel>();
            services.AddSingleton<RegistryPage>();
            services.AddSingleton<TweaksViewModel>();
            services.AddSingleton<TweaksPage>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<SettingsPage>();
        })
        .Build();

    public static IServiceProvider Services => ((App)Current)._host.Services;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        L.ApplySavedLanguage();

        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(
                System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag)));

        await _host.StartAsync();
        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show(e.Exception.ToString(), L.T("App.ErrorTitle"),
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
