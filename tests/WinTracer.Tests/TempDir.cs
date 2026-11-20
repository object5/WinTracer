using System.Diagnostics;

namespace WinTracer.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wt_" + Guid.NewGuid().ToString("N")[..8]);

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string relative, int size = 100, int ageDays = 0)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, new byte[size]);
        if (ageDays > 0)
        {
            var t = DateTime.Now.AddDays(-ageDays);
            System.IO.File.SetCreationTime(full, t);
            System.IO.File.SetLastWriteTime(full, t);
        }
        return full;
    }

    public string Dir(string relative)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public static void Junction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        p.WaitForExit();
    }

    public void Dispose()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories)
                         .Where(d => (new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0).ToList())
                Directory.Delete(d);
            Directory.Delete(Path, true);
        }
        catch (IOException) { }
    }
}
