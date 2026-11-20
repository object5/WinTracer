using System.IO.Enumeration;

namespace WinTracer.Core.Cleanup;

public static class PathExpander
{
    public static IEnumerable<string> Expand(string pattern)
    {
        string p = pattern;
        if (p.StartsWith('~'))
            p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p[1..];
        p = Environment.ExpandEnvironmentVariables(p);

        if (p.Contains('%') || !Path.IsPathFullyQualified(p))
            yield break;

        var segments = p.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = new List<string> { segments[0] + Path.DirectorySeparatorChar };

        for (int i = 1; i < segments.Length && current.Count > 0; i++)
        {
            string seg = segments[i];
            bool last = i == segments.Length - 1;
            var next = new List<string>();

            foreach (var dir in current)
            {
                if (seg.Contains('*') || seg.Contains('?'))
                {
                    if (!Directory.Exists(dir)) continue;
                    try
                    {
                        foreach (var d in Directory.EnumerateDirectories(dir, seg))
                        {
                            if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0)
                                next.Add(d);
                        }
                        if (last)
                            next.AddRange(Directory.EnumerateFiles(dir, seg));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                else
                {
                    next.Add(Path.Combine(dir, seg));
                }
            }
            current = next;
        }

        foreach (var path in current)
        {
            if ((Directory.Exists(path) || File.Exists(path)) && !PathSafety.IsCriticalFolder(path))
                yield return path;
        }
    }

    public static bool Matches(string pattern, ReadOnlySpan<char> name) =>
        pattern == "*" || FileSystemName.MatchesSimpleExpression(pattern, name);
}
