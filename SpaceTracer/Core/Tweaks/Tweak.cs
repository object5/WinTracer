using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using WinTracer.Localization;

namespace WinTracer.Core.Tweaks;

public enum TweakGroup
{
    Privacy,
    Interface,
    Performance,
    Updates,
}

public enum TweakState
{
    NotApplied,
    Partial,
    Applied,
    Unknown,
}

public enum TweakEffect
{
    Immediate,
    RestartExplorer,
    SignOut,
    Reboot,
}

public abstract class Tweak
{
    public required string Id { get; init; }
    public string Title => L.T($"Tweak.{Id}.Title");
    public string Description => L.T($"Tweak.{Id}.Description");
    public required TweakGroup Group { get; init; }
    public bool RequiresAdmin { get; init; }
    public TweakEffect Effect { get; init; } = TweakEffect.Immediate;
    public string? Warning => L.TryT($"Tweak.{Id}.Warning");

    public abstract TweakState GetState();
    public abstract void Apply();
    public abstract void Revert();
}

public sealed record RegSetting(RegistryHive Hive, string Path, string Name, object Value, RegistryValueKind Kind = RegistryValueKind.DWord)
{
    public object? Default { get; init; }

    internal bool Matches(object? current) => current is not null && Kind switch
    {
        RegistryValueKind.DWord => current is int i && i == unchecked((int)Convert.ToInt64(Value)),
        RegistryValueKind.QWord => current is long l && l == Convert.ToInt64(Value),
        _ => string.Equals(current.ToString(), Value.ToString(), StringComparison.Ordinal),
    };
}

public sealed class RegistryTweak : Tweak
{
    public required RegSetting[] Settings { get; init; }

    public Action? AfterChange { get; init; }

    public override TweakState GetState()
    {
        int matched = 0;
        foreach (var s in Settings)
            if (s.Matches(Read(s))) matched++;
        return matched == 0 ? TweakState.NotApplied : matched == Settings.Length ? TweakState.Applied : TweakState.Partial;
    }

    public override void Apply()
    {
        TweakBackup.CaptureIfMissing(Id, Settings);
        foreach (var s in Settings)
        {
            using var root = RegistryKey.OpenBaseKey(s.Hive, RegistryView.Registry64);
            using var key = root.CreateSubKey(s.Path, writable: true);
            key.SetValue(s.Name, ToRegistry(s.Value, s.Kind), s.Kind);
        }
        AfterChange?.Invoke();
    }

    public override void Revert()
    {
        if (!TweakBackup.Restore(Id))
        {
            foreach (var s in Settings)
            {
                using var root = RegistryKey.OpenBaseKey(s.Hive, RegistryView.Registry64);
                if (s.Default is null)
                {
                    using var key = root.OpenSubKey(s.Path, writable: true);
                    key?.DeleteValue(s.Name, throwOnMissingValue: false);
                }
                else
                {
                    using var key = root.CreateSubKey(s.Path, writable: true);
                    key.SetValue(s.Name, ToRegistry(s.Default, s.Kind), s.Kind);
                }
            }
        }
        AfterChange?.Invoke();
    }

    private static object? Read(RegSetting s)
    {
        using var root = RegistryKey.OpenBaseKey(s.Hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(s.Path);
        return key?.GetValue(s.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    internal static object ToRegistry(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => unchecked((int)Convert.ToInt64(value)),
        RegistryValueKind.QWord => Convert.ToInt64(value),
        _ => value,
    };
}

public sealed class CommandTweak : Tweak
{
    public required Func<TweakState> Detect { get; init; }
    public required Action ApplyAction { get; init; }
    public required Action RevertAction { get; init; }

    public override TweakState GetState()
    {
        try { return Detect(); }
        catch { return TweakState.Unknown; }
    }

    public override void Apply() => ApplyAction();
    public override void Revert() => RevertAction();

    public static string Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException((error.Length > 0 ? error : output).Trim());
        return output;
    }
}

internal static class TweakBackup
{
    private sealed record Saved(RegistryHive Hive, string Path, string Name, bool Existed, RegistryValueKind Kind, string? Data);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTracer", "tweaks-backup.json");

    private static readonly object Gate = new();

    public static void CaptureIfMissing(string id, IEnumerable<RegSetting> settings)
    {
        lock (Gate)
        {
            var all = Load();
            if (all.ContainsKey(id)) return;

            var saved = new List<Saved>();
            foreach (var s in settings)
            {
                using var root = RegistryKey.OpenBaseKey(s.Hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(s.Path);
                var value = key?.GetValue(s.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value is null)
                {
                    saved.Add(new Saved(s.Hive, s.Path, s.Name, false, s.Kind, null));
                    continue;
                }
                var kind = key!.GetValueKind(s.Name);
                string data = value switch
                {
                    byte[] bytes => Convert.ToBase64String(bytes),
                    string[] multi => JsonSerializer.Serialize(multi),
                    _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
                };
                saved.Add(new Saved(s.Hive, s.Path, s.Name, true, kind, data));
            }
            all[id] = saved;
            Save(all);
        }
    }

    public static bool Restore(string id)
    {
        lock (Gate)
        {
            var all = Load();
            if (!all.TryGetValue(id, out var saved)) return false;

            foreach (var s in saved)
            {
                using var root = RegistryKey.OpenBaseKey(s.Hive, RegistryView.Registry64);
                if (!s.Existed)
                {
                    using var key = root.OpenSubKey(s.Path, writable: true);
                    key?.DeleteValue(s.Name, throwOnMissingValue: false);
                    continue;
                }
                using var k = root.CreateSubKey(s.Path, writable: true);
                object value = s.Kind switch
                {
                    RegistryValueKind.DWord => int.Parse(s.Data!, System.Globalization.CultureInfo.InvariantCulture),
                    RegistryValueKind.QWord => long.Parse(s.Data!, System.Globalization.CultureInfo.InvariantCulture),
                    RegistryValueKind.Binary or RegistryValueKind.None => Convert.FromBase64String(s.Data!),
                    RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(s.Data!)!,
                    _ => s.Data ?? "",
                };
                k.SetValue(s.Name, value, s.Kind);
            }

            all.Remove(id);
            Save(all);
            return true;
        }
    }

    private static Dictionary<string, List<Saved>> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, List<Saved>>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void Save(Dictionary<string, List<Saved>> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }
}
