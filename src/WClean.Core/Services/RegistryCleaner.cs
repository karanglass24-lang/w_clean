using Microsoft.Win32;

namespace WClean;

public sealed class RegistryIssue : ObservableObject
{
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string Problem { get; init; } = "";
    public string KeyPath => KeyName is null
        ? $"{Parent.DisplayPath}  →  {ValueName}"
        : Parent.Child(KeyName).DisplayPath;

    private bool _isChecked = true;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    internal RegLocation Parent { get; init; } = null!;
    /// <summary>Set when the whole sub-key should be removed.</summary>
    internal string? KeyName { get; init; }
    /// <summary>Set when only one value should be removed.</summary>
    internal string? ValueName { get; init; }
    /// <summary>Further values in the same key that are removed together with <see cref="ValueName"/>.</summary>
    internal string[] ExtraValueNames { get; init; } = Array.Empty<string>();
}

public sealed record RegistryFixResult(int Fixed, int Failed, string? BackupPath);

public sealed class RegistryCleaner
{
    private static readonly string WinDirPrefix = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows\CurrentVersion";

    private static readonly (RegistryHive Hive, RegistryView View)[] MachineAndUser =
    {
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.CurrentUser, RegistryView.Registry64),
    };

    public List<RegistryIssue> Scan(CancellationToken ct = default)
    {
        var issues = new List<RegistryIssue>();
        Run(() => ScanUninstall(issues, ct));
        Run(() => ScanAppPaths(issues, ct));
        Run(() => ScanSharedDlls(issues, ct));
        Run(() => ScanMuiCache(issues, ct));
        Run(() => ScanRunEntries(issues, ct));
        return issues;
    }

    private static void Run(Action a)
    {
        try { a(); }
        catch (OperationCanceledException) { throw; }
        catch { /* one broken area must not stop the others */ }
    }

    // ------------------------------------------------------------ scanners

    private static void ScanUninstall(List<RegistryIssue> issues, CancellationToken ct)
    {
        foreach (var (hive, view) in MachineAndUser)
        {
            var parent = new RegLocation(hive, view, CurrentVersion + @"\Uninstall");
            using var k = parent.Open();
            if (k is null) continue;

            foreach (var sub in k.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                using var s = k.OpenSubKey(sub);
                if (s is null) continue;

                var display = s.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(display)) continue;
                if (s.GetValue("SystemComponent") is int sc && sc == 1) continue;

                var uninstall = (s.GetValue("UninstallString") ?? s.GetValue("QuietUninstallString")) as string;
                if (string.IsNullOrWhiteSpace(uninstall) || !PathHelper.IsMissing(uninstall)) continue;

                // If anything else of the program is still on disk, leave the entry alone.
                var loc = s.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(loc) && !PathHelper.IsPathMissing(loc)) continue;
                var icon = s.GetValue("DisplayIcon") as string;
                if (!string.IsNullOrWhiteSpace(icon) && !PathHelper.IsMissing(icon)) continue;

                issues.Add(new RegistryIssue
                {
                    Category = "삭제된 프로그램",
                    Name = display,
                    Problem = "제거 프로그램 파일이 없음",
                    Parent = parent,
                    KeyName = sub,
                });
            }
        }
    }

    private static void ScanAppPaths(List<RegistryIssue> issues, CancellationToken ct)
    {
        foreach (var (hive, view) in MachineAndUser)
        {
            var parent = new RegLocation(hive, view, CurrentVersion + @"\App Paths");
            using var k = parent.Open();
            if (k is null) continue;

            foreach (var sub in k.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                using var s = k.OpenSubKey(sub);
                var path = s?.GetValue("") as string;
                if (string.IsNullOrWhiteSpace(path) || !PathHelper.IsMissing(path)) continue;

                issues.Add(new RegistryIssue
                {
                    Category = "응용 프로그램 경로",
                    Name = sub,
                    Problem = $"파일 없음: {path}",
                    Parent = parent,
                    KeyName = sub,
                });
            }
        }
    }

    private static void ScanSharedDlls(List<RegistryIssue> issues, CancellationToken ct)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            var parent = new RegLocation(RegistryHive.LocalMachine, view, CurrentVersion + @"\SharedDLLs");
            using var k = parent.Open();
            if (k is null) continue;

            foreach (var name in k.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                if (name.StartsWith(WinDirPrefix, StringComparison.OrdinalIgnoreCase)) continue; // owned by Windows
                if (!PathHelper.IsPathMissing(name)) continue;
                issues.Add(new RegistryIssue
                {
                    Category = "공유 DLL",
                    Name = Path.GetFileName(name),
                    Problem = $"파일 없음: {name}",
                    Parent = parent,
                    ValueName = name,
                });
            }
        }
    }

    private static void ScanMuiCache(List<RegistryIssue> issues, CancellationToken ct)
    {
        var parent = new RegLocation(RegistryHive.CurrentUser, RegistryView.Registry64,
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache");
        using var k = parent.Open();
        if (k is null) return;

        var byPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in k.GetValueNames())
        {
            ct.ThrowIfCancellationRequested();
            var path = name;
            foreach (var suffix in new[] { ".FriendlyAppName", ".ApplicationCompany" })
                if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { path = path[..^suffix.Length]; break; }

            if (!PathHelper.IsPathMissing(path)) continue;
            if (!byPath.TryGetValue(path, out var names)) byPath[path] = names = new List<string>();
            names.Add(name);
        }

        foreach (var (path, names) in byPath)
        {
            issues.Add(new RegistryIssue
            {
                Category = "프로그램 이름 캐시",
                Name = Path.GetFileName(path),
                Problem = $"파일 없음: {path}",
                Parent = parent,
                ValueName = names[0],
                ExtraValueNames = names.Skip(1).ToArray(),
            });
        }
    }

    private static void ScanRunEntries(List<RegistryIssue> issues, CancellationToken ct)
    {
        var sources = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        };
        foreach (var (hive, view) in sources)
        {
            var parent = new RegLocation(hive, view, CurrentVersion + @"\Run");
            using var k = parent.Open();
            if (k is null) continue;

            foreach (var name in k.GetValueNames())
            {
                ct.ThrowIfCancellationRequested();
                var cmd = k.GetValue(name) as string;
                if (!PathHelper.IsMissing(cmd)) continue;
                issues.Add(new RegistryIssue
                {
                    Category = "시작 프로그램(파일 없음)",
                    Name = name.Length == 0 ? "(기본값)" : name,
                    Problem = $"파일 없음: {cmd}",
                    Parent = parent,
                    ValueName = name,
                });
            }
        }
    }

    // ------------------------------------------------------------ fix

    /// <summary>Backs everything up to one .reg file first, then deletes.</summary>
    public RegistryFixResult Fix(IEnumerable<RegistryIssue> issues)
    {
        var list = issues.ToList();
        var writer = new RegBackupWriter();
        foreach (var i in list)
        {
            try
            {
                if (i.KeyName is not null) writer.AddKeyTree(i.Parent.Child(i.KeyName));
                else
                {
                    writer.AddValue(i.Parent, i.ValueName!);
                    foreach (var extra in i.ExtraValueNames) writer.AddValue(i.Parent, extra);
                }
            }
            catch { }
        }
        var backup = writer.IsEmpty ? null : writer.Save("registry-cleanup");

        int ok = 0, failed = 0;
        foreach (var i in list)
        {
            try
            {
                using var k = i.Parent.Open(true);
                if (k is null) { failed++; continue; }
                if (i.KeyName is not null) k.DeleteSubKeyTree(i.KeyName, false);
                else
                {
                    k.DeleteValue(i.ValueName!, false);
                    foreach (var extra in i.ExtraValueNames) k.DeleteValue(extra, false);
                }
                ok++;
            }
            catch { failed++; }
        }
        return new RegistryFixResult(ok, failed, backup);
    }
}
