using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed record SizeOption(string Text, long Bytes);

public sealed class LargeFileViewModel : ObservableObject
{
    private readonly LargeFileService _svc = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<LargeFileInfo> Results { get; } = new();
    public IReadOnlyList<string> Locations { get; }
    public IReadOnlyList<SizeOption> MinSizes { get; } = new[]
    {
        new SizeOption("50 MB 이상", 50L << 20),
        new SizeOption("100 MB 이상", 100L << 20),
        new SizeOption("500 MB 이상", 500L << 20),
        new SizeOption("1 GB 이상", 1L << 30),
    };

    private string _searchPath;
    public string SearchPath { get => _searchPath; set => Set(ref _searchPath, value); }

    private SizeOption _selectedMin;
    public SizeOption SelectedMin { get => _selectedMin; set => Set(ref _selectedMin, value); }

    private bool _useRecycleBin = true;
    public bool UseRecycleBin { get => _useRecycleBin; set => Set(ref _useRecycleBin, value); }

    private bool _isScanning;
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }

    private string _status = "폴더나 드라이브를 선택하고 ‘검색’을 누르세요.";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public ICommand BrowseCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenLocationCommand { get; }

    public LargeFileViewModel()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var locs = new List<string> { profile };
        try { locs.AddRange(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name)); }
        catch { }
        Locations = locs;
        _searchPath = profile;
        _selectedMin = MinSizes[1];

        static bool Has(object? p) => p is IList { Count: > 0 };
        BrowseCommand = new RelayCommand(_ => Browse(), _ => !IsScanning);
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !IsScanning);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsScanning);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, p => Has(p) && !IsScanning);
        OpenLocationCommand = new RelayCommand(p =>
        {
            foreach (var f in ((IList)p!).Cast<LargeFileInfo>().Take(5)) Dialogs.RevealInExplorer(f.Path);
        }, Has);
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "검색할 폴더 선택" };
        if (Directory.Exists(SearchPath)) dlg.InitialDirectory = SearchPath;
        if (dlg.ShowDialog() == true) SearchPath = dlg.FolderName;
    }

    private async Task ScanAsync()
    {
        var root = SearchPath.Trim().Trim('"');
        if (!Directory.Exists(root)) { Dialogs.Error("존재하지 않는 경로입니다."); return; }

        _cts = new CancellationTokenSource();
        IsScanning = true;
        Results.Clear();
        var progress = new Progress<string>(p => Status = $"검색 중… {p}");
        var min = SelectedMin.Bytes;
        var ct = _cts.Token;

        try
        {
            var found = await Task.Run(() => _svc.Scan(new[] { root }, min, 500, progress, ct));
            foreach (var f in found) Results.Add(f);
            Status = (ct.IsCancellationRequested ? "검색 중지됨 · " : "검색 완료 · ") +
                     $"{found.Count}개 파일 · 합계 {SizeFormat.ToText(found.Sum(f => f.Size))}";
        }
        finally
        {
            IsScanning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task DeleteAsync(object? p)
    {
        var items = ((IList)p!).Cast<LargeFileInfo>().ToList();
        long size = items.Sum(i => i.Size);
        var how = UseRecycleBin ? "휴지통으로 이동" : "영구 삭제";
        if (!Dialogs.Confirm($"선택한 {items.Count}개 파일({SizeFormat.ToText(size)})을 {how}할까요?" +
                             (UseRecycleBin ? "" : "\n\n영구 삭제한 파일은 복구할 수 없습니다.")))
            return;

        var removed = new List<LargeFileInfo>();
        var failed = new List<string>();
        bool recycle = UseRecycleBin;
        await Task.Run(() =>
        {
            foreach (var f in items)
            {
                try
                {
                    if (LargeFileService.IsProtected(f.Path)) throw new InvalidOperationException("Windows 폴더의 파일은 삭제할 수 없습니다.");
                    if (recycle) Fs.DeleteToRecycleBin(f.Path);
                    else File.Delete(f.Path);
                    removed.Add(f);
                }
                catch (Exception ex) { failed.Add($"{f.Name}: {ex.Message}"); }
            }
        });

        foreach (var r in removed) Results.Remove(r);
        Status = $"{removed.Count}개 파일 삭제 · {SizeFormat.ToText(removed.Sum(r => r.Size))} 확보";
        if (failed.Count > 0) Dialogs.Error("삭제하지 못한 파일:\n\n" + string.Join("\n", failed.Take(10)));
    }
}
