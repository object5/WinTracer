using WinTracer.Core;
using WinTracer.Core.Scanning;
using WinTracer.Core.Startup;
using WinTracer.Localization;

namespace WinTracer.Tests;

public class ByteSizeTests
{
    [Fact]
    public void Small()
    {
        Assert.Equal("0 " + L.T("Unit.B"), ByteSize.Format(0));
        Assert.Equal("512 " + L.T("Unit.B"), ByteSize.Format(512));
        Assert.Equal("—", ByteSize.Format(-1));
    }

    [Fact]
    public void Units()
    {
        Assert.EndsWith(L.T("Unit.KB"), ByteSize.Format(2048));
        Assert.EndsWith(L.T("Unit.MB"), ByteSize.Format(5L * 1024 * 1024));
        Assert.EndsWith(L.T("Unit.GB"), ByteSize.Format(3L * 1024 * 1024 * 1024));
    }
}

public class CommandLineTests
{
    [Fact]
    public void Quoted()
    {
        var exe = CommandLine.GetExecutable("\"%windir%\\system32\\notepad.exe\" a b");
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "notepad.exe"), exe, ignoreCase: true);
    }

    [Fact]
    public void BareName() =>
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
            CommandLine.GetExecutable("rundll32.exe shell32.dll,Control_RunDLL"), ignoreCase: true);

    [Fact]
    public void UnquotedWithSpaces()
    {
        using var tmp = new TempDir();
        var exe = tmp.File(@"My App\app.exe");
        Assert.Equal(exe, CommandLine.GetExecutable($"{exe} --minimized"), ignoreCase: true);
    }

    [Fact]
    public void Empty() => Assert.Null(CommandLine.GetExecutable("  "));
}

public class PathSafetyTests
{
    [Fact]
    public void BlocksSystemPlaces()
    {
        Assert.Equal(PathRisk.Blocked, PathSafety.Check(@"C:\").Risk);
        Assert.Equal(PathRisk.Blocked, PathSafety.Check(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).Risk);
        Assert.Equal(PathRisk.Blocked, PathSafety.Check(Path.Combine(Environment.SystemDirectory, "drivers")).Risk);
        Assert.Equal(PathRisk.Blocked, PathSafety.Check(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Risk);
        Assert.Equal(PathRisk.Blocked, PathSafety.Check(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).Risk);
    }

    [Fact]
    public void WarnsInsideProgramFiles() =>
        Assert.Equal(PathRisk.Warning,
            PathSafety.Check(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SomeApp")).Risk);

    [Fact]
    public void AllowsTemp()
    {
        using var tmp = new TempDir();
        Assert.Equal(PathRisk.None, PathSafety.Check(tmp.Path).Risk);
    }

    [Fact]
    public void SameOrUnder()
    {
        Assert.True(PathSafety.IsSameOrUnder(@"C:\a\b", @"C:\a"));
        Assert.True(PathSafety.IsSameOrUnder(@"C:\A", @"c:\a\"));
        Assert.False(PathSafety.IsSameOrUnder(@"C:\ab", @"C:\a"));
    }
}

public class DiskScannerTests
{
    [Fact]
    public async Task SumsSizesAndSkipsJunctions()
    {
        using var tmp = new TempDir();
        using var outside = new TempDir();
        tmp.File(@"a\one.bin", 1000);
        tmp.File(@"a\b\two.bin", 3000);
        tmp.File("root.bin", 500);
        outside.File("big.bin", 50_000);
        TempDir.Junction(Path.Combine(tmp.Path, "link"), outside.Path);

        var progress = new ScanProgress();
        var root = await new DiskScanner().ScanAsync(tmp.Path, progress, default);

        Assert.Equal(4500, root.Size);
        Assert.Equal(3, root.FileCount);
        Assert.Equal("a", root.Children![0].Name);
        Assert.Contains(root.Children, c => c.Name == "link" && c.IsReparsePoint && c.Size == 0);
    }

    [Fact]
    public async Task DetachUpdatesParents()
    {
        using var tmp = new TempDir();
        tmp.File(@"a\one.bin", 1000);
        tmp.File(@"a\b\two.bin", 3000);

        var root = await new DiskScanner().ScanAsync(tmp.Path, new ScanProgress(), default);
        var a = root.Children!.Single(c => c.Name == "a");
        var b = a.Children!.Single(c => c.Name == "b");

        b.DetachFromParent();

        Assert.Equal(1000, root.Size);
        Assert.Equal(1, root.FileCount);
        Assert.False(b.IsAttached);
        Assert.True(a.IsAttached);
    }

    [Fact]
    public async Task Cancels()
    {
        using var tmp = new TempDir();
        tmp.File("x.bin");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DiskScanner().ScanAsync(tmp.Path, new ScanProgress(), cts.Token));
    }
}
