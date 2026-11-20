using System.Text;
using Microsoft.Win32;

namespace WinTracer.Core.RegistryTools;

public readonly record struct RegKeyRef(RegistryHive Hive, RegistryView View, string Path)
{
    public RegistryKey? Open(bool writable = false)
    {
        using var root = RegistryKey.OpenBaseKey(Hive, View);
        return root.OpenSubKey(Path, writable);
    }

    public string DisplayPath
    {
        get
        {
            string hive = Hive switch
            {
                RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
                RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
                RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
                RegistryHive.Users => "HKEY_USERS",
                _ => Hive.ToString(),
            };
            string path = Path;
            if (Hive == RegistryHive.LocalMachine && View == RegistryView.Registry32 &&
                path.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(@"SOFTWARE\WOW6432Node", StringComparison.OrdinalIgnoreCase))
            {
                path = @"SOFTWARE\WOW6432Node\" + path[9..];
            }
            return $@"{hive}\{path}";
        }
    }

    public string ShortPath => DisplayPath.Replace("HKEY_LOCAL_MACHINE", "HKLM").Replace("HKEY_CURRENT_USER", "HKCU");
}

public sealed class RegFileBuilder
{
    private readonly StringBuilder _sb = new StringBuilder().AppendLine("Windows Registry Editor Version 5.00").AppendLine();

    public bool IsEmpty { get; private set; } = true;

    public void AddKey(RegKeyRef key)
    {
        using var k = key.Open();
        if (k is null) return;
        AppendKey(k, key.DisplayPath);
    }

    public void AddValue(RegKeyRef key, string valueName)
    {
        using var k = key.Open();
        if (k is null || !k.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase)) return;
        _sb.AppendLine($"[{key.DisplayPath}]");
        AppendValue(k, valueName);
        _sb.AppendLine();
        IsEmpty = false;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, _sb.ToString(), Encoding.Unicode);
    }

    private void AppendKey(RegistryKey key, string displayPath)
    {
        _sb.AppendLine($"[{displayPath}]");
        foreach (var name in key.GetValueNames())
            AppendValue(key, name);
        _sb.AppendLine();
        IsEmpty = false;

        foreach (var sub in key.GetSubKeyNames())
        {
            try
            {
                using var child = key.OpenSubKey(sub);
                if (child is not null) AppendKey(child, $@"{displayPath}\{sub}");
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        }
    }

    private void AppendValue(RegistryKey key, string name)
    {
        string lhs = name.Length == 0 ? "@" : $"\"{Escape(name)}\"";
        RegistryValueKind kind;
        object? value;
        try
        {
            kind = key.GetValueKind(name);
            value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            return;
        }

        string rhs = kind switch
        {
            RegistryValueKind.String => $"\"{Escape(value as string ?? "")}\"",
            RegistryValueKind.DWord => $"dword:{unchecked((uint)(int)(value ?? 0)):x8}",
            RegistryValueKind.QWord => "hex(b):" + Hex(BitConverter.GetBytes((long)(value ?? 0L))),
            RegistryValueKind.ExpandString => "hex(2):" + Hex(Utf16Z(value as string ?? "")),
            RegistryValueKind.MultiString => "hex(7):" + Hex(MultiSz(value as string[] ?? [])),
            RegistryValueKind.Binary => "hex:" + Hex(value as byte[] ?? []),
            _ => "hex(0):" + Hex(value as byte[] ?? []),
        };
        _sb.AppendLine($"{lhs}={rhs}");
    }

    private static string Escape(string s) => s.Replace(@"\", @"\\").Replace("\"", "\\\"");

    private static string Hex(byte[] bytes) => string.Join(",", bytes.Select(b => b.ToString("x2")));

    private static byte[] Utf16Z(string s) => Encoding.Unicode.GetBytes(s + '\0');

    private static byte[] MultiSz(string[] items) => Encoding.Unicode.GetBytes(string.Concat(items.Select(i => i + '\0')) + '\0');
}
