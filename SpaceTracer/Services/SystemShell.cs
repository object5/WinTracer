using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace WinTracer.Services;

public static class SystemShell
{
    public static bool IsElevated { get; } = CheckElevated();

    private static bool CheckElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool RestartElevated()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception)
        {
            return false;
        }

        Application.Current.Shutdown();
        return true;
    }

    public static void RestartExplorer()
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            using (p)
            {
                if (p.SessionId != session) continue;
                try { p.Kill(); p.WaitForExit(3000); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            }
        }

        Thread.Sleep(1500);
        if (Process.GetProcessesByName("explorer").All(p => p.SessionId != session))
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    public static void ShowInExplorer(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    public static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
