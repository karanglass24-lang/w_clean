using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class RegistryViewModel : ObservableObject
{
    private readonly RegistryCleaner _svc = new();

    public ObservableCollection<RegistryIssue> Issues { get; } = new();

    private string _status = "‘검사’를 눌러 잘못된 레지스트리 항목을 찾아보세요.";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string _selectedSummary = "";
    public string SelectedSummary { get => _selectedSummary; private set => Set(ref _selectedSummary, value); }

    public ICommand ScanCommand { get; }
    public ICommand FixCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand OpenBackupFolderCommand { get; }

    public RegistryViewModel()
    {
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync());
        FixCommand = new AsyncRelayCommand(_ => FixAsync(), _ => Issues.Any(i => i.IsChecked));
        SelectAllCommand = new RelayCommand(_ => { foreach (var i in Issues) i.IsChecked = true; });
        SelectNoneCommand = new RelayCommand(_ => { foreach (var i in Issues) i.IsChecked = false; });
        OpenBackupFolderCommand = new RelayCommand(_ => Dialogs.RevealInExplorer(BackupStore.Directory));
    }

    private void OnIssueChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegistryIssue.IsChecked)) UpdateSummary();
    }

    private void UpdateSummary()
        => SelectedSummary = $"선택 {Issues.Count(i => i.IsChecked)} / 전체 {Issues.Count}개";

    private async Task ScanAsync()
    {
        Status = "검사 중…";
        var found = await Task.Run(() => _svc.Scan());
        foreach (var i in Issues) i.PropertyChanged -= OnIssueChanged;
        Issues.Clear();
        foreach (var i in found.OrderBy(i => i.Category).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            i.PropertyChanged += OnIssueChanged;
            Issues.Add(i);
        }
        UpdateSummary();
        Status = found.Count == 0 ? "문제가 있는 레지스트리 항목이 없습니다." : $"검사 완료 · {found.Count}개 항목 발견";
    }

    private async Task FixAsync()
    {
        var targets = Issues.Where(i => i.IsChecked).ToList();
        if (!Dialogs.Confirm($"선택한 {targets.Count}개 항목을 삭제할까요?\n삭제 전에 복원용 .reg 백업 파일을 저장합니다."))
            return;

        Status = "정리 중…";
        var result = await Task.Run(() => _svc.Fix(targets));

        // Drop what was handled (keep failures so they stay visible).
        if (result.Failed == 0)
        {
            foreach (var t in targets) { t.PropertyChanged -= OnIssueChanged; Issues.Remove(t); }
        }
        UpdateSummary();
        Status = $"완료 · {result.Fixed}개 삭제" + (result.Failed > 0 ? $" · 실패 {result.Failed}개 (관리자 권한 필요)" : "") +
                 (result.BackupPath is null ? "" : $" · 백업: {result.BackupPath}");
        if (result.Failed > 0) await ScanAsync();
    }
}
