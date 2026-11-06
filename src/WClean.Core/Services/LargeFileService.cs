using System.IO.Enumeration;

namespace WClean;

public sealed class LargeFileInfo : ObservableObject
{
    public LargeFileInfo(string path, long size, DateTime modified)
    {
        Path = path;
        Size = size;
        Modified = modified;
    }

    public string Path { get; }
    public long Size { get; }
    public DateTime Modified { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string SizeText => SizeFormat.ToText(Size);
    /// <summary>Size relative to the biggest result (0..1).</summary>
    public double Ratio { get; set; }

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

public sealed class LargeFileService
{
    private static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>The Windows folder is never scanned or deleted from.</summary>
    public static bool IsProtected(string path)
    {
        var win = WinDir.TrimEnd('\\') + "\\";
        return path.StartsWith(win, StringComparison.OrdinalIgnoreCase);
    }

    public List<LargeFileInfo> Scan(
        IEnumerable<string> roots, long minBytes, int maxResults,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var results = new List<LargeFileInfo>();
        long dirCounter = 0;
        var opt = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            ReturnSpecialDirectories = false,
        };

        try
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                var en = new FileSystemEnumerable<LargeFileInfo>(
                    root,
                    (ref FileSystemEntry e) => new LargeFileInfo(e.ToFullPath(), e.Length, e.LastWriteTimeUtc.LocalDateTime),
                    opt)
                {
                    ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && e.Length >= minBytes,
                    ShouldRecursePredicate = (ref FileSystemEntry e) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var full = e.ToFullPath();
                        if (IsProtected(full + "\\")) return false;
                        if ((++dirCounter & 0xFF) == 0) progress?.Report(full);
                        return true;
                    },
                };
                foreach (var f in en)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!IsProtected(f.Path)) results.Add(f);
                }
            }
        }
        catch (OperationCanceledException) { /* return what was found so far */ }

        var top = results.OrderByDescending(r => r.Size).Take(maxResults).ToList();
        if (top.Count == 0) return top;
        double max = top[0].Size;
        foreach (var r in top) r.Ratio = r.Size / max;
        return top;
    }
}
