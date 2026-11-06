using System.Security.Cryptography;

namespace WClean;

public sealed class DuplicateFile : ObservableObject
{
    public string Path { get; init; } = "";
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public int Group { get; init; }
    public bool AltGroup => Group % 2 == 1;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string SizeText => SizeFormat.ToText(Size);

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

public sealed class DuplicateService
{
    /// <summary>Finds files with identical content: group by size, then by a sample hash, then by a full SHA-256.</summary>
    public List<DuplicateFile> Scan(IEnumerable<string> roots, long minBytes, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var bySize = new Dictionary<long, List<FileEntry>>();
        var opt = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };
        long seen = 0;
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var f in Fs.EnumerateFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                if (f.Length < minBytes || LargeFileService.IsProtected(f.Path)) continue;
                if ((++seen & 0x3FF) == 0) progress?.Report($"파일 목록 수집 중… {seen:N0}개");
                if (!bySize.TryGetValue(f.Length, out var l)) bySize[f.Length] = l = new();
                l.Add(f);
            }
        }

        var candidates = bySize.Values.Where(l => l.Count > 1).ToList();
        var groups = new List<List<FileEntry>>();
        int done = 0;
        foreach (var sameSize in candidates)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"내용 비교 중… {++done}/{candidates.Count}");

            // 1) cheap: hash of the first/last 64 KB
            foreach (var partial in sameSize.GroupBy(f => Hash(f.Path, sample: true, ct)).Where(g => g.Key is not null && g.Count() > 1))
                // 2) full hash only for files that still look equal
                foreach (var full in partial.GroupBy(f => Hash(f.Path, sample: false, ct)).Where(g => g.Key is not null && g.Count() > 1))
                    groups.Add(full.ToList());
        }

        var result = new List<DuplicateFile>();
        int gi = 0;
        foreach (var g in groups.OrderByDescending(g => g[0].Length * (g.Count - 1)))
        {
            foreach (var f in g.OrderBy(f => f.LastWriteUtc).ThenBy(f => f.Path.Length))
                result.Add(new DuplicateFile { Path = f.Path, Size = f.Length, Modified = f.LastWriteUtc.ToLocalTime(), Group = gi });
            gi++;
        }
        return result;
    }

    /// <summary>Checks the first file in every group (the oldest) and unchecks it; checks the rest.</summary>
    public static void SelectAllButOne(IEnumerable<DuplicateFile> files)
    {
        foreach (var g in files.GroupBy(f => f.Group))
        {
            bool first = true;
            foreach (var f in g) { f.IsChecked = !first; first = false; }
        }
    }

    private static string? Hash(string path, bool sample, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            using var sha = SHA256.Create();
            var buf = new byte[1 << 16];
            if (sample)
            {
                int n = fs.Read(buf, 0, buf.Length);
                sha.TransformBlock(buf, 0, n, null, 0);
                if (fs.Length > buf.Length * 2L)
                {
                    fs.Seek(-buf.Length, SeekOrigin.End);
                    n = fs.Read(buf, 0, buf.Length);
                    sha.TransformBlock(buf, 0, n, null, 0);
                }
            }
            else
            {
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    sha.TransformBlock(buf, 0, n, null, 0);
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }   // unreadable file: never treated as a duplicate
    }
}
