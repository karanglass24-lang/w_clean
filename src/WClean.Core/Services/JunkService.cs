using System.Runtime.InteropServices;

namespace WClean;

public sealed class JunkCategory : ObservableObject
{
    public JunkCategory(string name, string description, Func<IEnumerable<FileEntry>>? files, Func<IEnumerable<string>>? roots = null, bool isRecycleBin = false)
    {
        Name = name;
        Description = description;
        Files = files;
        Roots = roots;
        IsRecycleBin = isRecycleBin;
    }

    public string Name { get; }
    public string Description { get; }
    public bool IsRecycleBin { get; }
    internal Func<IEnumerable<FileEntry>>? Files { get; }
    /// <summary>Folders whose empty sub-folders are removed after cleaning.</summary>
    internal Func<IEnumerable<string>>? Roots { get; }

    private bool _isChecked = true;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    private long _size;
    public long Size
    {
        get => _size;
        set { if (Set(ref _size, value)) Raise(nameof(SizeText)); }
    }

    private long _count;
    public long Count
    {
        get => _count;
        set { if (Set(ref _count, value)) Raise(nameof(SizeText)); }
    }

    private double _ratio;
    /// <summary>Size relative to the biggest category (0..1), for the bar in the list.</summary>
    public double Ratio { get => _ratio; set => Set(ref _ratio, value); }

    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; set; } = "";

    private bool _scanned;
    public bool Scanned
    {
        get => _scanned;
        set { if (Set(ref _scanned, value)) Raise(nameof(SizeText)); }
    }

    public string SizeText => Scanned ? $"{SizeFormat.ToText(Size)}  ({Count:N0}개)" : "-";
}

public sealed record CleanResult(long FreedBytes, long Deleted, long Skipped);

