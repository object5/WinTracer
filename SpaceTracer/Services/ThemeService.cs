using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WinTracer.Services;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public sealed class ThemeService
{
    private Window? _window;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public void Attach(Window window)
    {
        _window = window;
        SetMode(ThemeMode.System);
    }

    public void SetMode(ThemeMode mode)
    {
        Mode = mode;
        if (_window is null) return;

        if (mode == ThemeMode.System)
        {
            ApplicationThemeManager.Apply(IsSystemDark() ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica);
            SystemThemeWatcher.Watch(_window, WindowBackdropType.Mica, updateAccents: true);
        }
        else
        {
            SystemThemeWatcher.UnWatch(_window);
            ApplicationThemeManager.Apply(mode == ThemeMode.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica);
        }
    }

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}
