using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTracer.Services;
using WinTracer.Localization;

namespace WinTracer.ViewModels;

public sealed partial class SettingsViewModel(ThemeService theme) : ObservableObject
{
    public IReadOnlyList<KeyValuePair<ThemeMode, string>> Themes { get; } =
    [
        new(ThemeMode.System, L.T("Settings.Theme.System")),
        new(ThemeMode.Light, L.T("Settings.Theme.Light")),
        new(ThemeMode.Dark, L.T("Settings.Theme.Dark")),
    ];

    public ThemeMode SelectedTheme
    {
        get => theme.Mode;
        set
        {
            if (theme.Mode == value) return;
            theme.SetMode(value);
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<KeyValuePair<string, string>> Languages { get; } =
    [
        new("", L.T("Settings.Language.System")),
        new("en", "English"),
        new("ru", "Русский"),
    ];

    private readonly string _startLanguage = L.SavedLanguage;

    public string SelectedLanguage
    {
        get => L.SavedLanguage;
        set
        {
            if (L.SavedLanguage == value) return;
            L.SavedLanguage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageChanged));
        }
    }

    public bool LanguageChanged => L.SavedLanguage != _startLanguage;

    [RelayCommand]
    private static void Restart()
    {
        if (Environment.ProcessPath is { } exe)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }
    }

    public bool IsElevated => SystemShell.IsElevated;

    public string Version =>
        typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";

    [RelayCommand]
    private static void RestartElevated() => SystemShell.RestartElevated();
}
