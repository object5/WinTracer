namespace WinTracer.Core.Startup;

public static class CommandLine
{
    private static readonly string[] Extensions = [".exe", ".com", ".bat", ".cmd", ".lnk", ".dll", ".ps1", ".vbs", ".js"];

    public static string? GetExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string cmd = Environment.ExpandEnvironmentVariables(command.Trim());

        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            return Resolve(end > 1 ? cmd[1..end] : cmd.Trim('"'));
        }

        int pos = 0;
        while (true)
        {
            int space = cmd.IndexOf(' ', pos);
            string candidate = space < 0 ? cmd : cmd[..space];
            if (Resolve(candidate) is { } found && File.Exists(found)) return found;
            if (space < 0) break;
            pos = space + 1;
        }

        int firstSpace = cmd.IndexOf(' ');
        return Resolve(firstSpace < 0 ? cmd : cmd[..firstSpace]);
    }

    private static string? Resolve(string path)
    {
        path = path.Trim();
        if (path.Length == 0) return null;

        if (Path.IsPathFullyQualified(path))
        {
            if (File.Exists(path)) return path;
            foreach (var ext in Extensions)
                if (!Path.HasExtension(path) && File.Exists(path + ext)) return path + ext;
            return path;
        }

        var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var dir in dirs)
        {
            try
            {
                string full = Path.Combine(dir, path);
                if (File.Exists(full)) return full;
                if (!Path.HasExtension(path) && File.Exists(full + ".exe")) return full + ".exe";
            }
            catch (ArgumentException) { }
        }
        return path;
    }
}
