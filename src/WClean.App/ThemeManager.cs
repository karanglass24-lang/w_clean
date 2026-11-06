using System.Windows;
using Microsoft.Win32;

namespace WClean.App;

public static class ThemeManager
{
    private static string PrefFile
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WClean");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "theme.txt");
        }
    }

    /// <summary>Saved choice, otherwise the Windows app theme.</summary>
    public static bool LoadPreference()
    {
        try
        {
            if (File.Exists(PrefFile)) return File.ReadAllText(PrefFile).Trim() == "dark";
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static void Apply(bool dark, bool save = false)
    {
        var app = Application.Current;
        var dict = new ResourceDictionary { Source = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        app.Resources.MergedDictionaries[0] = dict;
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
        if (save)
        {
            try { File.WriteAllText(PrefFile, dark ? "dark" : "light"); } catch { }
        }
    }
}
