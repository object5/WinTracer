using WinTracer.Localization;
using System.Diagnostics;

namespace WinTracer.Core.Cleanup;

public enum CleanupGroup
{
    System,
    Browsers,
    Applications,
    AiTools,
    Development,
}

public sealed record CleanupEstimate(long Bytes, int Files, string? Note = null)
{
    public static readonly CleanupEstimate Empty = new(0, 0);
}

public sealed record CleanupResult(long FreedBytes, int DeletedFiles, int SkippedFiles, string? Note = null);

public abstract class CleanupTarget
{
    public required string Id { get; init; }
    public string Title => L.T($"Cleanup.{Id}.Title");
    public string Description => L.T($"Cleanup.{Id}.Description");
    public required CleanupGroup Group { get; init; }

    public string? Product { get; init; }

    public bool SelectedByDefault { get; init; } = true;
    public bool RequiresAdmin { get; init; }

    public string? Warning => L.TryT($"Cleanup.{Id}.Warning");

    public string[] ProcessNames { get; init; } = [];

    public abstract bool IsPresent();

    public abstract Task<CleanupEstimate> AnalyzeAsync(CancellationToken ct);

    public abstract Task<CleanupResult> CleanAsync(CancellationToken ct);

    public string? GetRunningProcesses()
    {
        var running = new List<string>();
        foreach (var name in ProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0) running.Add(name);
            foreach (var p in procs) p.Dispose();
        }
        return running.Count == 0 ? null : string.Join(", ", running.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
