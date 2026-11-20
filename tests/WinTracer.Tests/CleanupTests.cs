using WinTracer.Core.Cleanup;

namespace WinTracer.Tests;

public class CleanupTests
{
    private static PathCleanupTarget Target(params PathRule[] rules) => new()
    {
        Id = "t", Group = CleanupGroup.System, Rules = rules,
    };

    [Fact]
    public async Task DeletesOnlyOldFilesAndKeepsExcluded()
    {
        using var tmp = new TempDir();
        var cache = tmp.Dir("cache");
        tmp.File(@"cache\old.bin", 1000, ageDays: 90);
        tmp.File(@"cache\sub\old2.bin", 1000, ageDays: 90);
        var memory = tmp.File(@"cache\proj\memory\MEMORY.md", 10, ageDays: 90);
        var fresh = tmp.File(@"cache\fresh.bin", 10);
        File.SetAttributes(Path.Combine(cache, "old.bin"), FileAttributes.ReadOnly);

        var target = Target(new PathRule(cache) { MinAge = TimeSpan.FromDays(30), ExcludeDirectories = ["memory"] });

        var estimate = await target.AnalyzeAsync(default);
        Assert.Equal(2, estimate.Files);
        Assert.Equal(2000, estimate.Bytes);

        var result = await target.CleanAsync(default);
        Assert.Equal(2, result.DeletedFiles);
        Assert.True(File.Exists(memory));
        Assert.True(File.Exists(fresh));
        Assert.True(Directory.Exists(cache));
    }

    [Fact]
    public async Task NeverFollowsJunctions()
    {
        using var tmp = new TempDir();
        using var outside = new TempDir();
        var canary = outside.File("canary.txt", 10, ageDays: 90);
        var cache = tmp.Dir("cache");
        TempDir.Junction(Path.Combine(cache, "link"), outside.Path);

        await Target(new PathRule(cache)).CleanAsync(default);

        Assert.True(File.Exists(canary));
        Assert.True(Directory.Exists(Path.Combine(cache, "link")));
    }

    [Fact]
    public async Task Pattern()
    {
        using var tmp = new TempDir();
        tmp.File("a.dmp");
        var keep = tmp.File("a.txt");

        var result = await Target(new PathRule(tmp.Path) { Pattern = "*.dmp" }).CleanAsync(default);

        Assert.Equal(1, result.DeletedFiles);
        Assert.True(File.Exists(keep));
    }

    [Theory]
    [InlineData(@"%WT_NO_SUCH_VAR%\x")]
    [InlineData(@"relative\path")]
    [InlineData(@"C:\")]
    [InlineData("%USERPROFILE%")]
    [InlineData("%LOCALAPPDATA%")]
    [InlineData("%APPDATA%")]
    [InlineData("%WINDIR%")]
    public void UnsafeRootsExpandToNothing(string pattern) => Assert.Empty(PathExpander.Expand(pattern));

    [Fact]
    public void ExpandsWildcards()
    {
        using var tmp = new TempDir();
        tmp.Dir(@"Packages\App_123\Cache");
        tmp.Dir(@"Packages\App_456\Cache");
        tmp.Dir(@"Packages\Other\Cache");

        var found = PathExpander.Expand(Path.Combine(tmp.Path, @"Packages\App_*\Cache")).ToList();

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task OldVersionsKeepsNewestAndIgnoresOtherNames()
    {
        using var tmp = new TempDir();
        foreach (var v in new[] { "1.0.0", "1.1.0", "1.2.0" })
        {
            tmp.File($@"versions\{v}\app.exe");
            Thread.Sleep(30);
        }
        tmp.File(@"versions\9e1da79431da526b\codex.exe");
        tmp.File(@"versions\.sdk-version");

        var target = new OldVersionsCleanupTarget
        {
            Id = "v", Group = CleanupGroup.AiTools,
            Folders = [Path.Combine(tmp.Path, "versions")], Keep = 1,
        };
        await target.CleanAsync(default);

        var left = Directory.EnumerateFileSystemEntries(Path.Combine(tmp.Path, "versions")).Select(Path.GetFileName).ToHashSet();
        Assert.Equal(new HashSet<string?> { "1.2.0", "9e1da79431da526b", ".sdk-version" }, left);
    }

    [Fact]
    public void CatalogIsSane()
    {
        var all = CleanupCatalog.Create();
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        foreach (var t in all.OfType<PathCleanupTarget>())
            Assert.NotEmpty(t.Rules);
    }
}
