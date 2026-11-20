namespace WinTracer.Core.Startup;

public sealed class ScheduledTaskEntry : StartupEntry
{
    public required string TaskPath { get; init; }

    public override StartupKind Kind => StartupKind.ScheduledTask;

    public override bool RequiresAdmin => false;

    internal void LoadState(bool enabled) => IsEnabled = enabled;

    public override void SetEnabled(bool enabled)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new PlatformNotSupportedException();
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        dynamic task = service.GetFolder(Path.GetDirectoryName(TaskPath) ?? @"\").GetTask(Path.GetFileName(TaskPath));
        task.Enabled = enabled;
        IsEnabled = enabled;
    }
}
