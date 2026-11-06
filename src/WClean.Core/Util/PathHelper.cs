using System.Text.RegularExpressions;

namespace WClean;

public static class PathHelper
{
    private static readonly Regex ExeRegex = new(
        @"^(.*?\.(exe|bat|cmd|com|lnk|msi|scr|cpl))(\s|,|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Pulls the program path out of a command line such as <c>"C:\a b\x.exe" -min</c>.</summary>
    public static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var s = Environment.ExpandEnvironmentVariables(command.Trim());

        if (s.StartsWith('"'))
        {
            int end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : s.TrimStart('"');
        }

        if (File.Exists(s) || Directory.Exists(s)) return s;

        var m = ExeRegex.Match(s);
        if (m.Success) return m.Groups[1].Value;

        int sp = s.IndexOf(' ');
        return sp > 0 ? s[..sp] : s;
    }

    /// <summary>
    /// True only when the command clearly points to a file that is gone.
    /// Relative names, UNC paths and disks that are not connected count as "not missing".
    /// </summary>
    public static bool IsMissing(string? command)
    {
        var p = ExtractExecutable(command);
        return IsPathMissing(p);
    }

    public static bool IsPathMissing(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (path.StartsWith(@"\\")) return false;
        if (!Path.IsPathFullyQualified(path)) return false;

        string? root;
        try { root = Path.GetPathRoot(path); }
        catch { return false; }
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;

        try { return !File.Exists(path) && !Directory.Exists(path); }
        catch { return false; }
    }
}
