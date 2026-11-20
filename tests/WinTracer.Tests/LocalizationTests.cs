using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WinTracer.Core.Cleanup;
using WinTracer.Core.RegistryTools;
using WinTracer.Core.Tweaks;
using WinTracer.Localization;

namespace WinTracer.Tests;

public class LocalizationTests
{
    private static readonly string Repo = FindRepo();

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WinTracer.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(Repo, "SpaceTracer", "Resources", file)).Root!
            .Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    [Fact]
    public void SameKeysInBothLanguages()
    {
        var en = Load("Strings.resx");
        var ru = Load("Strings.ru.resx");
        Assert.Empty(en.Keys.Except(ru.Keys));
        Assert.Empty(ru.Keys.Except(en.Keys));
    }

    [Fact]
    public void PlaceholdersMatch()
    {
        var en = Load("Strings.resx");
        var ru = Load("Strings.ru.resx");
        var holes = new Regex(@"\{(\d+)");
        foreach (var (key, value) in en)
        {
            var a = holes.Matches(value).Select(m => m.Groups[1].Value).Distinct().Order();
            var b = holes.Matches(ru[key]).Select(m => m.Groups[1].Value).Distinct().Order();
            Assert.True(a.SequenceEqual(b), $"{key}: placeholders differ");
        }
    }

    [Fact]
    public void EveryUsedKeyExists()
    {
        var keys = Load("Strings.resx").Keys.ToHashSet();
        var used = new Regex(@"L\.(?:T|F)\(""([^""]+)""|\{l:Loc ([^}\s]+)\}");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo, "SpaceTracer"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in used.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (!keys.Contains(key)) missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void CatalogsAreTranslated(string lang)
    {
        L.Use(CultureInfo.GetCultureInfo(lang));
        try
        {
            var texts = new List<string>();
            texts.AddRange(CleanupCatalog.Create().SelectMany(t => new[] { t.Title, t.Description }));
            texts.AddRange(TweakCatalog.Create().SelectMany(t => new[] { t.Title, t.Description }));
            texts.AddRange(BuiltInApps.Catalog.Select(a => a.Title));
            texts.AddRange(Enum.GetValues<RegistryIssueCategory>().SelectMany(c => new[] { RegistryScanner.CategoryTitle(c), RegistryScanner.CategoryDescription(c) }));
            texts.AddRange(Enum.GetValues<CleanupGroup>().Select(CleanupCatalog.GroupTitle));
            texts.AddRange(Enum.GetValues<TweakGroup>().Select(TweakCatalog.GroupTitle));

            Assert.DoesNotContain(texts, t => t.StartsWith('['));
            bool hasCyrillic = texts.Any(t => Regex.IsMatch(t, "[А-Яа-я]"));
            Assert.Equal(lang == "ru", hasCyrillic);
        }
        finally
        {
            L.Use(CultureInfo.GetCultureInfo("en"));
        }
    }
}
