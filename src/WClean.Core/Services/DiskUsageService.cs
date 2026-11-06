using System.IO.Enumeration;

namespace WClean;

public sealed record DiskItem(string Name, string Path, long Size, bool IsDirectory)
{
    public string SizeText => SizeFormat.ToText(Size);
}

public readonly record struct TreemapRect(double X, double Y, double W, double H, int Index);

public sealed class DiskUsageService
{
    /// <summary>Sizes of the direct children of <paramref name="folder"/> (folders are summed recursively).</summary>
    public List<DiskItem> Analyze(string folder, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var items = new List<DiskItem>();
        var dirs = new List<string>();
        try
        {
            foreach (var f in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (f is FileInfo fi) items.Add(new DiskItem(fi.Name, fi.FullName, fi.Length, false));
                else dirs.Add(f.FullName);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        var sums = new System.Collections.Concurrent.ConcurrentBag<DiskItem>();
        int done = 0;
        Parallel.ForEach(dirs, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 }, d =>
        {
            long sum = 0;
            foreach (var e in Fs.EnumerateFiles(d)) { ct.ThrowIfCancellationRequested(); sum += e.Length; }
            sums.Add(new DiskItem(System.IO.Path.GetFileName(d), d, sum, true));
            progress?.Report($"{Interlocked.Increment(ref done)}/{dirs.Count} 폴더 계산 완료");
        });
        items.AddRange(sums);
        return items.Where(i => i.Size > 0).OrderByDescending(i => i.Size).ToList();
    }

    /// <summary>Squarified treemap: lays out <paramref name="sizes"/> (descending) inside a w x h rectangle.</summary>
    public static List<TreemapRect> Layout(IReadOnlyList<long> sizes, double w, double h)
    {
        var result = new List<TreemapRect>();
        double total = sizes.Where(s => s > 0).Sum(s => (double)s);
        if (total <= 0 || w <= 0 || h <= 0) return result;

        double scale = w * h / total;
        var areas = sizes.Select(s => s * scale).ToList();
        double x = 0, y = 0, rw = w, rh = h;
        int i = 0;
        while (i < areas.Count)
        {
            bool horizontal = rw >= rh;          // fill a column along the shorter side
            double side = horizontal ? rh : rw;
            int start = i;
            double rowSum = 0, best = double.MaxValue;
            int end = i;
            while (end < areas.Count)
            {
                double s = rowSum + areas[end];
                double worst = Worst(areas, start, end, s, side);
                if (worst > best) break;
                best = worst; rowSum = s; end++;
            }
            if (end == start) { end = start + 1; rowSum = areas[start]; }

            double thick = rowSum / side;
            double pos = 0;
            for (int k = start; k < end; k++)
            {
                double len = areas[k] / thick;
                result.Add(horizontal
                    ? new TreemapRect(x, y + pos, thick, len, k)
                    : new TreemapRect(x + pos, y, len, thick, k));
                pos += len;
            }
            if (horizontal) { x += thick; rw -= thick; } else { y += thick; rh -= thick; }
            i = end;
        }
        return result;
    }

    private static double Worst(List<double> a, int start, int end, double sum, double side)
    {
        double max = 0, min = double.MaxValue;
        for (int k = start; k <= end; k++) { max = Math.Max(max, a[k]); min = Math.Min(min, a[k]); }
        double s2 = side * side, sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }
}
