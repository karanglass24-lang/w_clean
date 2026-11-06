using System.IO.Enumeration;
using Microsoft.VisualBasic.FileIO;

namespace WClean;

public readonly record struct FileEntry(string Path, long Length, DateTime LastWriteUtc);

public static class Fs
{
    public static IEnumerable<FileEntry> EnumerateFiles(string dir, string pattern = "*", bool recurse = true)
    {
        if (!Directory.Exists(dir)) return Array.Empty<FileEntry>();

        var opt = new EnumerationOptions
        {
            RecurseSubdirectories = recurse,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };
        bool all = pattern == "*";
        return new FileSystemEnumerable<FileEntry>(
            dir,
            (ref FileSystemEntry e) => new FileEntry(e.ToFullPath(), e.Length, e.LastWriteTimeUtc.UtcDateTime),
            opt)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) =>
                !e.IsDirectory && (all || FileSystemName.MatchesWin32Expression(pattern, e.FileName)),
        };
    }

    public static bool TryDelete(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            File.Delete(path);
            return !File.Exists(path);
        }
        catch { return false; }
    }

    public static void DeleteToRecycleBin(string path)
    {
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    /// <summary>Removes empty sub-folders below <paramref name="root"/> (the root itself stays).</summary>
    public static void PruneEmptyDirs(string root)
    {
        if (!Directory.Exists(root)) return;
        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            }).OrderByDescending(d => d.Length).ToList();
        }
        catch { return; }

        foreach (var d in dirs)
        {
            try { Directory.Delete(d, false); } catch { /* not empty or locked */ }
        }
    }
}
