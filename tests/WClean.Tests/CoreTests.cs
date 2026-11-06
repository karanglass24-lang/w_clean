using Microsoft.Win32;
using Xunit;
using Xunit.Abstractions;

namespace WClean.Tests;

public class PathHelperTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\A B\\x.exe\" -min", "C:\\Program Files\\A B\\x.exe")]
    [InlineData("C:\\Program Files\\A B\\x.exe -min", "C:\\Program Files\\A B\\x.exe")]
    [InlineData("rundll32.exe shell32.dll,Foo", "rundll32.exe")]
    public void ExtractExecutable(string cmd, string expected)
        => Assert.Equal(expected, PathHelper.ExtractExecutable(cmd));

    [Fact]
    public void MissingFile_IsMissing() => Assert.True(PathHelper.IsMissing("\"C:\\definitely\\not\\here\\x.exe\" /a"));

    [Fact]
    public void ExistingFile_IsNotMissing()
        => Assert.False(PathHelper.IsMissing(Path.Combine(Environment.SystemDirectory, "cmd.exe") + " /c"));

    [Fact]
    public void RelativeAndUnc_AreNeverMissing()
    {
        Assert.False(PathHelper.IsMissing("msiexec.exe /x {1234}"));
        Assert.False(PathHelper.IsMissing("\\\\server\\share\\x.exe"));
    }
}

public class RegBackupTests
{
    [Fact]
    public void FormatValue_String_EscapesBackslashAndQuote()
        => Assert.Equal("\"a\"=\"C:\\\\x \\\"q\\\"\"",
            RegBackupWriter.FormatValue("a", "C:\\x \"q\"", RegistryValueKind.String));

    [Fact]
    public void FormatValue_DefaultValueUsesAt()
        => Assert.StartsWith("@=", RegBackupWriter.FormatValue("", "x", RegistryValueKind.String));

    [Fact]
    public void FormatValue_DwordAndExpand()
    {
        Assert.Equal("\"d\"=dword:0000002a", RegBackupWriter.FormatValue("d", 42, RegistryValueKind.DWord));
        Assert.Equal("\"e\"=hex(2):25,00,00,00", RegBackupWriter.FormatValue("e", "%", RegistryValueKind.ExpandString));
    }

    [Fact]
    public void RoundTrip_ValueBackupContainsKeyAndValue()
    {
        const string sub = @"Software\WCleanTest_Backup";
        using (var k = Registry.CurrentUser.CreateSubKey(sub))
            k.SetValue("v", "hello");
        try
        {
            var w = new RegBackupWriter();
            w.AddValue(new RegLocation(RegistryHive.CurrentUser, RegistryView.Registry64, sub), "v");
            var text = w.ToString();
            Assert.Contains("[HKEY_CURRENT_USER\\Software\\WCleanTest_Backup]", text);
            Assert.Contains("\"v\"=\"hello\"", text);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(sub, false); }
    }

    [Fact]
    public void WowPathIsUsedForRegFile()
    {
        var loc = new RegLocation(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Foo");
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Foo", loc.RegFilePath);
    }
}

public class RegistryCleanerTests
{
    [Fact]
    public void Fix_DeletesValueAndKey_AndWritesBackup()
    {
        const string sub = @"Software\WCleanTest_Fix";
        using (var k = Registry.CurrentUser.CreateSubKey(sub))
        {
            k.SetValue("gone", @"C:\nope\x.exe");
            k.CreateSubKey("child").SetValue("a", 1);
        }
        try
        {
            var parent = new RegLocation(RegistryHive.CurrentUser, RegistryView.Registry64, sub);
            var parentOfParent = new RegLocation(RegistryHive.CurrentUser, RegistryView.Registry64, "Software");
            var issues = new[]
            {
                new RegistryIssue { Name = "v", Parent = parent, ValueName = "gone" },
                new RegistryIssue { Name = "k", Parent = parent, KeyName = "child" },
            };

            var result = new RegistryCleaner().Fix(issues);

            Assert.Equal(2, result.Fixed);
            Assert.Equal(0, result.Failed);
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath));
            Assert.Contains("gone", File.ReadAllText(result.BackupPath!));
            using var after = Registry.CurrentUser.OpenSubKey(sub)!;
            Assert.Null(after.GetValue("gone"));
            Assert.Null(after.OpenSubKey("child"));
            File.Delete(result.BackupPath!);
            _ = parentOfParent;
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(sub, false); }
    }
}

