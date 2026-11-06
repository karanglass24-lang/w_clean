using Microsoft.Win32;

namespace WClean;

/// <summary>A registry key in a specific hive and 32/64-bit view.</summary>
public sealed record RegLocation(RegistryHive Hive, RegistryView View, string SubKey)
{
    public RegLocation Child(string name) => this with { SubKey = SubKey.Length == 0 ? name : SubKey + "\\" + name };

    public RegistryKey? Open(bool writable = false)
    {
        using var baseKey = RegistryKey.OpenBaseKey(Hive, View);
        return SubKey.Length == 0 ? baseKey.OpenSubKey("", writable) : baseKey.OpenSubKey(SubKey, writable);
    }

    public string HiveName => Hive switch
    {
        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
        RegistryHive.Users => "HKEY_USERS",
        _ => Hive.ToString().ToUpperInvariant(),
    };

    /// <summary>Path as shown to the user.</summary>
    public string DisplayPath
        => $"{HiveName}\\{SubKey}" + (View == RegistryView.Registry32 && Hive == RegistryHive.LocalMachine ? "  (32비트)" : "");

    /// <summary>Path as written in a .reg file (32-bit view of HKLM\SOFTWARE lives under WOW6432Node).</summary>
    public string RegFilePath
    {
        get
        {
            var sub = SubKey;
            if (View == RegistryView.Registry32 && Hive == RegistryHive.LocalMachine
                && sub.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase))
            {
                sub = "SOFTWARE\\WOW6432Node\\" + sub["SOFTWARE\\".Length..];
            }
            return sub.Length == 0 ? HiveName : $"{HiveName}\\{sub}";
        }
    }
}
