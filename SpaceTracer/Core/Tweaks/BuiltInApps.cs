using System.Diagnostics;
using System.Text.Json;
using WinTracer.Localization;

namespace WinTracer.Core.Tweaks;

public sealed record BuiltInApp(string PackageName)
{
    public string Title => L.T($"App.{PackageName}");
    public string? Warning => L.TryT($"App.{PackageName}.Warning");
}

public static class BuiltInApps
{
    public static IReadOnlyList<BuiltInApp> Catalog { get; } =
    [
        new("Microsoft.BingNews"),
        new("Microsoft.BingWeather"),
        new("Microsoft.GetHelp"),
        new("Microsoft.Getstarted"),
        new("Microsoft.WindowsFeedbackHub"),
        new("Microsoft.MicrosoftSolitaireCollection"),
        new("Microsoft.Clipchamp"),
        new("Microsoft.Todos"),
        new("Microsoft.People"),
        new("Microsoft.WindowsMaps"),
        new("Microsoft.WindowsAlarms"),
        new("Microsoft.WindowsSoundRecorder"),
        new("Microsoft.ZuneVideo"),
        new("Microsoft.ZuneMusic"),
        new("Microsoft.YourPhone"),
        new("Microsoft.WindowsCamera"),
        new("Microsoft.Paint"),
        new("MicrosoftTeams"),
        new("MSTeams"),
        new("Microsoft.OutlookForWindows"),
        new("Microsoft.DevHome"),
        new("Microsoft.549981C3F5F10"),
        new("Microsoft.Copilot"),
        new("Microsoft.XboxGamingOverlay"),
        new("Microsoft.XboxGameOverlay"),
        new("Microsoft.XboxSpeechToTextOverlay"),
        new("Microsoft.Xbox.TCUI"),
        new("Microsoft.XboxIdentityProvider"),
    ];

    public static async Task<HashSet<string>> GetInstalledAsync()
    {
        string json = await RunPowerShellAsync("Get-AppxPackage | Select-Object -ExpandProperty Name | ConvertTo-Json -Compress");
        var names = string.IsNullOrWhiteSpace(json)
            ? []
            : json.TrimStart().StartsWith('[')
                ? JsonSerializer.Deserialize<string[]>(json) ?? []
                : [JsonSerializer.Deserialize<string>(json) ?? ""];
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    public static Task RemoveAsync(string packageName)
    {
        string safe = packageName.Replace("'", "''");
        return RunPowerShellAsync($"Get-AppxPackage -Name '{safe}' | Remove-AppxPackage -ErrorAction Stop");
    }

    private static async Task<string> RunPowerShellAsync(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        string err = (await stderr).Trim();
        if (p.ExitCode != 0 || err.Length > 0 && command.Contains("Remove-AppxPackage"))
        {
            if (err.Length > 0) throw new InvalidOperationException(err.Split('\n')[0].Trim());
        }
        return await stdout;
    }
}
