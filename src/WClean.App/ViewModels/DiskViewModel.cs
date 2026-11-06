using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class DiskViewModel : PageViewModel
{
    private readonly DiskUsageService _svc = new();
    private CancellationTokenSource? _cts;
    private readonly Stack<string> _history = new();

    public override string Title => "디스크 사용량";
    public override string Description => "폴더별 용량을 크기에 비례한 블록으로 보여 줍니다. 블록을 두 번 클릭하면 그 폴더 안으로 들어갑니다.";
    public override string Glyph => "";

    public ObservableCollection<DiskItem> Items { get; } = new();
    public IReadOnlyList<string> Locations { get; }

    private string _path;
    public string CurrentPath { get => _path; set => Set(ref _path, value); }

    private bool _isScanning;
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }

    private string _totalText = "—";
    public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }
    private string _biggestText = "—";
    public string BiggestText { get => _biggestText; private set => Set(ref _biggestText, value); }
    private string _driveText = "—";
    public string DriveText { get => _driveText; private set => Set(ref _driveText, value); }

    public ICommand BrowseCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand RevealCommand { get; }

    public DiskViewModel()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var locs = new List<string> { profile };
        try { locs.AddRange(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name)); }
        catch { }
        Locations = locs;
        _path = profile;
        Status = "폴더나 드라이브를 선택하고 ‘분석’을 누르세요.";

        BrowseCommand = new RelayCommand(_ => Browse(), _ => !IsScanning);
        ScanCommand = new AsyncRelayCommand(_ => AnalyzeAsync(CurrentPath, push: false), _ => !IsScanning);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsScanning);
        UpCommand = new AsyncRelayCommand(_ => UpAsync(), _ => !IsScanning && (_history.Count > 0 || Parent(CurrentPath) is not null));
        OpenCommand = new AsyncRelayCommand(p => p is DiskItem { IsDirectory: true } d ? AnalyzeAsync(d.Path, push: true) : Task.CompletedTask,
            p => !IsScanning);
        RevealCommand = new RelayCommand(p => { if (p is DiskItem d) Dialogs.RevealInExplorer(d.Path); });
    }

    private static string? Parent(string path)
    {
        try { return Path.GetDirectoryName(path.TrimEnd('\\')); }
        catch { return null; }
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "분석할 폴더 선택" };
        if (Directory.Exists(CurrentPath)) dlg.InitialDirectory = CurrentPath;
        if (dlg.ShowDialog() == true) CurrentPath = dlg.FolderName;
    }

    private Task UpAsync()
    {
        var target = _history.Count > 0 ? _history.Pop() : Parent(CurrentPath);
        return target is null ? Task.CompletedTask : AnalyzeAsync(target, push: false, fromUp: true);
    }

    private Task AnalyzeAsync(string path, bool push, bool fromUp = false) => BusyAsync(async () =>
    {
        path = path.Trim().Trim('"');
        if (!Directory.Exists(path)) { Dialogs.Error("존재하지 않는 경로입니다."); return; }
        if (push && Directory.Exists(CurrentPath)) _history.Push(CurrentPath);
        if (!push && !fromUp) _history.Clear();

        _cts = new CancellationTokenSource();
        IsScanning = true;
        CurrentPath = path;
        var progress = new Progress<string>(p => Status = $"분석 중… {p}");
        var ct = _cts.Token;
        try
        {
            var items = await Task.Run(() => _svc.Analyze(path, progress, ct), ct);
            Items.Clear();
            foreach (var i in items.Take(200)) Items.Add(i);
            long total = items.Sum(i => i.Size);
            TotalText = SizeFormat.ToText(total);
            BiggestText = items.Count > 0 ? items[0].Name : "—";
            try
            {
                var root = Path.GetPathRoot(path);
                var di = new DriveInfo(root!);
                DriveText = SizeFormat.ToText(di.AvailableFreeSpace);
            }
            catch { DriveText = "—"; }
            Status = $"분석 완료 · {path}";
        }
        catch (OperationCanceledException) { Status = "분석을 중지했습니다."; }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    });
}