public class FileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WCleanTest_" + Guid.NewGuid().ToString("N"));

    public FileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Make(string rel, int bytes)
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[bytes]);
        return p;
    }

    [Fact]
    public void LargeFileScan_FiltersBySizeAndSorts()
    {
        Make("a.bin", 3_000_000);
        Make(@"sub\b.bin", 5_000_000);
        Make("small.bin", 10);

        var r = new LargeFileService().Scan(new[] { _dir }, 2_000_000, 10);

        Assert.Equal(2, r.Count);
        Assert.Equal("b.bin", r[0].Name);
        Assert.Equal("a.bin", r[1].Name);
    }

    [Fact]
    public void LargeFileScan_RespectsMaxResults()
    {
        for (int i = 0; i < 5; i++) Make($"f{i}.bin", 1_000_000 + i);
        Assert.Equal(3, new LargeFileService().Scan(new[] { _dir }, 1, 3).Count);
    }

    [Fact]
    public void LargeFileScan_Cancelled_ReturnsPartialWithoutThrowing()
    {
        Make("a.bin", 1000);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = new LargeFileService().Scan(new[] { _dir }, 1, 10, null, cts.Token);
        Assert.NotNull(r);
    }

    [Fact]
    public void WindowsFolder_IsProtected()
        => Assert.True(LargeFileService.IsProtected(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "x.dll")));

    [Fact]
    public async Task JunkCategory_ScanThenClean_DeletesFilesAndPrunesDirs()
    {
        Make("a.tmp", 1000);
        Make(@"d1\d2\b.tmp", 2000);
        var cat = new JunkCategory("test", "", () => Fs.EnumerateFiles(_dir), () => new[] { _dir });
        var svc = new JunkService();

        await svc.ScanAsync(new[] { cat });
        Assert.Equal(3000, cat.Size);
        Assert.Equal(2, cat.Count);

        var result = await svc.CleanAsync(new[] { cat });

        Assert.Equal(3000, result.FreedBytes);
        Assert.Equal(2, result.Deleted);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void TryDelete_RemovesReadOnlyFile()
    {
        var p = Make("ro.tmp", 5);
        File.SetAttributes(p, FileAttributes.ReadOnly);
        Assert.True(Fs.TryDelete(p));
        Assert.False(File.Exists(p));
    }
}

public class SizeFormatTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1L << 30, "1 GB")]
    public void ToText(long b, string expected) => Assert.Equal(expected, SizeFormat.ToText(b));
}

/// <summary>Read-only look at this machine; prints findings so false positives are easy to spot.</summary>
public class RealSystemSmokeTests
{
    private readonly ITestOutputHelper _out;
    public RealSystemSmokeTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void StartupService_Load_ListsEntries()
    {
        var list = new StartupService().Load();
        foreach (var e in list)
            _out.WriteLine($"[{(e.IsEnabled ? "on " : "off")}] {e.Name} | {e.Source} | missing={e.TargetMissing} | {e.Command}");
        Assert.NotNull(list);
    }

    [Fact]
    public void RegistryCleaner_Scan_ListsIssues()
    {
        var issues = new RegistryCleaner().Scan();
        foreach (var i in issues)
            _out.WriteLine($"{i.Category} | {i.Name} | {i.Problem} | {i.KeyPath}");
        _out.WriteLine($"total {issues.Count}");
        Assert.NotNull(issues);
    }

    [Fact]
    public async Task Junk_Scan_ReportsSizes()
    {
        var svc = new JunkService();
        var cats = svc.CreateCategories();
        await svc.ScanAsync(cats);
        foreach (var c in cats) _out.WriteLine($"{c.Name}: {c.SizeText}");
        Assert.All(cats, c => Assert.True(c.Scanned));
    }
}
