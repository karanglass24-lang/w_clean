using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class ProgramsViewModel : PageViewModel
{
    private readonly ProgramService _svc = new();
    private List<InstalledProgram> _all = new();

    public override string Title => "프로그램 제거";
    public override string Description => "설치된 프로그램을 용량·설치일 순으로 보고 제거합니다. 제거 후 남은 설치 폴더도 함께 정리할 수 있습니다.";
    public override string Glyph => "";

    public ObservableCollection<InstalledProgram> Programs { get; } = new();
    public IReadOnlyList<string> SortOptions { get; } = new[] { "이름순", "용량 큰 순", "최근 설치 순" };

    private string _sort = "용량 큰 순";
    public string Sort { get => _sort; set { if (Set(ref _sort, value)) Apply(); } }

    private string _filter = "";
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) Apply(); } }

    private string _totalText = "—";
    public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }
    private string _sizeText = "—";
    public string SizeTotalText { get => _sizeText; private set => Set(ref _sizeText, value); }
    private string _recentText = "—";
    public string RecentText { get => _recentText; private set => Set(ref _recentText, value); }

    public ICommand RefreshCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand OpenLocationCommand { get; }

    public ProgramsViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync());
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, p => p is InstalledProgram);
        OpenLocationCommand = new RelayCommand(p => { if (p is InstalledProgram { InstallLocation: { } l }) Dialogs.RevealInExplorer(l); },
            p => p is InstalledProgram { InstallLocation: not null });
    }

    public Task LoadAsync() => BusyAsync(async () =>
    {
        Status = "설치된 프로그램을 불러오는 중…";
        _all = await Task.Run(_svc.Load);
        Apply();
        TotalText = _all.Count.ToString();
        SizeTotalText = SizeFormat.ToText(_all.Sum(p => p.SizeBytes));
        RecentText = _all.Count(p => p.InstallDate is { } d && d > DateTime.Today.AddDays(-30)).ToString();
        Badge = _all.Count.ToString();
        Status = $"설치된 프로그램 {_all.Count}개";
    });

    private void Apply()
    {
        IEnumerable<InstalledProgram> q = _all;
        if (!string.IsNullOrWhiteSpace(Filter))
            q = q.Where(p => p.Name.Contains(Filter, StringComparison.CurrentCultureIgnoreCase) ||
                             p.Publisher.Contains(Filter, StringComparison.CurrentCultureIgnoreCase));
        q = Sort switch
        {
            "이름순" => q.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            "최근 설치 순" => q.OrderByDescending(p => p.InstallDate ?? DateTime.MinValue),
            _ => q.OrderByDescending(p => p.SizeBytes),
        };
        Programs.Clear();
        foreach (var p in q) Programs.Add(p);
    }

    private async Task UninstallAsync(object? param)
    {
        if (param is not InstalledProgram p) return;
        if (!Dialogs.Confirm($"‘{p.Name}’을(를) 제거할까요?\n\n해당 프로그램의 제거 마법사가 실행됩니다.")) return;

        Status = $"‘{p.Name}’ 제거 프로그램 실행 중…";
        await BusyAsync(async () =>
        {
            try { await ProgramService.RunUninstallerAsync(p); }
            catch (Exception ex) { Dialogs.Error($"제거 프로그램을 실행하지 못했습니다.\n\n{ex.Message}"); return; }
        });

        var before = _all.Count;
        await LoadAsync();
        bool gone = !_all.Any(x => x.Name == p.Name && x.Version == p.Version);
        if (!gone) { Status = $"‘{p.Name}’이(가) 아직 설치되어 있습니다. (제거를 취소했거나 실패했을 수 있어요)"; return; }

        var left = ProgramService.LeftoverFolder(p);
        if (left is not null)
        {
            long size = await Task.Run(() => ProgramService.FolderSize(left));
            if (Dialogs.Confirm($"제거 후 남은 폴더가 있습니다.\n\n{left}\n({SizeFormat.ToText(size)})\n\n휴지통으로 이동할까요?"))
            {
                try
                {
                    await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(left,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin));
                    HistoryService.Add($"프로그램 제거: {p.Name}", size, 1);
                }
                catch (Exception ex) { Dialogs.Error(ex.Message); }
            }
        }
        else HistoryService.Add($"프로그램 제거: {p.Name}", p.SizeBytes, 1);
        Status = $"‘{p.Name}’ 제거 완료. 남은 레지스트리 항목은 ‘레지스트리 정리’에서 찾아보세요.";
    }
}
