using System.Diagnostics;
using WinTracer.Localization;

namespace WinTracer.Core.RegistryTools;

public sealed record FixResult(int Fixed, int Failed, string BackupFile, IReadOnlyList<string> Errors);

public static class RegistryFixer
{
    public static string BackupFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTracer", "Backups");

    public static FixResult Fix(IReadOnlyList<RegistryIssue> issues)
    {
        var backup = new RegFileBuilder();
        foreach (var issue in issues)
        {
            if (issue.IsWholeKey) backup.AddKey(issue.Key);
            else foreach (var v in issue.ValueNames) backup.AddValue(issue.Key, v);
        }

        string file = Path.Combine(BackupFolder, $"registry-{DateTime.Now:yyyyMMdd-HHmmss}.reg");
        backup.Save(file);

        int ok = 0, failed = 0;
        var errors = new List<string>();

        var firewall = issues.Where(i => i.Category == RegistryIssueCategory.FirewallRules).ToList();
        if (firewall.Count > 0)
        {
            string ids = string.Join(",", firewall.Select(i => "'" + i.ValueNames[0].Replace("'", "''") + "'"));
            string? error = RunPowerShell($"Remove-NetFirewallRule -Name {ids} -ErrorAction Stop");
            if (error is null) ok += firewall.Count;
            else
            {
                failed += firewall.Count;
                errors.Add(L.F("Registry.FirewallError", error));
            }
        }

        foreach (var issue in issues.Where(i => i.Category != RegistryIssueCategory.FirewallRules))
        {
            try
            {
                if (issue.IsWholeKey)
                {
                    int slash = issue.Key.Path.LastIndexOf('\\');
                    using var parent = (issue.Key with { Path = issue.Key.Path[..slash] }).Open(writable: true);
                    parent?.DeleteSubKeyTree(issue.Key.Path[(slash + 1)..], throwOnMissingSubKey: false);
                }
                else
                {
                    using var key = issue.Key.Open(writable: true);
                    foreach (var v in issue.ValueNames) key?.DeleteValue(v, throwOnMissingValue: false);
                }
                ok++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                failed++;
                if (errors.Count < 5) errors.Add($"{issue.Title}: {ex.Message}");
            }
        }
        return new FixResult(ok, failed, file, errors);
    }

    private static string? RunPowerShell(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);
        using var p = Process.Start(psi)!;
        string err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 ? null : (err.Trim().Split('\n').FirstOrDefault()?.Trim() ?? L.F("Common.ExitCode", p.ExitCode));
    }

    public static async Task<string?> CreateRestorePointAsync(string description)
    {
        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi)!;
        var errTask = p.StandardError.ReadToEndAsync();
        await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        string err = (await errTask).Trim();
        return p.ExitCode == 0 ? null : (err.Length > 0 ? err.Split('\n')[0].Trim() : L.F("Common.ExitCode", p.ExitCode));
    }
}
