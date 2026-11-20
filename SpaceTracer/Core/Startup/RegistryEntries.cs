using System.Text;
using Microsoft.Win32;
using WinTracer.Localization;

namespace WinTracer.Core.Startup;

internal static class StartupApproved
{
    private const string Base = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static bool IsEnabled(RegistryHive hive, string subKey, string valueName)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(Base + subKey);
        return key?.GetValue(valueName) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
    }

    public static void Set(RegistryHive hive, string subKey, string valueName, bool enabled)
    {
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.CreateSubKey(Base + subKey, writable: true);
        key.SetValue(valueName, data, RegistryValueKind.Binary);
    }

    public static void Remove(RegistryHive hive, string subKey, string valueName)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(Base + subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

public sealed class RegistryRunEntry : StartupEntry
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal RegistryHive Hive { get; init; }
    internal RegistryView View { get; init; }

    internal string ApprovedSubKey { get; init; } = "Run";

    public override StartupKind Kind => StartupKind.Registry;
    public override bool RequiresAdmin => Hive == RegistryHive.LocalMachine;
    public override bool CanDelete => true;

    internal void LoadState() => IsEnabled = StartupApproved.IsEnabled(Hive, ApprovedSubKey, Name);

    public override void SetEnabled(bool enabled)
    {
        StartupApproved.Set(Hive, ApprovedSubKey, Name, enabled);
        IsEnabled = enabled;
    }

    public override void Delete(string backupFolder)
    {
        using var root = RegistryKey.OpenBaseKey(Hive, View);
        using var key = root.OpenSubKey(RunKey, writable: true)
                        ?? throw new InvalidOperationException(L.T("Startup.Error.RunKeyMissing"));
        string value = key.GetValue(Name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? Command;

        WriteRegBackup(backupFolder, value);
        key.DeleteValue(Name, throwOnMissingValue: false);
        StartupApproved.Remove(Hive, ApprovedSubKey, Name);
    }

    private void WriteRegBackup(string folder, string value)
    {
        Directory.CreateDirectory(folder);
        string hiveName = Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
        string keyPath = Hive == RegistryHive.LocalMachine && View == RegistryView.Registry32
            ? @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            : RunKey;

        static string Esc(string s) => s.Replace(@"\", @"\\").Replace("\"", "\\\"");
        var sb = new StringBuilder()
            .AppendLine("Windows Registry Editor Version 5.00")
            .AppendLine()
            .AppendLine($"[{hiveName}\\{keyPath}]")
            .AppendLine($"\"{Esc(Name)}\"=\"{Esc(value)}\"");

        string file = Path.Combine(folder, $"startup-{DateTime.Now:yyyyMMdd-HHmmss}-{Sanitize(Name)}.reg");
        File.WriteAllText(file, sb.ToString(), Encoding.Unicode);
    }

    internal static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

public sealed class StartupFolderEntry : StartupEntry
{
    internal RegistryHive Hive { get; init; }
    public required string FilePath { get; init; }

    public override StartupKind Kind => StartupKind.StartupFolder;
    public override bool RequiresAdmin => Hive == RegistryHive.LocalMachine;
    public override bool CanDelete => true;

    private string FileName => Path.GetFileName(FilePath);

    internal void LoadState() => IsEnabled = StartupApproved.IsEnabled(Hive, "StartupFolder", FileName);

    public override void SetEnabled(bool enabled)
    {
        StartupApproved.Set(Hive, "StartupFolder", FileName, enabled);
        IsEnabled = enabled;
    }

    public override void Delete(string backupFolder)
    {
        Directory.CreateDirectory(backupFolder);
        File.Copy(FilePath, Path.Combine(backupFolder, $"startup-{DateTime.Now:yyyyMMdd-HHmmss}-{FileName}"), overwrite: true);
        File.Delete(FilePath);
        StartupApproved.Remove(Hive, "StartupFolder", FileName);
    }
}

public sealed class ServiceEntry : StartupEntry
{
    internal const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    internal const string TrackingKey = @"Software\WinTracer\ManualServices";

    public required string ServiceName { get; init; }

    public override StartupKind Kind => StartupKind.Service;
    public override bool RequiresAdmin => true;

    internal void LoadState(int start) => IsEnabled = start == 2;

    public override void SetEnabled(bool enabled)
    {
        using (var key = Registry.LocalMachine.OpenSubKey($@"{ServicesKey}\{ServiceName}", writable: true)
                         ?? throw new InvalidOperationException(L.T("Startup.Error.ServiceMissing")))
        {
            key.SetValue("Start", enabled ? 2 : 3, RegistryValueKind.DWord);
        }

        using var tracking = Registry.CurrentUser.CreateSubKey(TrackingKey, writable: true);
        if (enabled) tracking.DeleteValue(ServiceName, throwOnMissingValue: false);
        else tracking.SetValue(ServiceName, DateTime.Now.ToString("s"));

        IsEnabled = enabled;
    }
}
