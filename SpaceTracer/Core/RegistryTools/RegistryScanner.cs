using Microsoft.Win32;
using WinTracer.Core.Startup;
using WinTracer.Localization;

namespace WinTracer.Core.RegistryTools;

public enum RegistryIssueCategory
{
    Uninstall,
    AppPaths,
    FileAssociations,
    FirewallRules,
    SharedDlls,
    InstallerFolders,
    MuiCache,
}

public sealed record RegistryIssue(
    RegistryIssueCategory Category,
    string Title,
    string MissingTarget,
    RegKeyRef Key,
    IReadOnlyList<string> ValueNames)
{
    public bool IsWholeKey => ValueNames.Count == 0;
    public bool RequiresAdmin => Key.Hive == RegistryHive.LocalMachine;
    public string Location => IsWholeKey ? Key.ShortPath : $"{Key.ShortPath} → {string.Join(", ", ValueNames)}";
}

public static class RegistryScanner
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string SharedDllsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
    private const string InstallerFoldersKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders";
    private const string MuiCacheKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
    private const string ApplicationsKey = @"Software\Classes\Applications";
    private const string FirewallRulesKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

    private static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static readonly HashSet<string> FixedDrives = DriveInfo.GetDrives()
        .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
        .Select(d => d.Name.TrimEnd('\\').ToUpperInvariant())
        .ToHashSet();

    public sealed record ScanResult(IReadOnlyList<RegistryIssue> Issues, IReadOnlyList<string> Errors);

    public static ScanResult Scan(CancellationToken ct = default)
    {
        var issues = new List<RegistryIssue>();
        var errors = new List<string>();

        void Run(string what, Action action)
        {
            ct.ThrowIfCancellationRequested();
            try { action(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add($"{what}: {ex.GetBaseException().Message}"); }
        }

        Run(L.T("Registry.Section.Uninstall"), () =>
        {
            ScanUninstall(new(RegistryHive.CurrentUser, RegistryView.Registry64, UninstallKey), issues);
            ScanUninstall(new(RegistryHive.LocalMachine, RegistryView.Registry64, UninstallKey), issues);
            ScanUninstall(new(RegistryHive.LocalMachine, RegistryView.Registry32, UninstallKey), issues);
        });
        Run("App Paths", () =>
        {
            ScanAppPaths(new(RegistryHive.CurrentUser, RegistryView.Registry64, AppPathsKey), issues);
            ScanAppPaths(new(RegistryHive.LocalMachine, RegistryView.Registry64, AppPathsKey), issues);
            ScanAppPaths(new(RegistryHive.LocalMachine, RegistryView.Registry32, AppPathsKey), issues);
        });
        Run(L.T("Registry.Section.Associations"), () => ScanApplications(issues));
        Run(L.T("Registry.Section.Firewall"), () => ScanFirewall(issues));
        Run(L.T("Registry.Section.SharedDlls"), () =>
        {
            ScanPathValues(new(RegistryHive.LocalMachine, RegistryView.Registry64, SharedDllsKey), RegistryIssueCategory.SharedDlls, issues, isFolder: false);
            ScanPathValues(new(RegistryHive.LocalMachine, RegistryView.Registry32, SharedDllsKey), RegistryIssueCategory.SharedDlls, issues, isFolder: false);
        });
        Run(L.T("Registry.Section.InstallerFolders"), () =>
            ScanPathValues(new(RegistryHive.LocalMachine, RegistryView.Registry64, InstallerFoldersKey), RegistryIssueCategory.InstallerFolders, issues, isFolder: true));
        Run("MUICache", () => ScanMuiCache(issues));

        return new ScanResult(issues, errors);
    }

    internal static bool IsProvablyMissing(string? path, bool folder = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (path.Contains('%') || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\")) return false;

        string? root = Path.GetPathRoot(path)?.TrimEnd('\\').ToUpperInvariant();
        if (root is null || !FixedDrives.Contains(root)) return false;

        try
        {
            return folder ? !Directory.Exists(path) : !File.Exists(path) && !Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static void ScanUninstall(RegKeyRef parent, List<RegistryIssue> issues)
    {
        using var key = parent.Open();
        if (key is null) return;

        foreach (var sub in key.GetSubKeyNames())
        {
            using var entry = key.OpenSubKey(sub);
            if (entry is null) continue;

            string? name = entry.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (entry.GetValue("SystemComponent") is int sc && sc == 1) continue;
            if (entry.GetValue("WindowsInstaller") is int wi && wi == 1) continue;
            if (entry.GetValue("ParentKeyName") is string) continue;

            string? uninstall = entry.GetValue("UninstallString") as string;
            string? uninstallExe = CommandLine.GetExecutable(uninstall);
            string? location = entry.GetValue("InstallLocation") as string;

            bool uninstallerIsSystem = uninstallExe is not null && PathSafety.IsSameOrUnder(uninstallExe, WindowsDir);
            bool uninstallerMissing = !uninstallerIsSystem && IsProvablyMissing(uninstallExe);
            bool hasLocation = !string.IsNullOrWhiteSpace(location);
            bool locationMissing = hasLocation && IsProvablyMissing(location, folder: true);

            bool stale = (uninstallerMissing && (!hasLocation || locationMissing)) ||
                         (locationMissing && (uninstallerIsSystem || uninstallExe is null || uninstallerMissing));
            if (!stale) continue;

            issues.Add(new RegistryIssue(RegistryIssueCategory.Uninstall, name.Trim(),
                uninstallerMissing ? uninstallExe! : location!,
                parent with { Path = $@"{parent.Path}\{sub}" }, []));
        }
    }

    private static void ScanAppPaths(RegKeyRef parent, List<RegistryIssue> issues)
    {
        using var key = parent.Open();
        if (key is null) return;

        foreach (var sub in key.GetSubKeyNames())
        {
            using var entry = key.OpenSubKey(sub);
            if (entry?.GetValue("") is not string exePath) continue;
            string exe = exePath.Trim().Trim('"');
            if (!IsProvablyMissing(exe)) continue;
            issues.Add(new RegistryIssue(RegistryIssueCategory.AppPaths, sub, exe,
                parent with { Path = $@"{parent.Path}\{sub}" }, []));
        }
    }

    private static void ScanApplications(List<RegistryIssue> issues)
    {
        var parent = new RegKeyRef(RegistryHive.CurrentUser, RegistryView.Registry64, ApplicationsKey);
        using var key = parent.Open();
        if (key is null) return;

        foreach (var app in key.GetSubKeyNames())
        {
            using var shell = key.OpenSubKey($@"{app}\shell");
            if (shell is null) continue;

            string? missing = null;
            bool anyExisting = false;
            foreach (var verb in shell.GetSubKeyNames())
            {
                using var cmd = shell.OpenSubKey($@"{verb}\command");
                if (cmd?.GetValue("") is not string command) continue;
                string? exe = CommandLine.GetExecutable(command);
                if (IsProvablyMissing(exe)) missing ??= exe;
                else anyExisting = true;
            }
            if (missing is null || anyExisting) continue;

            issues.Add(new RegistryIssue(RegistryIssueCategory.FileAssociations, app, missing,
                parent with { Path = $@"{parent.Path}\{app}" }, []));
        }
    }

    private static void ScanFirewall(List<RegistryIssue> issues)
    {
        var keyRef = new RegKeyRef(RegistryHive.LocalMachine, RegistryView.Registry64, FirewallRulesKey);
        using var key = keyRef.Open();
        if (key is null) return;

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is not string rule) continue;
            string? app = null, ruleName = null;
            foreach (var part in rule.Split('|'))
            {
                if (part.StartsWith("App=", StringComparison.OrdinalIgnoreCase)) app = part[4..];
                else if (part.StartsWith("Name=", StringComparison.OrdinalIgnoreCase)) ruleName = part[5..];
            }
            if (app is null || !IsProvablyMissing(app)) continue;
            if (PathSafety.IsSameOrUnder(Environment.ExpandEnvironmentVariables(app), WindowsDir)) continue;

            string title = ruleName is null || ruleName.StartsWith('@') ? Path.GetFileName(app) : ruleName;
            issues.Add(new RegistryIssue(RegistryIssueCategory.FirewallRules, title,
                Environment.ExpandEnvironmentVariables(app), keyRef, [name]));
        }
    }

    private static void ScanPathValues(RegKeyRef keyRef, RegistryIssueCategory category, List<RegistryIssue> issues, bool isFolder)
    {
        using var key = keyRef.Open();
        if (key is null) return;

        foreach (var name in key.GetValueNames())
        {
            if (!IsProvablyMissing(name, isFolder)) continue;
            issues.Add(new RegistryIssue(category, Path.GetFileName(name.TrimEnd('\\')), name, keyRef, [name]));
        }
    }

    private static void ScanMuiCache(List<RegistryIssue> issues)
    {
        var keyRef = new RegKeyRef(RegistryHive.CurrentUser, RegistryView.Registry64, MuiCacheKey);
        using var key = keyRef.Open();
        if (key is null) return;

        var byExe = new Dictionary<string, (string Title, List<string> Values)>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in key.GetValueNames())
        {
            int dot = name.LastIndexOf(".exe.", StringComparison.OrdinalIgnoreCase);
            if (dot < 0) continue;
            string exe = name[..(dot + 4)];
            if (!byExe.TryGetValue(exe, out var entry))
            {
                if (!IsProvablyMissing(exe)) continue;
                entry = (Path.GetFileName(exe), []);
                byExe[exe] = entry;
            }
            entry.Values.Add(name);
            if (name.EndsWith(".FriendlyAppName", StringComparison.OrdinalIgnoreCase) &&
                key.GetValue(name) is string { Length: > 0 } friendly)
                byExe[exe] = (friendly, entry.Values);
        }

        foreach (var (exe, (title, values)) in byExe)
            issues.Add(new RegistryIssue(RegistryIssueCategory.MuiCache, title, exe, keyRef, values));
    }

    public static bool IsSelectedByDefault(RegistryIssueCategory c) =>
        c is not (RegistryIssueCategory.SharedDlls or RegistryIssueCategory.InstallerFolders);

    public static string CategoryTitle(RegistryIssueCategory c) => L.T($"Registry.{c}.Title");

    public static string CategoryDescription(RegistryIssueCategory c) => L.T($"Registry.{c}.Description");
}
