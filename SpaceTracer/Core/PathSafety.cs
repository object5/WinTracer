using WinTracer.Localization;
namespace WinTracer.Core;

public enum PathRisk
{
    None,
    Warning,
    Blocked,
}

public readonly record struct PathVerdict(PathRisk Risk, string? Reason);

public static class PathSafety
{
    private static readonly string Windows = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
    private static readonly string UserProfile = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private static readonly string[] ProgramFolders = Distinct(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetEnvironmentVariable("ProgramW6432"));

    private static readonly HashSet<string> BlockedExact = new(StringComparer.OrdinalIgnoreCase)
    {
        UserProfile,
        Normalize(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
        Normalize(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
        Normalize(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        Normalize(Path.Combine(UserProfile, "AppData")),
        Normalize(Path.Combine(UserProfile, "AppData", "LocalLow")),
        Normalize(Path.GetDirectoryName(UserProfile) ?? UserProfile),
    };

    private static readonly HashSet<string> BlockedRootNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$Recycle.Bin", "Recovery", "Boot", "EFI", "Documents and Settings",
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "BOOTNXT", "DumpStack.log.tmp",
    };

    static PathSafety()
    {
        foreach (var pf in ProgramFolders) BlockedExact.Add(pf);
    }

    public static PathVerdict Check(string path)
    {
        string p = Normalize(path);
        string? root = Path.GetPathRoot(p);

        if (string.IsNullOrEmpty(root) || string.Equals(p, Normalize(root), StringComparison.OrdinalIgnoreCase))
            return new(PathRisk.Blocked, L.T("Safety.DriveRoot"));

        if (IsSameOrUnder(p, Windows))
            return new(PathRisk.Blocked, L.T("Safety.WindowsFolder"));

        if (BlockedExact.Contains(p))
            return new(PathRisk.Blocked, L.T("Safety.SystemFolder"));

        if (string.Equals(Path.GetDirectoryName(p), Normalize(root), StringComparison.OrdinalIgnoreCase) &&
            BlockedRootNames.Contains(Path.GetFileName(p)))
            return new(PathRisk.Blocked, L.T("Safety.SystemObject"));

        foreach (var pf in ProgramFolders)
        {
            if (IsSameOrUnder(p, pf))
                return new(PathRisk.Warning,
                    L.T("Safety.ProgramFolder"));
        }

        return new(PathRisk.None, null);
    }

    public static bool IsCriticalFolder(string path)
    {
        string p = Normalize(path);
        string? root = Path.GetPathRoot(p);
        return string.IsNullOrEmpty(root)
            || string.Equals(p, Normalize(root), StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, Windows, StringComparison.OrdinalIgnoreCase)
            || BlockedExact.Contains(p);
    }

    public static bool IsSameOrUnder(string path, string folder)
    {
        string p = Normalize(path), f = Normalize(folder);
        return p.Equals(f, StringComparison.OrdinalIgnoreCase) ||
               p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0) ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
    }

    private static string[] Distinct(params string?[] paths) =>
        paths.Where(p => !string.IsNullOrEmpty(p)).Select(p => Normalize(p!)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
