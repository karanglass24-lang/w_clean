using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace WClean;

public sealed class StartupService
{
    private const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private sealed record RunSource(RegistryHive Hive, RegistryView View, string Approved, string Scope);

    private static readonly RunSource[] RunSources =
    {
        new(RegistryHive.CurrentUser, RegistryView.Registry64, "Run", "레지스트리 · 현재 사용자"),
        new(RegistryHive.LocalMachine, RegistryView.Registry64, "Run", "레지스트리 · 모든 사용자"),
        new(RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", "레지스트리 · 모든 사용자(32비트)"),
    };

    public List<StartupEntry> Load()
    {
        var list = new List<StartupEntry>();
        LoadRegistry(list);
        LoadFolders(list);
        LoadTasks(list);
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---------------------------------------------------------------- load

    private static void LoadRegistry(List<StartupEntry> list)
    {
        foreach (var s in RunSources)
        {
            try
            {
                using var key = new RegLocation(s.Hive, s.View, RunPath).Open();
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                {
                    var cmd = key.GetValue(name)?.ToString() ?? "";
                    list.Add(new StartupEntry
                    {
                        Name = name.Length == 0 ? "(기본값)" : name,
                        Command = cmd,
                        Source = s.Scope,
                        Kind = StartupKind.Registry,
                        TargetMissing = PathHelper.IsMissing(cmd),
                        IsEnabled = IsApproved(s.Hive, s.Approved, name),
                        Hive = s.Hive,
                        View = s.View,
                        ApprovedKey = s.Approved,
                        ValueName = name,
                    });
                }
            }
            catch { /* hive not readable */ }
        }
    }

    private static void LoadFolders(List<StartupEntry> list)
    {
        var folders = new[]
        {
            (Dir: Environment.GetFolderPath(Environment.SpecialFolder.Startup), Hive: RegistryHive.CurrentUser, Scope: "시작 폴더 · 현재 사용자"),
            (Dir: Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Hive: RegistryHive.LocalMachine, Scope: "시작 폴더 · 모든 사용자"),
        };

        foreach (var f in folders)
        {
            try
            {
                if (!Directory.Exists(f.Dir)) continue;
                foreach (var file in Directory.EnumerateFiles(f.Dir))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    var (target, args) = ResolveShortcut(file);
                    var command = target is null ? file : $"{target} {args}".Trim();
                    list.Add(new StartupEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(fileName),
                        Command = command,
                        Source = f.Scope,
                        Kind = StartupKind.Folder,
                        TargetMissing = target is not null && PathHelper.IsPathMissing(target),
                        IsEnabled = IsApproved(f.Hive, "StartupFolder", fileName),
                        Hive = f.Hive,
                        View = RegistryView.Registry64,
                        ApprovedKey = "StartupFolder",
                        ValueName = fileName,
                        FilePath = file,
                    });
                }
            }
            catch { }
        }
    }

    private static void LoadTasks(List<StartupEntry> list)
    {
        try
        {
            using var ts = new TaskService();
            foreach (var task in ts.AllTasks)
            {
                try
                {
                    if (task.Path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase)) continue;
                    var def = task.Definition;
                    if (!def.Triggers.Any(t => t.TriggerType is TaskTriggerType.Logon or TaskTriggerType.Boot)) continue;

                    var exec = def.Actions.OfType<ExecAction>().FirstOrDefault();
                    var cmd = exec is null ? "" : $"{exec.Path} {exec.Arguments}".Trim();
                    list.Add(new StartupEntry
                    {
                        Name = task.Name,
                        Command = cmd,
                        Source = "작업 스케줄러",
                        Kind = StartupKind.Task,
                        TargetMissing = exec is not null && PathHelper.IsPathMissing(exec.Path),
                        IsEnabled = task.Enabled,
                        TaskPath = task.Path,
                    });
                }
                catch { /* unreadable task */ }
            }
        }
        catch { /* Task Scheduler not reachable */ }
    }

    private static (string? Target, string Args) ResolveShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return (null, "");
        object? shell = null, lnk = null;
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t is null) return (null, "");
            shell = Activator.CreateInstance(t);
            dynamic sh = shell!;
            lnk = sh.CreateShortcut(path);
            dynamic l = lnk!;
            string target = l.TargetPath ?? "";
            string args = l.Arguments ?? "";
            return (string.IsNullOrWhiteSpace(target) ? null : target, args);
        }
        catch { return (null, ""); }
        finally
        {
            if (lnk is not null) Marshal.FinalReleaseComObject(lnk);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    // ---------------------------------------------------------------- change

    public void SetEnabled(StartupEntry e, bool enabled)
    {
        if (e.Kind == StartupKind.Task)
        {
            using var ts = new TaskService();
            var task = ts.GetTask(e.TaskPath!) ?? throw new InvalidOperationException("작업을 찾을 수 없습니다.");
            task.Enabled = enabled;
        }
        else
        {
            SetApproved(e.Hive, e.ApprovedKey, e.ValueName, enabled);
        }
        e.IsEnabled = enabled;
    }

    /// <returns>Path of the backup that was written, or null.</returns>
    public string? Delete(StartupEntry e)
    {
        switch (e.Kind)
        {
            case StartupKind.Registry:
            {
                var loc = new RegLocation(e.Hive, e.View, RunPath);
                var w = new RegBackupWriter();
                w.AddValue(loc, e.ValueName);
                var backup = w.IsEmpty ? null : w.Save("startup-" + e.Name);
                using (var k = loc.Open(true))
                    k?.DeleteValue(e.ValueName, false);
                RemoveApproved(e.Hive, e.ApprovedKey, e.ValueName);
                return backup;
            }
            case StartupKind.Folder:
                Fs.DeleteToRecycleBin(e.FilePath!);
                RemoveApproved(e.Hive, e.ApprovedKey, e.ValueName);
                return null;
            case StartupKind.Task:
            {
                using var ts = new TaskService();
                var task = ts.GetTask(e.TaskPath!) ?? throw new InvalidOperationException("작업을 찾을 수 없습니다.");
                var backup = BackupStore.NewFilePath("task-" + e.Name, ".xml");
                File.WriteAllText(backup, task.Xml);
                task.Folder.DeleteTask(task.Name);
                return backup;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- StartupApproved

    /// <summary>First byte odd (03) means "disabled" in Task Manager; missing value means enabled.</summary>
    internal static bool IsApproved(RegistryHive hive, string approvedKey, string valueName)
    {
        try
        {
            using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = b.OpenSubKey($@"{ApprovedRoot}\{approvedKey}");
            if (k?.GetValue(valueName) is byte[] d && d.Length > 0) return (d[0] & 1) == 0;
        }
        catch { }
        return true;
    }

    internal static void SetApproved(RegistryHive hive, string approvedKey, string valueName, bool enabled)
    {
        using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var k = b.CreateSubKey($@"{ApprovedRoot}\{approvedKey}", true);
        var data = new byte[12];
        if (enabled) data[0] = 2;
        else
        {
            data[0] = 3;
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        }
        k.SetValue(valueName, data, RegistryValueKind.Binary);
    }

    internal static void RemoveApproved(RegistryHive hive, string approvedKey, string valueName)
    {
        try
        {
            using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = b.OpenSubKey($@"{ApprovedRoot}\{approvedKey}", true);
            k?.DeleteValue(valueName, false);
        }
        catch { }
    }
}
