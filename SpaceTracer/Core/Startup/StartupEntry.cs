using System.Diagnostics;

namespace WinTracer.Core.Startup;

public enum StartupKind
{
    Registry,
    StartupFolder,
    ScheduledTask,
    Service,
}

public abstract class StartupEntry
{
    public required string Name { get; init; }

    public required string Command { get; init; }

    public required string SourceText { get; init; }

    public required string Location { get; init; }

    public abstract StartupKind Kind { get; }

    public bool IsEnabled { get; protected set; }

    public abstract bool RequiresAdmin { get; }

    public virtual bool CanDelete => false;

    public string? ExecutablePath { get; private set; }
    public bool FileExists { get; private set; }
    public string? Publisher { get; private set; }
    public string? Description { get; private set; }

    public abstract void SetEnabled(bool enabled);

    public virtual void Delete(string backupFolder) => throw new NotSupportedException();

    internal void ResolveFile(string? explicitPath = null)
    {
        ExecutablePath = explicitPath ?? CommandLine.GetExecutable(Command);
        FileExists = ExecutablePath is not null && File.Exists(ExecutablePath);
        if (!FileExists) return;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(ExecutablePath!);
            Publisher = string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim();
            Description = string.IsNullOrWhiteSpace(info.FileDescription) ? null : info.FileDescription.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
