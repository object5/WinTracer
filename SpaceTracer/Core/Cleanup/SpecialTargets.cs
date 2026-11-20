using System.Diagnostics;
using System.Runtime.InteropServices;
using WinTracer.Core.Native;
using WinTracer.Localization;

namespace WinTracer.Core.Cleanup;

public sealed class OldVersionsCleanupTarget : CleanupTarget
{
    public required string[] Folders { get; init; }
    public int Keep { get; init; } = 1;

    private static readonly System.Text.RegularExpressions.Regex VersionName =
        new(@"^v?\d+(\.\d+)+([-+][0-9A-Za-z.\-]+)?(\.exe)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public override bool IsPresent() => Folders.Any(f => PathExpander.Expand(f).Any(Directory.Exists));

    public override Task<CleanupEstimate> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        long bytes = 0;
        int files = 0;
        foreach (var path in Candidates())
        {
            ct.ThrowIfCancellationRequested();
            var (b, f) = Measure(path);
            bytes += b;
            files += f;
        }
        return new CleanupEstimate(bytes, files);
    }, ct);

    public override Task<CleanupResult> CleanAsync(CancellationToken ct) => Task.Run(() =>
    {
        long freed = 0;
        int deleted = 0, skipped = 0;
        foreach (var path in Candidates())
        {
            ct.ThrowIfCancellationRequested();
            var (b, f) = Measure(path);
            try
            {
                if (File.Exists(path))
                {
                    if (PathCleanupTarget.TryDeleteFile(path)) { freed += b; deleted++; } else skipped++;
                }
                else
                {
                    Directory.Delete(path, recursive: true);
                    freed += b;
                    deleted += f;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped += Math.Max(1, f);
            }
        }
        return new CleanupResult(freed, deleted, skipped);
    }, ct);

    private List<string> Candidates()
    {
        var inUse = RunningImagePaths();
        var result = new List<string>();

        foreach (var folder in Folders.SelectMany(PathExpander.Expand).Where(Directory.Exists))
        {
            var entries = new DirectoryInfo(folder).EnumerateFileSystemInfos()
                .Where(e => VersionName.IsMatch(e.Name) && (e.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(e => e.CreationTimeUtc > e.LastWriteTimeUtc ? e.CreationTimeUtc : e.LastWriteTimeUtc)
                .ToList();

            foreach (var entry in entries.Skip(Keep))
            {
                bool running = inUse.Any(exe => PathSafety.IsSameOrUnder(exe, entry.FullName));
                if (!running) result.Add(entry.FullName);
            }
        }
        return result;
    }

    private static (long Bytes, int Files) Measure(string path)
    {
        if (File.Exists(path)) return (new FileInfo(path).Length, 1);
        long bytes = 0;
        int files = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", options))
        {
            bytes += f.Length;
            files++;
        }
        return (bytes, files);
    }

    private static List<string> RunningImagePaths()
    {
        var list = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (NativeMethods.TryGetProcessImagePath(p.Id) is { } path) list.Add(path);
            }
        }
        return list;
    }
}

public sealed class RecycleBinCleanupTarget : CleanupTarget
{
    public override bool IsPresent() => true;

    public override Task<CleanupEstimate> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        return NativeMethods.SHQueryRecycleBin(null, ref info) == 0
            ? new CleanupEstimate(info.i64Size, (int)Math.Min(int.MaxValue, info.i64NumItems))
            : new CleanupEstimate(-1, 0, L.T("Cleanup.RecycleSizeUnknown"));
    }, ct);

    public override async Task<CleanupResult> CleanAsync(CancellationToken ct)
    {
        var before = await AnalyzeAsync(ct);
        await Task.Run(() => NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
            NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND), ct);
        var after = await AnalyzeAsync(ct);
        return new CleanupResult(Math.Max(0, before.Bytes - Math.Max(0, after.Bytes)), before.Files - after.Files, after.Files);
    }
}

public sealed class DismComponentCleanupTarget : CleanupTarget
{
    public override bool IsPresent() => File.Exists(DismPath);

    private static string DismPath => Path.Combine(Environment.SystemDirectory, "dism.exe");

    public override Task<CleanupEstimate> AnalyzeAsync(CancellationToken ct) =>
        Task.FromResult(new CleanupEstimate(-1, 0, L.T("Cleanup.SizeAfterClean")));

    public override async Task<CleanupResult> CleanAsync(CancellationToken ct)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        long freeBefore = drive.AvailableFreeSpace;

        using var process = Process.Start(new ProcessStartInfo(DismPath, "/Online /Cleanup-Image /StartComponentCleanup /English")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        await output;

        drive = new DriveInfo(drive.Name);
        long freed = Math.Max(0, drive.AvailableFreeSpace - freeBefore);
        return process.ExitCode == 0
            ? new CleanupResult(freed, 0, 0)
            : new CleanupResult(freed, 0, 0, L.F("Cleanup.DismExit", process.ExitCode));
    }
}
