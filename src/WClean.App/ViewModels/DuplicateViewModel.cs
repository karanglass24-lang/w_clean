using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class DuplicateViewModel : PageViewModel
{
    private readonly DuplicateService _svc = new();
    private CancellationTokenSource? _cts;

    public override string Title => "중복파일 찾기";
    public override string Description => "내용이 완전히 같은 파일을 찾아 한 개만 남기고 정리합니다. 삭제는 휴지통으로 이동합니다.";
    public override string Glyph => "";

    public ObservableCollection<DuplicateFile> Results { get; } = new();
    public IReadOnlyList<string> Locations { get; }
    public IReadOnlyList<SizeOption> MinSizes { get; } = new[]
    {
        new SizeOption("1 MB 이상", 1L << 20),
        new SizeOption("10 MB 이상", 10L << 20),
        new SizeOption("100 MB 이상", 100L << 20),
    };

    private string _searchPath;
    public string SearchPath { get => _searchPath; set => Set(ref _searchPath, value); }

    private SizeOption _selectedMin;
    public SizeOption SelectedMin { get => _selectedMin; set => Set(ref _selectedMin, value); }

    private bool _isScanning;
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }

    private string _groupsText = "—";
    public string GroupsText { get => _groupsText; private set => Set(ref _groupsText, value); }
    private string _wastedText = "—";
    public string WastedText { get => _wastedText; private set => Set(ref _wastedText, value); }
    private string _selectedText = "—";
    public string SelectedText { get => _selectedText; private set => Set(ref _selectedText, value); }

    public ICommand BrowseCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AutoSelectCommand { get; }
    public ICommand ClearSelectCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenLocationCommand { get; }

    public DuplicateViewModel()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var locs = new List<string> { profile };
        try { locs.AddRange(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name)); }
        catch { }
        Locations = locs;
        _searchPath = Path.Combine(profile, "Downloads");
        if (!Directory.Exists(_searchPath)) _searchPath = profile;
        _selectedMin = MinSizes[0];
        Status = "폴더를 고르고 ‘검색’을 누르세요.";

        BrowseCommand = new RelayCommand(_ => Browse(), _ => !IsScanning);
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !IsScanning);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsScanning);
        AutoSelectCommand = new RelayCommand(_ => { DuplicateService.SelectAllButOne(Results); Update(); }, _ => Results.Count > 0);
        ClearSelectCommand = new RelayCommand(_ => { foreach (var r in Results) r.IsChecked = false; Update(); }, _ => Results.Count > 0);
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => Results.Any(r => r.IsChecked) && !IsScanning);
        OpenLocationCommand = new RelayCommand(p => { if (p is DuplicateFile f) Dialogs.RevealInExplorer(f.Path); }, p => p is DuplicateFile);
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "검색할 폴더 선택" };
        if (Directory.Exists(SearchPath)) dlg.InitialDirectory = SearchPath;
        if (dlg.ShowDialog() == true) SearchPath = dlg.FolderName;
    }

    private void Hook(DuplicateFile f) => f.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DuplicateFile.IsChecked)) Update(); };

    private void Update()
    {
        var groups = Results.Select(r => r.Group).Distinct().Count();
        GroupsText = Results.Count == 0 ? "—" : $"{groups}묶음";
        long wasted = Results.GroupBy(r => r.Group).Sum(g => g.First().Size * (g.Count() - 1));
        WastedText = Results.Count == 0 ? "—" : SizeFormat.ToText(wasted);
        SelectedText = Results.Count == 0 ? "—" : SizeFormat.ToText(Results.Where(r => r.IsChecked).Sum(r => r.Size));
        Badge = groups > 0 ? groups.ToString() : "";
    }

    private Task ScanAsync() => BusyAsync(async () =>
    {
        var root = SearchPath.Trim().Trim('"');
        if (!Directory.Exists(root)) { Dialogs.Error("존재하지 않는 경로입니다."); return; }

        _cts = new CancellationTokenSource();
        IsScanning = true;
        Results.Clear();
        Update();
        var progress = new Progress<string>(p => Status = p);
        var min = SelectedMin.Bytes;
        var ct = _cts.Token;
        try
        {
            var found = await Task.Run(() => _svc.Scan(new[] { root }, min, progress, ct));
            foreach (var f in found) { Hook(f); Results.Add(f); }
            Update();
            Status = found.Count == 0 ? "중복 파일이 없습니다." : $"검색 완료 · {GroupsText}에서 {WastedText}을(를) 줄일 수 있습니다. ‘중복 자동 선택’을 눌러 보세요.";
        }
        catch (OperationCanceledException) { Status = "검색을 중지했습니다."; }
        finally
        {
            IsScanning = false;
            _cts.Dispose();
            _cts = null;
        }
    });

    private async Task DeleteAsync()
    {
        var items = Results.Where(r => r.IsChecked).ToList();
        // never delete every copy
        var wipedGroups = items.GroupBy(i => i.Group).Where(g => g.Count() == Results.Count(r => r.Group == g.Key)).ToList();
        if (wipedGroups.Count > 0)
        {
            Dialogs.Error($"{wipedGroups.Count}개 묶음에서 모든 사본이 선택되어 있습니다.\n각 묶음에서 최소 한 개는 남겨야 합니다.");
            return;
        }
        long size = items.Sum(i => i.Size);
        if (!Dialogs.Confirm($"선택한 {items.Count}개 파일({SizeFormat.ToText(size)})을 휴지통으로 이동할까요?")) return;

        var removed = new List<DuplicateFile>();
        var failed = new List<string>();
        await Task.Run(() =>
        {
            foreach (var f in items)
            {
                try { Fs.DeleteToRecycleBin(f.Path); removed.Add(f); }
                catch (Exception ex) { failed.Add($"{f.Name}: {ex.Message}"); }
            }
        });
        foreach (var r in removed) Results.Remove(r);
        // drop groups that no longer have duplicates
        foreach (var g in Results.GroupBy(r => r.Group).Where(g => g.Count() < 2).ToList())
            foreach (var r in g.ToList()) Results.Remove(r);
        Update();
        long freed = removed.Sum(r => r.Size);
        if (removed.Count > 0) HistoryService.Add("중복파일 삭제", freed, removed.Count);
        Status = $"{removed.Count}개 파일 삭제 · {SizeFormat.ToText(freed)} 확보";
        if (failed.Count > 0) Dialogs.Error("삭제하지 못한 파일:\n\n" + string.Join("\n", failed.Take(10)));
    }
}
