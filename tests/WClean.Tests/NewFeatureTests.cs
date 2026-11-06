using Xunit;
using Xunit.Abstractions;

namespace WClean.Tests;

public class TreemapTests
{
    [Fact]
    public void Layout_FillsAreaAndStaysInside()
    {
        var sizes = new long[] { 600, 300, 200, 100, 50, 25 };
        var rects = DiskUsageService.Layout(sizes, 800, 400);
        Assert.Equal(sizes.Length, rects.Count);
        Assert.Equal(800 * 400, rects.Sum(r => r.W * r.H), 3);
        Assert.All(rects, r =>
        {
            Assert.True(r.X >= -0.001 && r.Y >= -0.001);
            Assert.True(r.X + r.W <= 800.001 && r.Y + r.H <= 400.001);
        });
        // area proportional to size
        double total = sizes.Sum();
        foreach (var r in rects) Assert.Equal(sizes[r.Index] / total * 800 * 400, r.W * r.H, 3);
    }

    [Fact]
    public void Layout_EmptyOrZero_ReturnsNothing()
    {
        Assert.Empty(DiskUsageService.Layout(Array.Empty<long>(), 100, 100));
        Assert.Empty(DiskUsageService.Layout(new long[] { 0, 0 }, 100, 100));
    }
}

public class DuplicateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wclean-dup-" + Guid.NewGuid().ToString("N"));
    public DuplicateTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string name, byte[] data)
    {
        var p = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, data);
        return p;
    }

    [Fact]
    public void FindsIdenticalFiles_AndIgnoresSameSizeDifferentContent()
    {
        var a = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        var b = (byte[])a.Clone(); b[^1] ^= 0xFF;             // same size, different last byte
        Write("one.bin", a);
        Write(@"sub\copy.bin", a);
        Write("other.bin", b);
        Write("small.txt", new byte[] { 1, 2, 3 });

        var found = new DuplicateService().Scan(new[] { _dir }, 1000);
        Assert.Equal(2, found.Count);
        Assert.Equal(2, found.Select(f => Path.GetFileName(f.Path)).Intersect(new[] { "one.bin", "copy.bin" }).Count());
        Assert.Single(found.Select(f => f.Group).Distinct());
    }

    [Fact]
    public void SelectAllButOne_KeepsOnePerGroup()
    {
        var a = new byte[4000]; new Random(1).NextBytes(a);
        var c = new byte[4000]; new Random(2).NextBytes(c);
        Write("a1", a); Write("a2", a); Write("a3", a);
        Write("c1", c); Write("c2", c);

        var found = new DuplicateService().Scan(new[] { _dir }, 1000);
        DuplicateService.SelectAllButOne(found);
        foreach (var g in found.GroupBy(f => f.Group))
            Assert.Equal(1, g.Count(f => !f.IsChecked));
        Assert.Equal(3, found.Count(f => f.IsChecked));
    }
}

public class TidyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wclean-tidy-" + Guid.NewGuid().ToString("N"));
    public TidyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void EmptyFolders_ReportsTopMostEmptyOnly()
    {
        Directory.CreateDirectory(Path.Combine(_dir, @"empty\inner\deeper"));   // all empty -> report "empty"
        Directory.CreateDirectory(Path.Combine(_dir, @"keep\sub"));
        File.WriteAllText(Path.Combine(_dir, @"keep\file.txt"), "x");           // keep has a file; keep\sub is empty
        Directory.CreateDirectory(Path.Combine(_dir, @".hidden\x"));            // dot-folders are skipped

        var found = new TidyService().FindEmptyFolders(_dir).Select(i => Path.GetRelativePath(_dir, i.Path)).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "empty", @"keep\sub" }, found);
    }

    [Fact]
    public void Delete_RefusesFolderThatGotAFile()
    {
        var d = Path.Combine(_dir, "e");
        Directory.CreateDirectory(d);
        var item = new TidyItem { Kind = TidyKind.EmptyFolder, Path = d };
        File.WriteAllText(Path.Combine(d, "late.txt"), "x");
        Assert.Throws<IOException>(() => new TidyService().Delete(item));
        Assert.True(File.Exists(Path.Combine(d, "late.txt")));
    }
}

public class MiscTests
{
    private readonly ITestOutputHelper _out;
    public MiscTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(200, "낮음")]
    [InlineData(1500, "보통")]
    [InlineData(8000, "높음")]
    public void ImpactText_Levels(long ms, string level) => Assert.StartsWith(level, BootImpactService.ImpactText(ms));

    [Fact]
    public void JunkCategories_HaveNewEntries_AndDevCachesStartUnchecked()
    {
        var cats = new JunkService().CreateCategories();
        Assert.Contains(cats, c => c.Name == "메신저·앱 캐시");
        Assert.Contains(cats, c => c.Name == "배달 최적화 캐시");
        Assert.False(cats.Single(c => c.Name == "개발 도구 캐시").IsChecked);
    }

    [Fact]
    public void Programs_Load_ListsInstalledPrograms()
    {
        var list = new ProgramService().Load();
        foreach (var p in list.Take(10)) _out.WriteLine($"{p.Name} | {p.Version} | {p.SizeText} | {p.DateText}");
        Assert.NotEmpty(list);
        Assert.All(list, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }

    [Fact]
    public void Services_Load_FindsThirdPartyAndMicrosoft()
    {
        var list = new WindowsServiceManager().Load();
        _out.WriteLine($"total {list.Count}, non-MS {list.Count(s => !s.IsMicrosoft)}");
        Assert.NotEmpty(list);
        Assert.Contains(list, s => s.IsMicrosoft);
    }

    [Fact]
    public void Backups_Load_DoesNotThrow() => Assert.NotNull(new BackupService().Load());

    [Fact]
    public void LeftoverFolder_RefusesBroadPaths()
    {
        var p = new InstalledProgram { InstallLocation = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
        Assert.Null(ProgramService.LeftoverFolder(p));
        Assert.Null(ProgramService.LeftoverFolder(new InstalledProgram { InstallLocation = @"C:\" }));
    }
}
