using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace WClean;

public sealed class InstalledProgram
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public DateTime? InstallDate { get; init; }
    public long SizeBytes { get; init; }
    public string? InstallLocation { get; init; }
    public string UninstallString { get; init; } = "";
    public string Source { get; init; } = "";

    public string SizeText => SizeBytes > 0 ? SizeFormat.ToText(SizeBytes) : "-";
    public string DateText => InstallDate?.ToString("yyyy-MM-dd") ?? "-";
}

public sealed class ProgramService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly (RegistryHive Hive, RegistryView View, string Scope)[] Sources =
    {
        (RegistryHive.LocalMachine, RegistryView.Registry64, "모든 사용자"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, "모든 사용자 (32비트)"),
        (RegistryHive.CurrentUser, RegistryView.Registry64, "현재 사용자"),
    };

    public List<InstalledProgram> Load()
    {
        var list = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view, scope) in Sources)
        {
            try
            {
                using var key = new RegLocation(hive, view, UninstallPath).Open();
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var k = key.OpenSubKey(sub);
                        if (k is null) continue;
                        var p = Read(k, scope);
                        if (p is not null && seen.Add(p.Name + "|" + p.Version)) list.Add(p);
                    }
                    catch { }
                }
            }
            catch { }
        }
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static InstalledProgram? Read(RegistryKey k, string scope)
    {
        var name = k.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (k.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (k.GetValue("ParentKeyName") is not null) return null;   // updates / patches of another product
        var uninstall = (k.GetValue("QuietUninstallString") ?? k.GetValue("UninstallString")) as string;
        var normal = k.GetValue("UninstallString") as string;
        if (string.IsNullOrWhiteSpace(normal ?? uninstall)) return null;

        DateTime? date = null;
        if (k.GetValue("InstallDate") is string ds &&
            DateTime.TryParseExact(ds, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            date = d;

        long size = 0;
        if (k.GetValue("EstimatedSize") is int kb && kb > 0) size = kb * 1024L;

        var loc = (k.GetValue("InstallLocation") as string)?.Trim().Trim('"');
        return new InstalledProgram
        {
            Name = name.Trim(),
            Version = k.GetValue("DisplayVersion") as string ?? "",
            Publisher = k.GetValue("Publisher") as string ?? "",
            InstallDate = date,
            SizeBytes = size,
            InstallLocation = string.IsNullOrWhiteSpace(loc) ? null : loc,
            UninstallString = normal ?? uninstall!,
            Source = scope,
        };
    }

    /// <summary>Runs the program's own uninstaller and waits for it to finish.</summary>
    public static Task RunUninstallerAsync(InstalledProgram p)
        => Task.Run(() =>
        {
            // cmd strips the outer quotes, so quoted paths with arguments keep working.
            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + p.UninstallString + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        });

    /// <summary>The install folder if it is still there after uninstalling and is safe to remove.</summary>
    public static string? LeftoverFolder(InstalledProgram p)
    {
        var loc = p.InstallLocation;
        if (string.IsNullOrWhiteSpace(loc) || !Path.IsPathFullyQualified(loc)) return null;
        loc = loc.TrimEnd('\\');
        if (!Directory.Exists(loc)) return null;
        if (IsTooBroad(loc)) return null;
        return loc;
    }

    private static bool IsTooBroad(string path)
    {
        var root = Path.GetPathRoot(path)?.TrimEnd('\\') ?? "";
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return true;
        var protectedDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };
        return protectedDirs.Any(d => !string.IsNullOrEmpty(d) && string.Equals(d.TrimEnd('\\'), path, StringComparison.OrdinalIgnoreCase))
               || path.Split('\\').Length <= 2;
    }

    public static long FolderSize(string dir) => Fs.EnumerateFiles(dir).Sum(f => f.Length);
}
