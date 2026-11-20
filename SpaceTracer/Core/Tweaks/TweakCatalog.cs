using WinTracer.Localization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinTracer.Core.Tweaks;

public static partial class TweakCatalog
{
    private const RegistryHive HKCU = RegistryHive.CurrentUser;
    private const RegistryHive HKLM = RegistryHive.LocalMachine;

    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private const string UltimateSource = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string UltimateCopy = "5a1f0b3e-6c2d-4b7a-9e1f-7d2c3b4a5e60";
    private const string BalancedScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string StateKey = @"Software\WinTracer\Tweaks";

    public static IReadOnlyList<Tweak> Create() =>
    [
        new RegistryTweak
        {
            Id = "privacy.telemetry", Group = TweakGroup.Privacy, RequiresAdmin = true,
            Settings =
            [
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DisableDeviceNameUsage", 1),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowDesktopAnalytics", 0),
                new(HKCU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
            ],
        },
        new RegistryTweak
        {
            Id = "privacy.advertising", Group = TweakGroup.Privacy,
            Settings =
            [
                new(HKCU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0) { Default = 1 },
                new(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0) { Default = 1 },
                new(HKCU, Advanced, "Start_TrackProgs", 0) { Default = 1 },
            ],
        },
        new RegistryTweak
        {
            Id = "privacy.inking", Group = TweakGroup.Privacy,
            Settings =
            [
                new(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1) { Default = 0 },
                new(HKCU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1) { Default = 0 },
                new(HKCU, @"Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0) { Default = 1 },
                new(HKCU, @"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0),
                new(HKCU, @"Software\Microsoft\Input\TIPC", "Enabled", 0),
                new(HKCU, @"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0),
            ],
        },
        new RegistryTweak
        {
            Id = "privacy.activity", Group = TweakGroup.Privacy, RequiresAdmin = true,
            Settings =
            [
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0),
            ],
        },
        new RegistryTweak
        {
            Id = "privacy.copilot", Group = TweakGroup.Privacy, RequiresAdmin = true, Effect = TweakEffect.RestartExplorer,
            Settings =
            [
                new(HKCU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
                new(HKCU, Advanced, "ShowCopilotButton", 0),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0),
            ],
        },
        new RegistryTweak
        {
            Id = "privacy.recall", Group = TweakGroup.Privacy, RequiresAdmin = true, Effect = TweakEffect.Reboot,
            Settings =
            [
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1),
            ],
        },
        new RegistryTweak
        {
            Id = "privacy.web-search", Group = TweakGroup.Privacy, Effect = TweakEffect.RestartExplorer,
            Settings =
            [
                new(HKCU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1),
                new(HKCU, @"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0) { Default = 1 },
            ],
        },
        new RegistryTweak
        {
            Id = "privacy.spotlight", Group = TweakGroup.Privacy,
            Settings = [new(HKCU, @"Software\Policies\Microsoft\Windows\CloudContent", "DisableWindowsSpotlightFeatures", 1)],
        },

        new RegistryTweak
        {
            Id = "ui.ads", Group = TweakGroup.Interface, Effect = TweakEffect.RestartExplorer,
            Settings =
            [
                new(HKCU, Advanced, "Start_IrisRecommendations", 0) { Default = 1 },
                new(HKCU, Advanced, "Start_AccountNotifications", 0) { Default = 1 },
                new(HKCU, Advanced, "ShowSyncProviderNotifications", 0) { Default = 1 },
                new(HKCU, Cdm, "RotatingLockScreenEnabled", 0) { Default = 1 },
                new(HKCU, Cdm, "SubscribedContent-338387Enabled", 0) { Default = 1 },
                new(HKCU, Cdm, "SilentInstalledAppsEnabled", 0) { Default = 1 },
                new(HKCU, Cdm, "SystemPaneSuggestionsEnabled", 0) { Default = 1 },
            ],
        },
        new RegistryTweak
        {
            Id = "ui.consumer-features", Group = TweakGroup.Interface, RequiresAdmin = true,
            Settings =
            [
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1),
            ],
        },
        new RegistryTweak
        {
            Id = "ui.widgets", Group = TweakGroup.Interface, RequiresAdmin = true, Effect = TweakEffect.RestartExplorer,
            Settings =
            [
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0),
                new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Chat", "ChatIcon", 3),
                new(HKCU, Advanced, "TaskbarMn", 0),
            ],
        },
        new RegistryTweak
        {
            Id = "ui.this-pc", Group = TweakGroup.Interface,
            Settings = [new(HKCU, Advanced, "LaunchTo", 1)],
        },
        new RegistryTweak
        {
            Id = "ui.extensions", Group = TweakGroup.Interface, Effect = TweakEffect.RestartExplorer,
            Settings = [new(HKCU, Advanced, "HideFileExt", 0) { Default = 1 }],
        },
        new RegistryTweak
        {
            Id = "ui.hidden", Group = TweakGroup.Interface, Effect = TweakEffect.RestartExplorer,
            Settings = [new(HKCU, Advanced, "Hidden", 1) { Default = 2 }],
        },
        new RegistryTweak
        {
            Id = "ui.end-task", Group = TweakGroup.Interface,
            Settings = [new(HKCU, Advanced + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1)],
        },

        new RegistryTweak
        {
            Id = "perf.startup-delay", Group = TweakGroup.Performance,
            Settings = [new(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0)],
        },
        new RegistryTweak
        {
            Id = "perf.game-mode", Group = TweakGroup.Performance,
            Settings =
            [
                new(HKCU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1),
                new(HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
            ],
        },
        new RegistryTweak
        {
            Id = "perf.game-dvr", Group = TweakGroup.Performance,
            Settings =
            [
                new(HKCU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0),
                new(HKCU, @"System\GameConfigStore", "GameDVR_Enabled", 0) { Default = 1 },
            ],
        },
        new RegistryTweak
        {
            Id = "perf.game-bar", Group = TweakGroup.Performance,
            Settings =
            [
                new(HKCU, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0),
                new(HKCU, @"Software\Microsoft\GameBar", "ShowStartupPanel", 0),
            ],
        },
        new RegistryTweak
        {
            Id = "perf.hags", Group = TweakGroup.Performance, RequiresAdmin = true, Effect = TweakEffect.Reboot,
            Settings = [new(HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2) { Default = 1 }],
        },
        new CommandTweak
        {
            Id = "perf.ultimate", Group = TweakGroup.Performance,
            Detect = () => ActiveScheme().Equals(UltimateCopy, StringComparison.OrdinalIgnoreCase) ? TweakState.Applied : TweakState.NotApplied,
            ApplyAction = () =>
            {
                string current = ActiveScheme();
                if (!current.Equals(UltimateCopy, StringComparison.OrdinalIgnoreCase))
                    SetState("PreviousPowerScheme", current);
                if (!CommandTweak.Run("powercfg", "/list").Contains(UltimateCopy, StringComparison.OrdinalIgnoreCase))
                    CommandTweak.Run("powercfg", "-duplicatescheme", UltimateSource, UltimateCopy);
                CommandTweak.Run("powercfg", "/setactive", UltimateCopy);
            },
            RevertAction = () =>
            {
                string previous = GetState("PreviousPowerScheme") ?? BalancedScheme;
                CommandTweak.Run("powercfg", "/setactive", previous);
            },
        },
        new RegistryTweak
        {
            Id = "perf.power-throttling", Group = TweakGroup.Performance, RequiresAdmin = true, Effect = TweakEffect.Reboot,
            Settings = [new(HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1)],
        },
        new RegistryTweak
        {
            Id = "perf.network-throttling", Group = TweakGroup.Performance, RequiresAdmin = true, Effect = TweakEffect.Reboot,
            Settings =
            [
                new(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", 0xFFFFFFFFL) { Default = 10 },
                new(HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10) { Default = 20 },
            ],
        },
        new RegistryTweak
        {
            Id = "perf.mouse-accel", Group = TweakGroup.Performance,
            Settings =
            [
                new(HKCU, @"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String) { Default = "1" },
                new(HKCU, @"Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String) { Default = "6" },
                new(HKCU, @"Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String) { Default = "10" },
            ],
            AfterChange = ApplyMouseSettingsLive,
        },
        new CommandTweak
        {
            Id = "perf.dynamic-tick", Group = TweakGroup.Performance, RequiresAdmin = true, Effect = TweakEffect.Reboot,
            Detect = () =>
                CommandTweak.Run("bcdedit", "/enum", "{current}")
                    .Split('\n')
                    .Any(l => l.TrimStart().StartsWith("disabledynamictick", StringComparison.OrdinalIgnoreCase) &&
                              l.Contains("Yes", StringComparison.OrdinalIgnoreCase))
                    ? TweakState.Applied : TweakState.NotApplied,
            ApplyAction = () => CommandTweak.Run("bcdedit", "/set", "disabledynamictick", "yes"),
            RevertAction = () => CommandTweak.Run("bcdedit", "/deletevalue", "disabledynamictick"),
        },

        new RegistryTweak
        {
            Id = "upd.delivery", Group = TweakGroup.Updates, RequiresAdmin = true,
            Settings = [new(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)],
        },
    ];

    public static string GroupTitle(TweakGroup g) => L.T($"TweakGroup.{g}");

    private static string ActiveScheme()
    {
        string output = CommandTweak.Run("powercfg", "/getactivescheme");
        var match = System.Text.RegularExpressions.Regex.Match(output, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value : "";
    }

    private static void SetState(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StateKey, writable: true);
        key.SetValue(name, value);
    }

    private static string? GetState(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StateKey);
        return key?.GetValue(name) as string;
    }

    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPIF_SENDCHANGE = 0x02;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint action, uint param, int[] data, uint winIni);

    private static void ApplyMouseSettingsLive()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
        int Read(string n, int d) => int.TryParse(key?.GetValue(n) as string, out int v) ? v : d;
        int[] values = [Read("MouseThreshold1", 6), Read("MouseThreshold2", 10), Read("MouseSpeed", 1)];
        SystemParametersInfoW(SPI_SETMOUSE, 0, values, SPIF_SENDCHANGE);
    }
}
