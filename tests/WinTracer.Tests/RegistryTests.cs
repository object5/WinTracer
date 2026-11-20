using System.Diagnostics;
using Microsoft.Win32;
using WinTracer.Core.RegistryTools;
using WinTracer.Core.Startup;
using WinTracer.Core.Tweaks;

namespace WinTracer.Tests;

public sealed class TestKey : IDisposable
{
    public string Path { get; } = @"Software\WinTracer.Tests." + Guid.NewGuid().ToString("N")[..8];

    public RegistryKey Create() => Registry.CurrentUser.CreateSubKey(Path, writable: true);

    public RegistryKey? Open() => Registry.CurrentUser.OpenSubKey(Path);

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(Path, throwOnMissingSubKey: false);
}

public class RegFileTests
{
    [Fact]
    public void BackupRestoresEverything()
    {
        using var key = new TestKey();
        using (var k = key.Create())
        {
            k.SetValue("str", "a\"b\\c");
            k.SetValue("dw", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
            k.SetValue("qw", 5L, RegistryValueKind.QWord);
            k.SetValue("exp", @"%TEMP%\x", RegistryValueKind.ExpandString);
            k.SetValue("multi", new[] { "one", "two" }, RegistryValueKind.MultiString);
            k.SetValue("bin", new byte[] { 1, 2, 255 }, RegistryValueKind.Binary);
            k.SetValue("", "default");
            using var child = k.CreateSubKey("child");
            child.SetValue("x", 1);
        }

        var builder = new RegFileBuilder();
        builder.AddKey(new RegKeyRef(RegistryHive.CurrentUser, RegistryView.Registry64, key.Path));
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wt_{Guid.NewGuid():N}.reg");
        builder.Save(file);
        Registry.CurrentUser.DeleteSubKeyTree(key.Path);

        try
        {
            using (var p = Process.Start(new ProcessStartInfo("reg.exe", $"import \"{file}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true })!)
                p.WaitForExit();

            using var r = key.Open()!;
            Assert.Equal("a\"b\\c", r.GetValue("str"));
            Assert.Equal(-1, r.GetValue("dw"));
            Assert.Equal(5L, r.GetValue("qw"));
            Assert.Equal(RegistryValueKind.ExpandString, r.GetValueKind("exp"));
            Assert.Equal(@"%TEMP%\x", r.GetValue("exp", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            Assert.Equal(new[] { "one", "two" }, (string[])r.GetValue("multi")!);
            Assert.Equal(new byte[] { 1, 2, 255 }, (byte[])r.GetValue("bin")!);
            Assert.Equal("default", r.GetValue(""));
            Assert.Equal(1, r.OpenSubKey("child")!.GetValue("x"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Wow64PathIsShown() =>
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\X",
            new RegKeyRef(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\X").DisplayPath);
}

public class RegistryScannerTests
{
    [Fact]
    public void MissingDetection()
    {
        using var tmp = new TempDir();
        var exists = tmp.File("a.exe");

        Assert.False(RegistryScanner.IsProvablyMissing(exists));
        Assert.True(RegistryScanner.IsProvablyMissing(System.IO.Path.Combine(tmp.Path, "gone.exe")));
        Assert.True(RegistryScanner.IsProvablyMissing(System.IO.Path.Combine(tmp.Path, "gone"), folder: true));
        Assert.False(RegistryScanner.IsProvablyMissing(@"\\server\share\x.exe"));
        Assert.False(RegistryScanner.IsProvablyMissing("relative.exe"));
        Assert.False(RegistryScanner.IsProvablyMissing("%WT_NO_SUCH_VAR%\\x.exe"));
        Assert.False(RegistryScanner.IsProvablyMissing(null));
    }

    [Fact]
    public void ScanRuns()
    {
        var result = RegistryScanner.Scan();
        Assert.All(result.Issues, i => Assert.False(string.IsNullOrEmpty(i.MissingTarget)));
    }
}

public class StartupTests
{
    [Fact]
    public void StartupApprovedRoundTrip()
    {
        var name = "WinTracer.Tests." + Guid.NewGuid().ToString("N")[..8];
        RegistryRunEntry Make() => new()
        {
            Name = name, Command = "x", SourceText = "t", Location = "t",
            Hive = RegistryHive.CurrentUser, View = RegistryView.Registry64,
        };

        try
        {
            var e = Make();
            e.LoadState();
            Assert.True(e.IsEnabled);

            e.SetEnabled(false);
            var check = Make();
            check.LoadState();
            Assert.False(check.IsEnabled);

            e.SetEnabled(true);
            check.LoadState();
            Assert.True(check.IsEnabled);
        }
        finally
        {
            StartupApproved.Remove(RegistryHive.CurrentUser, "Run", name);
        }
    }

    [Fact]
    public void ScanRuns()
    {
        var result = StartupScanner.Scan();
        Assert.All(result.Entries, e => Assert.False(string.IsNullOrEmpty(e.Name)));
    }
}

public class TweakTests
{
    [Fact]
    public void ApplyAndRevertRestoresOriginal()
    {
        using var key = new TestKey();
        using (var k = key.Create()) k.SetValue("A", 7);

        var tweak = new RegistryTweak
        {
            Id = "test." + Guid.NewGuid().ToString("N"), Group = TweakGroup.Interface,
            Settings =
            [
                new(RegistryHive.CurrentUser, key.Path, "A", 1),
                new(RegistryHive.CurrentUser, key.Path, "S", "x", RegistryValueKind.String),
                new(RegistryHive.CurrentUser, key.Path, "Big", 0xFFFFFFFFL),
            ],
        };

        Assert.Equal(TweakState.NotApplied, tweak.GetState());
        tweak.Apply();
        Assert.Equal(TweakState.Applied, tweak.GetState());
        tweak.Apply();
        tweak.Revert();

        using var r = key.Open()!;
        Assert.Equal(7, r.GetValue("A"));
        Assert.Null(r.GetValue("S"));
        Assert.Null(r.GetValue("Big"));
        Assert.Equal(TweakState.NotApplied, tweak.GetState());
    }

    [Fact]
    public void PartialState()
    {
        using var key = new TestKey();
        using (var k = key.Create()) k.SetValue("A", 1);

        var tweak = new RegistryTweak
        {
            Id = "test.partial", Group = TweakGroup.Interface,
            Settings = [new(RegistryHive.CurrentUser, key.Path, "A", 1), new(RegistryHive.CurrentUser, key.Path, "B", 1)],
        };

        Assert.Equal(TweakState.Partial, tweak.GetState());
    }

    [Fact]
    public void CatalogIdsUnique()
    {
        var all = TweakCatalog.Create();
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
    }
}
