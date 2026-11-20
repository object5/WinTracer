using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinTracer.Localization;

namespace WinTracer.Core.Startup;

public static partial class StartupScanner
{
    private const int TASK_TRIGGER_BOOT = 8;
    private const int TASK_TRIGGER_LOGON = 9;
    private const int TASK_ACTION_EXEC = 0;
    private const int TASK_ENUM_HIDDEN = 1;

    private static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public sealed record ScanResult(IReadOnlyList<StartupEntry> Entries, IReadOnlyList<string> Errors);

    public static ScanResult Scan()
    {
        var entries = new List<StartupEntry>();
        var errors = new List<string>();

        void Collect(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { errors.Add($"{what}: {ex.GetBaseException().Message}"); }
        }

        Collect(L.T("Startup.Section.Registry"), () => ReadRunKeys(entries));
        Collect(L.T("Startup.Section.Folders"), () => ReadStartupFolders(entries));
        Collect(L.T("Startup.Section.Tasks"), () => ReadScheduledTasks(entries));
        Collect(L.T("Startup.Section.Services"), () => ReadServices(entries));

        return new ScanResult(entries, errors);
    }

    private static void ReadRunKeys(List<StartupEntry> entries)
    {
        ReadRunKey(entries, RegistryHive.CurrentUser, RegistryView.Registry64, "Run", L.T("Startup.Source.RunUser"));
        ReadRunKey(entries, RegistryHive.LocalMachine, RegistryView.Registry64, "Run", L.T("Startup.Source.RunMachine"));
        ReadRunKey(entries, RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", L.T("Startup.Source.RunMachine32"));
    }

    private static void ReadRunKey(List<StartupEntry> entries, RegistryHive hive, RegistryView view, string approved, string source)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var key = root.OpenSubKey(RegistryRunEntry.RunKey);
        if (key is null) return;

        string hiveText = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
        string keyText = view == RegistryView.Registry32
            ? $@"{hiveText}\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            : $@"{hiveText}\{RegistryRunEntry.RunKey}";

        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (key.GetValue(name) is not string command || string.IsNullOrWhiteSpace(command)) continue;

            var entry = new RegistryRunEntry
            {
                Name = name,
                Command = command,
                SourceText = source,
                Location = keyText,
                Hive = hive,
                View = view,
                ApprovedSubKey = approved,
            };
            entry.LoadState();
            entry.ResolveFile();
            entries.Add(entry);
        }
    }

    private static void ReadStartupFolders(List<StartupEntry> entries)
    {
        ReadStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            RegistryHive.CurrentUser, L.T("Startup.Source.FolderUser"));
        ReadStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            RegistryHive.LocalMachine, L.T("Startup.Source.FolderAll"));
    }

    private static void ReadStartupFolder(List<StartupEntry> entries, string folder, RegistryHive hive, string source)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            string fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

            string? target = file;
            string command = file;
            if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && ResolveShortcut(file) is { } lnk)
            {
                target = lnk.Target;
                command = string.IsNullOrEmpty(lnk.Arguments) ? $"\"{lnk.Target}\"" : $"\"{lnk.Target}\" {lnk.Arguments}";
            }

            var entry = new StartupFolderEntry
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Command = command,
                SourceText = source,
                Location = file,
                Hive = hive,
                FilePath = file,
            };
            entry.LoadState();
            entry.ResolveFile(string.IsNullOrEmpty(target) ? null : target);
            entries.Add(entry);
        }
    }

    private static (string Target, string Arguments)? ResolveShortcut(string lnkPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                string target = shortcut.TargetPath;
                string args = shortcut.Arguments;
                Marshal.FinalReleaseComObject(shortcut);
                return string.IsNullOrEmpty(target) ? null : (target, args);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static void ReadScheduledTasks(List<StartupEntry> entries)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        WalkTaskFolder(service.GetFolder(@"\"), entries);
    }

    private static void WalkTaskFolder(dynamic folder, List<StartupEntry> entries)
    {
        string folderPath = folder.Path;
        if (folderPath.StartsWith(@"\Microsoft", StringComparison.OrdinalIgnoreCase)) return;

        dynamic tasks = folder.GetTasks(TASK_ENUM_HIDDEN);
        int count = tasks.Count;
        for (int i = 1; i <= count; i++)
        {
            try { AddTask(tasks[i], entries); }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { }
        }

        dynamic subFolders = folder.GetFolders(0);
        int folderCount = subFolders.Count;
        for (int i = 1; i <= folderCount; i++)
        {
            try { WalkTaskFolder(subFolders[i], entries); }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { }
        }
    }

    private static void AddTask(dynamic task, List<StartupEntry> entries)
    {
        dynamic definition = task.Definition;

        string? when = null;
        dynamic triggers = definition.Triggers;
        int triggerCount = triggers.Count;
        for (int t = 1; t <= triggerCount; t++)
        {
            int type = triggers[t].Type;
            if (type == TASK_TRIGGER_LOGON) { when = L.T("Startup.Source.AtLogon"); break; }
            if (type == TASK_TRIGGER_BOOT) when ??= L.T("Startup.Source.AtBoot");
        }
        if (when is null) return;

        string? exe = null, args = null;
        dynamic actions = definition.Actions;
        int actionCount = actions.Count;
        for (int a = 1; a <= actionCount; a++)
        {
            dynamic action = actions[a];
            if ((int)action.Type != TASK_ACTION_EXEC) continue;
            exe = action.Path;
            args = action.Arguments;
            break;
        }
        if (string.IsNullOrWhiteSpace(exe)) return;

        string path = task.Path;
        string expandedExe = Environment.ExpandEnvironmentVariables(exe.Trim('"'));
        var entry = new ScheduledTaskEntry
        {
            Name = task.Name,
            Command = string.IsNullOrEmpty(args) ? $"\"{exe.Trim('"')}\"" : $"\"{exe.Trim('"')}\" {args}",
            SourceText = L.F("Startup.Source.Task", when),
            Location = path,
            TaskPath = path,
        };
        entry.LoadState((bool)task.Enabled);
        entry.ResolveFile(CommandLine.GetExecutable($"\"{expandedExe}\""));
        entries.Add(entry);
    }

    private static void ReadServices(List<StartupEntry> entries)
    {
        using var services = Registry.LocalMachine.OpenSubKey(ServiceEntry.ServicesKey);
        if (services is null) return;

        using var tracking = Registry.CurrentUser.OpenSubKey(ServiceEntry.TrackingKey);
        var switchedByUs = new HashSet<string>(tracking?.GetValueNames() ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var name in services.GetSubKeyNames())
        {
            using var key = services.OpenSubKey(name);
            if (key is null) continue;

            if (key.GetValue("Type") is not int type || (type & 0x30) == 0) continue;
            if (key.GetValue("Start") is not int start) continue;
            if (start != 2 && !(start == 3 && switchedByUs.Contains(name))) continue;

            if (key.GetValue("ImagePath") is not string imagePath) continue;
            string? exe = CommandLine.GetExecutable(imagePath);
            if (exe is null || PathSafety.IsSameOrUnder(exe, WindowsDir)) continue;
            if (exe.Contains(@"\Windows Defender\", StringComparison.OrdinalIgnoreCase)) continue;

            string display = LoadIndirect(key.GetValue("DisplayName") as string) ?? name;
            var entry = new ServiceEntry
            {
                Name = display,
                Command = imagePath,
                SourceText = key.GetValue("DelayedAutostart") is int d && d == 1 ? L.T("Startup.Source.ServiceDelayed") : L.T("Startup.Source.ServiceAuto"),
                Location = name,
                ServiceName = name,
            };
            entry.LoadState(start);
            entry.ResolveFile(exe);
            entries.Add(entry);
        }
    }

    [LibraryImport("shlwapi.dll", EntryPoint = "SHLoadIndirectString", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHLoadIndirectString(string pszSource, [Out] char[] pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

    private static string? LoadIndirect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith('@')) return value;
        var buffer = new char[512];
        if (SHLoadIndirectString(value, buffer, buffer.Length, IntPtr.Zero) != 0) return null;
        int len = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, len < 0 ? buffer.Length : len);
    }
}
