using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WClean;

public sealed class BackupItem
{
    public string Path { get; init; } = "";
    public string Label { get; init; } = "";
    public DateTime Time { get; init; }
    public long Size { get; init; }
    public bool IsRegistry { get; init; }
    public string KindText => IsRegistry ? "레지스트리" : "작업 스케줄러";
    public string Glyph => IsRegistry ? "" : "";
    public string SizeText => SizeFormat.ToText(Size);
    public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");
}

/// <summary>Lists, restores and deletes the backups that cleaning operations leave in %AppData%\WClean\Backups.</summary>
public sealed class BackupService
{
    private static readonly Regex NameRegex = new(@"^(\d{8}-\d{6})-(.*)$", RegexOptions.Compiled);

    public List<BackupItem> Load()
    {
        var list = new List<BackupItem>();
        foreach (var f in new DirectoryInfo(BackupStore.Directory).EnumerateFiles())
        {
            var ext = f.Extension.ToLowerInvariant();
            if (ext is not (".reg" or ".xml")) continue;
            var stem = System.IO.Path.GetFileNameWithoutExtension(f.Name);
            var m = NameRegex.Match(stem);
            list.Add(new BackupItem
            {
                Path = f.FullName,
                Label = m.Success ? m.Groups[2].Value : stem,
                Time = m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss",
                           System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t)
                       ? t : f.LastWriteTime,
                Size = f.Length,
                IsRegistry = ext == ".reg",
            });
        }
        return list.OrderByDescending(b => b.Time).ToList();
    }

    /// <summary>Puts a backup back (reg import / task re-create). Throws with the tool's message on failure.</summary>
    public void Restore(BackupItem b)
    {
        if (b.IsRegistry) Run("reg.exe", $"import \"{b.Path}\"");
        else
        {
            var xml = File.ReadAllText(b.Path);
            var uri = Regex.Match(xml, @"<URI>([^<]+)</URI>");
            var name = uri.Success ? System.Net.WebUtility.HtmlDecode(uri.Groups[1].Value)
                                   : (b.Label.StartsWith("task-") ? b.Label[5..] : b.Label);
            Run("schtasks.exe", $"/create /f /tn \"{name}\" /xml \"{b.Path}\"");
        }
    }

    public static void Delete(BackupItem b) => File.Delete(b.Path);

    private static void Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException((err.Trim().Length > 0 ? err : outp).Trim());
    }
}
