using System.Text;
using Microsoft.Win32;

namespace WClean;

/// <summary>Builds a .reg file that restores the values/keys we are about to delete.</summary>
public sealed class RegBackupWriter
{
    private readonly StringBuilder _sb = new("Windows Registry Editor Version 5.00\r\n");
    private bool _hasContent;

    public bool IsEmpty => !_hasContent;

    public void AddValue(RegLocation key, string valueName)
    {
        using var k = key.Open();
        if (k is null) return;
        Header(key);
        AppendValue(k, valueName);
    }

    public void AddKeyTree(RegLocation key)
    {
        using var k = key.Open();
        if (k is null) return;
        Header(key);
        foreach (var n in k.GetValueNames()) AppendValue(k, n);
        foreach (var sub in k.GetSubKeyNames()) AddKeyTree(key.Child(sub));
    }

    public string Save(string label)
    {
        var path = BackupStore.NewFilePath(label, ".reg");
        File.WriteAllText(path, _sb.ToString(), Encoding.Unicode);
        return path;
    }

    public override string ToString() => _sb.ToString();

    private void Header(RegLocation key)
    {
        _hasContent = true;
        _sb.Append("\r\n[").Append(key.RegFilePath).Append("]\r\n");
    }

    private void AppendValue(RegistryKey k, string name)
    {
        try
        {
            var kind = k.GetValueKind(name);
            var value = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null) return;
            _sb.Append(FormatValue(name, value, kind)).Append("\r\n");
        }
        catch { /* value vanished or unreadable */ }
    }

    public static string FormatValue(string name, object value, RegistryValueKind kind)
    {
        var n = name.Length == 0 ? "@" : $"\"{Escape(name)}\"";
        switch (kind)
        {
            case RegistryValueKind.String:
                return $"{n}=\"{Escape((string)value)}\"";
            case RegistryValueKind.ExpandString:
                return $"{n}=hex(2):{Hex(Encoding.Unicode.GetBytes((string)value + "\0"))}";
            case RegistryValueKind.DWord:
                return $"{n}=dword:{unchecked((uint)(int)value):x8}";
            case RegistryValueKind.QWord:
                return $"{n}=hex(b):{Hex(BitConverter.GetBytes(unchecked((ulong)(long)value)))}";
            case RegistryValueKind.MultiString:
            {
                var sb = new StringBuilder();
                foreach (var s in (string[])value) sb.Append(s).Append('\0');
                sb.Append('\0');
                return $"{n}=hex(7):{Hex(Encoding.Unicode.GetBytes(sb.ToString()))}";
            }
            case RegistryValueKind.Binary:
                return $"{n}=hex:{Hex((byte[])value)}";
            default:
                return $"{n}=hex({(int)kind:x}):{Hex(value as byte[] ?? Array.Empty<byte>())}";
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Hex(byte[] bytes) => string.Join(",", bytes.Select(b => b.ToString("x2")));
}