public sealed class JunkService
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string WinDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public List<JunkCategory> CreateCategories()
    {
        static Func<IEnumerable<FileEntry>> InDirs(Func<IEnumerable<string>> dirs)
            => () => dirs().SelectMany(d => Fs.EnumerateFiles(d));

        var tempDirs = () => new[] { Path.GetTempPath() };
        var winTemp = () => new[] { Path.Combine(WinDir, "Temp") };
        var wuDirs = () => new[]
        {
            Path.Combine(WinDir, @"SoftwareDistribution\Download"),
            Path.Combine(WinDir, @"SoftwareDistribution\DeliveryOptimization"),
        };
        var dumpDirs = () => new[]
        {
            Path.Combine(Local, "CrashDumps"),
            Path.Combine(WinDir, "Minidump"),
            Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive"),
            Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue"),
            Path.Combine(Local, @"Microsoft\Windows\WER"),
        };
        var shaderDirs = () => new[]
        {
            Path.Combine(Local, "D3DSCache"),
            Path.Combine(Local, @"NVIDIA\DXCache"),
            Path.Combine(Local, @"NVIDIA\GLCache"),
            Path.Combine(Local, @"AMD\DxCache"),
        };

        return new List<JunkCategory>
        {
            new("사용자 임시 파일", "%TEMP% 폴더의 임시 파일", InDirs(tempDirs), tempDirs),
            new("Windows 임시 파일", "C:\\Windows\\Temp (관리자 권한 필요)", InDirs(winTemp), winTemp),
            new("휴지통", "모든 드라이브의 휴지통", null, null, isRecycleBin: true),
            new("썸네일·아이콘 캐시", "탐색기 미리보기 캐시 (사용 중인 파일은 건너뜀)",
                () => Fs.EnumerateFiles(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", false)
                        .Concat(Fs.EnumerateFiles(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "iconcache_*.db", false))),
            new("Windows 업데이트 캐시", "내려받은 업데이트 설치 파일 / 배달 최적화 캐시", InDirs(wuDirs), wuDirs),
            new("오류 보고·크래시 덤프", "충돌 덤프와 Windows 오류 보고 파일", () => InDirs(dumpDirs)().Concat(MemoryDump()), dumpDirs),
            new("그래픽 셰이더 캐시", "DirectX / NVIDIA / AMD 셰이더 캐시 (게임 첫 실행 시 재생성)", InDirs(shaderDirs), shaderDirs),
            new("Chrome 캐시", "Chrome을 종료한 뒤 정리하세요", InDirs(() => ChromiumCacheDirs(Path.Combine(Local, @"Google\Chrome\User Data"))),
                () => ChromiumCacheDirs(Path.Combine(Local, @"Google\Chrome\User Data"))),
            new("Edge 캐시", "Edge를 종료한 뒤 정리하세요", InDirs(() => ChromiumCacheDirs(Path.Combine(Local, @"Microsoft\Edge\User Data"))),
                () => ChromiumCacheDirs(Path.Combine(Local, @"Microsoft\Edge\User Data"))),
            new("Whale 캐시", "네이버 웨일을 종료한 뒤 정리하세요", InDirs(() => ChromiumCacheDirs(Path.Combine(Local, @"Naver\Naver Whale\User Data"))),
                () => ChromiumCacheDirs(Path.Combine(Local, @"Naver\Naver Whale\User Data"))),
            new("Firefox 캐시", "Firefox를 종료한 뒤 정리하세요", InDirs(FirefoxCacheDirs), FirefoxCacheDirs),
        };
    }

    private static IEnumerable<FileEntry> MemoryDump()
    {
        var f = new FileInfo(Path.Combine(WinDir, "MEMORY.DMP"));
        if (f.Exists) yield return new FileEntry(f.FullName, f.Length, f.LastWriteTimeUtc);
    }

    private static IEnumerable<string> ChromiumCacheDirs(string userData)
    {
        if (!Directory.Exists(userData)) yield break;
        IEnumerable<string> profiles;
        try { profiles = Directory.EnumerateDirectories(userData).ToList(); }
        catch { yield break; }

        foreach (var profile in profiles)
        {
            var n = Path.GetFileName(profile);
            if (n != "Default" && !n.StartsWith("Profile ", StringComparison.Ordinal)) continue;
            foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage" })
                yield return Path.Combine(profile, sub);
        }
    }

    private static IEnumerable<string> FirefoxCacheDirs()
    {
        var root = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(root)) yield break;
        IEnumerable<string> profiles;
        try { profiles = Directory.EnumerateDirectories(root).ToList(); }
        catch { yield break; }
        foreach (var p in profiles) yield return Path.Combine(p, "cache2");
    }

    // ------------------------------------------------------------ scan / clean

    public Task ScanAsync(IEnumerable<JunkCategory> categories, CancellationToken ct = default)
        => Task.Run(() =>
        {
            Parallel.ForEach(categories.ToList(), new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 }, c =>
            {
                long size = 0, count = 0;
                if (c.IsRecycleBin)
                {
                    (size, count) = QueryRecycleBin();
                }
                else if (c.Files is not null)
                {
                    foreach (var f in c.Files())
                    {
                        ct.ThrowIfCancellationRequested();
                        size += f.Length;
                        count++;
                    }
                }
                c.Size = size;
                c.Count = count;
                c.Scanned = true;
            });
        }, ct);

    public Task<CleanResult> CleanAsync(IEnumerable<JunkCategory> categories, IProgress<string>? progress = null, CancellationToken ct = default)
        => Task.Run(() =>
        {
            long freed = 0, deleted = 0, skipped = 0;
            foreach (var c in categories.ToList())
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(c.Name);

                if (c.IsRecycleBin)
                {
                    var (size, count) = QueryRecycleBin();
                    var hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
                        NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
                    if (hr >= 0) { freed += size; deleted += count; }
                    else skipped += count;
                }
                else if (c.Files is not null)
                {
                    foreach (var f in c.Files().ToList())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (Fs.TryDelete(f.Path)) { freed += f.Length; deleted++; }
                        else skipped++;
                    }
                    if (c.Roots is not null)
                        foreach (var r in c.Roots()) Fs.PruneEmptyDirs(r);
                }
                c.Scanned = false;
            }
            return new CleanResult(freed, deleted, skipped);
        }, ct);

    private static (long Size, long Count) QueryRecycleBin()
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        return NativeMethods.SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }
}
