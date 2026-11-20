using System.IO.Enumeration;

namespace WinTracer.Core.Cleanup;

public sealed record PathRule(string Path)
{
    public string Pattern { get; init; } = "*";

    public bool Recursive { get; init; } = true;

    public TimeSpan MinAge { get; init; } = TimeSpan.Zero;

    public string[] ExcludeDirectories { get; init; } = [];

    public bool RemoveEmptyDirectories { get; init; } = true;
}

public sealed class PathCleanupTarget : CleanupTarget
{
    public required PathRule[] Rules { get; init; }

    public override bool IsPresent() => Rules.Any(r => PathExpander.Expand(r.Path).Any());

    public override Task<CleanupEstimate> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        long bytes = 0;
        int files = 0;
        foreach (var (_, size) in EnumerateCandidates(ct))
        {
            bytes += size;
            files++;
        }
        return new CleanupEstimate(bytes, files);
    }, ct);

    public override Task<CleanupResult> CleanAsync(CancellationToken ct) => Task.Run(() =>
    {
        long freed = 0;
        int deleted = 0, skipped = 0;

        foreach (var (path, size) in EnumerateCandidates(ct).ToList())
        {
            ct.ThrowIfCancellationRequested();
            if (TryDeleteFile(path)) { freed += size; deleted++; }
            else skipped++;
        }

        foreach (var rule in Rules.Where(r => r.RemoveEmptyDirectories && r.Recursive))
        {
            foreach (var root in PathExpander.Expand(rule.Path).Where(Directory.Exists))
                RemoveEmptyDirectories(root, rule, isRoot: true);
        }

        return new CleanupResult(freed, deleted, skipped);
    }, ct);

    private IEnumerable<(string Path, long Size)> EnumerateCandidates(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in Rules)
        {
            DateTime cutoff = now - rule.MinAge;
            foreach (var root in PathExpander.Expand(rule.Path))
            {
                ct.ThrowIfCancellationRequested();

                if (File.Exists(root))
                {
                    var fi = new FileInfo(root);
                    if ((fi.Attributes & FileAttributes.ReparsePoint) == 0 &&
                        Newest(fi.CreationTimeUtc, fi.LastWriteTimeUtc) <= cutoff && seen.Add(root))
                        yield return (root, fi.Length);
                    continue;
                }

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = rule.Recursive,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    ReturnSpecialDirectories = false,
                };
                var excluded = rule.ExcludeDirectories;
                string pattern = rule.Pattern;

                var entries = new FileSystemEnumerable<(string, long)>(
                    root,
                    (ref FileSystemEntry e) => (e.ToFullPath(), e.Length),
                    options)
                {
                    ShouldIncludePredicate = (ref FileSystemEntry e) =>
                        !e.IsDirectory &&
                        PathExpander.Matches(pattern, e.FileName) &&
                        Newest(e.CreationTimeUtc.UtcDateTime, e.LastWriteTimeUtc.UtcDateTime) <= cutoff,
                    ShouldRecursePredicate = (ref FileSystemEntry e) =>
                        excluded.Length == 0 || !excluded.Contains(e.FileName.ToString(), StringComparer.OrdinalIgnoreCase),
                };

                foreach (var item in entries)
                {
                    if (seen.Add(item.Item1)) yield return item;
                }
            }
        }
    }

    private static DateTime Newest(DateTime a, DateTime b) => a > b ? a : b;

    internal static bool TryDeleteFile(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReparsePoint) != 0) return false;
            if ((attrs & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
                File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void RemoveEmptyDirectories(string dir, PathRule rule, bool isRoot)
    {
        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(dir).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }

        foreach (var sub in subdirs)
        {
            try
            {
                var info = new DirectoryInfo(sub);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (rule.ExcludeDirectories.Contains(info.Name, StringComparer.OrdinalIgnoreCase)) continue;
                RemoveEmptyDirectories(sub, rule, isRoot: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        if (isRoot) return;
        try
        {
            var info = new DirectoryInfo(dir);
            if (DateTime.UtcNow - info.CreationTimeUtc < rule.MinAge) return;
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
