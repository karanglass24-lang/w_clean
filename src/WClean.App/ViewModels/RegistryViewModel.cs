using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class RegistryViewModel : PageViewModel
{
    private readonly RegistryCleaner _svc = new();

    public override string Title => "레지스트리 정리";
    public override string Description => "삭제된 프로그램의 흔적과 존재하지 않는 파일을 가리키는 항목만 찾습니다. 삭제 전에 복원용 .reg 파일을 자동 저장합니다.";
    public override string Glyph => "";

    public ObservableCollection<RegistryIssue> Issues { get; } = new();

    public int FoundCount => Issues.Count;
    public int SelectedCount => Issues.Count(i => i.IsChecked);
    public int CategoryCount => Issues.Select(i => i.Category).Distinct().Count();

    public ICommand ScanCommand { get; }
    public ICommand FixCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand OpenBackupFolderCommand { get; }

    public RegistryViewModel()
    {
        Status = "‘검사’를 눌러 잘못된 레지스트리 항목을 찾아보세요.";
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync());
        FixCommand = new AsyncRelayCommand(_ => FixAsync(), _ => Issues.Any(i => i.IsChecked));
        SelectAllCommand = new RelayCommand(_ => { foreach (var i in Issues) i.IsChecked = true; });
        SelectNoneCommand = new RelayCommand(_ => { foreach (var i in Issues) i.IsChecked = false; });
        OpenBackupFolderCommand = new RelayCommand(_ => Dialogs.RevealInExplorer(BackupStore.Directory));
    }

    private void OnIssueChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegistryIssue.IsChecked)) Raise(nameof(SelectedCount));
    }

    private void UpdateSummary()
    {
        Raise(nameof(FoundCount)); Raise(nameof(SelectedCount)); Raise(nameof(CategoryCount));
        Badge = Issues.Count > 0 ? Issues.Count.ToString() : "";
    }

    private Task ScanAsync() => BusyAsync(async () =>
    {
        Status = "레지스트리 검사 중…";
        var found = await Task.Run(() => _svc.Scan());
        foreach (var i in Issues) i.PropertyChanged -= OnIssueChanged;
        Issues.Clear();
        foreach (var i in found.OrderBy(i => i.Category).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            i.PropertyChanged += OnIssueChanged;
            Issues.Add(i);
        }
        UpdateSummary();
        Status = found.Count == 0 ? "문제가 있는 레지스트리 항목이 없습니다." : $"검사 완료 · {found.Count}개 항목을 찾았습니다.";
    });

    private Task FixAsync() => BusyAsync(async () =>
    {
        var targets = Issues.Where(i => i.IsChecked).ToList();
        if (!Dialogs.Confirm($"선택한 {targets.Count}개 항목을 삭제할까요?\n삭제 전에 복원용 .reg 백업 파일을 저장합니다."))
            return;

        Status = "정리 중…";
        var result = await Task.Run(() => _svc.Fix(targets));

        if (result.Failed == 0)
        {
            foreach (var t in targets) { t.PropertyChanged -= OnIssueChanged; Issues.Remove(t); }
        }
        UpdateSummary();
        Status = $"완료 · {result.Fixed}개 삭제" + (result.Failed > 0 ? $" · 실패 {result.Failed}개 (관리자 권한 필요)" : "") +
                 (result.BackupPath is null ? "" : $" · 백업: {result.BackupPath}");
    });
}
