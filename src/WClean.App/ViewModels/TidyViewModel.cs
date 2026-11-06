using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class TidyViewModel : PageViewModel
{
    private readonly TidyService _svc = new();
    private CancellationTokenSource? _cts;

    public override string Title => "빈 폴더·바로가기";
    public override string Description => "대상이 사라진 깨진 바로가기와 내용이 없는 빈 폴더를 찾아 정리합니다. 바로가기는 휴지통으로 이동합니다.";
    public override string Glyph => "";

    public ObservableCollection<TidyItem> Items { get; } = new();
    public IReadOnlyList<string> Locations { get; }

    private string _searchPath;
    public string SearchPath { get => _searchPath; set => Set(ref _searchPath, value); }

    private bool _scanShortcuts = true;
    public bool ScanShortcuts { get => _scanShortcuts; set => Set(ref _scanShortcuts, value); }

    private bool _scanFolders = true;
    public bool ScanFolders { get => _scanFolders; set => Set(ref _scanFolders, value); }

    private bool _isScanning;
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }

    public int ShortcutCount => Items.Count(i => i.Kind == TidyKind.BrokenShortcut);
    public int FolderCount => Items.Count(i => i.Kind == TidyKind.EmptyFolder);
    public int CheckedCount => Items.Count(i => i.IsChecked);

    public ICommand BrowseCommand { get; }
    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand OpenLocationCommand { get; }

    public TidyViewModel()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var locs = new List<string>();
        foreach (var f in new[] { "Documents", "Downloads", "Desktop", "Pictures" })
        {
            var p = Path.Combine(profile, f);
            if (Directory.Exists(p)) locs.Add(p);
        }
        locs.Add(profile);
        Locations = locs;
        _searchPath = locs[0];
        Status = "‘검사’를 눌러 정리할 항목을 찾아보세요.";

        BrowseCommand = new RelayCommand(_ => Browse(), _ => !IsScanning);
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !IsScanning);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsScanning);
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => Items.Any(i => i.IsChecked) && !IsScanning);
        SelectAllCommand = new RelayCommand(_ => { foreach (var i in Items) i.IsChecked = true; Refresh(); });
        SelectNoneCommand = new RelayCommand(_ => { foreach (var i in Items) i.IsChecked = false; Refresh(); });
        OpenLocationCommand = new RelayCommand(p => { if (p is TidyItem i) Dialogs.RevealInExplorer(i.Path); }, p => p is TidyItem);
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "빈 폴더를 찾을 위치 선택" };
        if (Directory.Exists(SearchPath)) dlg.InitialDirectory = SearchPath;
        if (dlg.ShowDialog() == true) SearchPath = dlg.FolderName;
    }

    private void Refresh()
    {
        Raise(nameof(ShortcutCount)); Raise(nameof(FolderCount)); Raise(nameof(CheckedCount));
        Badge = Items.Count > 0 ? Items.Count.ToString() : "";
    }

    private Task ScanAsync() => BusyAsync(async () =>
    {
        var root = SearchPath.Trim().Trim('"');
        if (ScanFolders && !Directory.Exists(root)) { Dialogs.Error("존재하지 않는 경로입니다."); return; }

        _cts = new CancellationTokenSource();
        IsScanning = true;
        Items.Clear();
        Refresh();
        var progress = new Progress<string>(p => Status = $"검사 중… {p}");
        var ct = _cts.Token;
        try
        {
            var found = await Task.Run(() =>
            {
                var l = new List<TidyItem>();
                if (ScanShortcuts) l.AddRange(_svc.FindBrokenShortcuts(ct));
                if (ScanFolders) l.AddRange(_svc.FindEmptyFolders(root, progress, ct));
                return l;
            }, ct);
            foreach (var i in found)
            {
                i.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TidyItem.IsChecked)) Refresh(); };
                Items.Add(i);
            }
            Refresh();
            Status = found.Count == 0 ? "정리할 항목이 없습니다." : $"검사 완료 · 깨진 바로가기 {ShortcutCount}개, 빈 폴더 {FolderCount}개";
        }
        catch (OperationCanceledException) { Status = "검사를 중지했습니다."; }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    });

    private async Task DeleteAsync()
    {
        var targets = Items.Where(i => i.IsChecked).ToList();
        if (!Dialogs.Confirm($"선택한 {targets.Count}개 항목을 정리할까요?\n(깨진 바로가기는 휴지통으로 이동, 빈 폴더는 삭제)")) return;

        var removed = new List<TidyItem>();
        var failed = new List<string>();
        await Task.Run(() =>
        {
            foreach (var i in targets)
            {
                try { _svc.Delete(i); removed.Add(i); }
                catch (Exception ex) { failed.Add($"{i.Name}: {ex.Message}"); }
            }
        });
        foreach (var r in removed) Items.Remove(r);
        Refresh();
        if (removed.Count > 0) HistoryService.Add("빈 폴더·바로가기 정리", 0, removed.Count);
        Status = $"{removed.Count}개 정리 완료";
        if (failed.Count > 0) Dialogs.Error("정리하지 못한 항목:\n\n" + string.Join("\n", failed.Take(10)));
    }
}
