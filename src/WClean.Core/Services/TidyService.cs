namespace WClean;

public enum TidyKind { EmptyFolder, BrokenShortcut }

public sealed class TidyItem : ObservableObject
{
    public TidyKind Kind { get; init; }
    public string Path { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string KindText => Kind == TidyKind.EmptyFolder ? "빈 폴더" : "깨진 바로가기";
    public string Glyph => Kind == TidyKind.EmptyFolder ? "" : "";

    private bool _isChecked = true;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

/// <summary>Finds empty folders and shortcuts whose target no longer exists.</summary>
public sealed class TidyService
{
    public static IEnumerable<string> ShortcutFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
    }

    public List<TidyItem> FindBrokenShortcuts(CancellationToken ct = default)
    {
        var list = new List<TidyItem>();
        foreach (var dir in ShortcutFolders().Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var f in Fs.EnumerateFiles(dir, "*.lnk"))
            {
                ct.ThrowIfCancellationRequested();
                var (target, _) = StartupService.ResolveShortcut(f.Path);
                if (target is not null && PathHelper.IsPathMissing(target))
                    list.Add(new TidyItem { Kind = TidyKind.BrokenShortcut, Path = f.Path, Detail = "대상 없음: " + target });
            }
        }
        return list;
    }

    /// <summary>Folders that contain no files at all (sub-folders are fine as long as they are empty too).</summary>
    public List<TidyItem> FindEmptyFolders(string root, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var found = new List<TidyItem>();
        if (!Directory.Exists(root)) return found;
        long n = 0;
        Walk(root, isRoot: true);
        return found;

        // returns true when the folder holds no files anywhere below it
        bool Walk(string dir, bool isRoot)
        {
            ct.ThrowIfCancellationRequested();
            if ((++n & 0x7F) == 0) progress?.Report(dir);
            bool empty = true;
            var childEmpty = new List<string>();
            try
            {
                var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", opt))
                {
                    if (e is DirectoryInfo di)
                    {
                        if (IsSkipped(di)) { empty = false; continue; }
                        if (Walk(di.FullName, false)) childEmpty.Add(di.FullName);
                        else empty = false;
                    }
                    else empty = false;
                }
            }
            catch { return false; }   // unreadable folder: assume it has content

            // Report only the top-most empty folder, not every empty child inside it.
            if (empty && !isRoot) return true;
            foreach (var c in childEmpty)
                found.Add(new TidyItem { Kind = TidyKind.EmptyFolder, Path = c, Detail = "비어 있음" });
            return false;
        }
    }

    private static bool IsSkipped(DirectoryInfo d)
    {
        var name = d.Name;
        if (name.StartsWith('.')) return true;   // .git, .vs ...
        if (LargeFileService.IsProtected(d.FullName + "\\")) return true;
        if (string.Equals(name, "AppData", StringComparison.OrdinalIgnoreCase)) return true;
        return (d.Attributes & (FileAttributes.System | FileAttributes.ReparsePoint)) != 0;
    }

    public void Delete(TidyItem item)
    {
        if (item.Kind == TidyKind.BrokenShortcut) Fs.DeleteToRecycleBin(item.Path);
        else if (Directory.Exists(item.Path))
        {
            // Only remove when it still holds no files (it may have changed since the scan).
            if (Fs.EnumerateFiles(item.Path).Any()) throw new IOException("폴더에 파일이 생겨서 삭제하지 않았습니다.");
            Directory.Delete(item.Path, true);
        }
    }
}
