using System.Globalization;
using System.Resources;
using System.Windows.Markup;
using Microsoft.Win32;

namespace WinTracer.Localization;

public static class L
{
    private static readonly ResourceManager Rm = new("WinTracer.Resources.Strings", typeof(L).Assembly);

    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public static string T(string key) => Rm.GetString(key, Culture) ?? $"[{key}]";

    public static string? TryT(string key) => Rm.GetString(key, Culture);

    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    private const string SettingsKey = @"Software\WinTracer";

    public static string SavedLanguage
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return key?.GetValue("Language") as string ?? "";
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey, writable: true);
            key.SetValue("Language", value);
        }
    }

    public static void ApplySavedLanguage()
    {
        string lang = SavedLanguage;
        if (lang is not ("en" or "ru"))
            lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";

        Use(CultureInfo.GetCultureInfo(lang));
    }

    public static void Use(CultureInfo culture)
    {
        Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
