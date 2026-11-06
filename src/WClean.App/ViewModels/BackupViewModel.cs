using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class BackupViewModel : PageViewModel
{
    private readonly BackupService _svc = new();

    public override string Title => "백업 복원";
    public override string Description => "레지스트리와 시작프로그램을 정리할 때 자동으로 저장된 백업입니다. 잘못 지웠다면 여기서 되돌릴 수 있습니다.";
    public override string Glyph => "";

    public ObservableCollection<BackupItem> Items { get; } = new();

    public int Total => Items.Count;
    public int RegCount => Items.Count(i => i.IsRegistry);
    public int TaskCount => Items.Count(i => !i.IsRegistry);

    public ICommand RefreshCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public BackupViewModel()
    {
        static bool Has(object? p) => p is IList { Count: > 0 };
        RefreshCommand = new RelayCommand(_ => Load());
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, Has);
        DeleteCommand = new RelayCommand(Delete, Has);
        OpenFolderCommand = new RelayCommand(_ => Dialogs.RevealInExplorer(BackupStore.Directory));
        Load();
    }

    public void Load()
    {
        Items.Clear();
        foreach (var b in _svc.Load()) Items.Add(b);
        Raise(nameof(Total)); Raise(nameof(RegCount)); Raise(nameof(TaskCount));
        Badge = Items.Count > 0 ? Items.Count.ToString() : "";
        Status = Items.Count == 0 ? "저장된 백업이 없습니다." : $"백업 {Items.Count}개 · 폴더: {BackupStore.Directory}";
    }

    private static List<BackupItem> Selected(object? p) => ((IList)p!).Cast<BackupItem>().ToList();

    private async Task RestoreAsync(object? p)
    {
        var items = Selected(p);
        if (!Dialogs.Confirm($"선택한 {items.Count}개 백업을 복원할까요?\n\n레지스트리 항목은 다시 기록되고, 작업 스케줄러 항목은 다시 만들어집니다.\n(HKLM 항목은 관리자 권한이 필요합니다.)"))
            return;
        var failed = new List<string>();
        int ok = 0;
        await Task.Run(() =>
        {
            foreach (var b in items)
            {
                try { _svc.Restore(b); ok++; }
                catch (Exception ex) { failed.Add($"{b.Label}: {ex.Message}"); }
            }
        });
        Status = $"{ok}개 복원 완료" + (failed.Count > 0 ? $" · 실패 {failed.Count}개" : "");
        if (failed.Count > 0) Dialogs.Error("복원하지 못한 백업:\n\n" + string.Join("\n", failed.Take(8)));
    }

    private void Delete(object? p)
    {
        var items = Selected(p);
        if (!Dialogs.Confirm($"선택한 백업 {items.Count}개를 삭제할까요?\n삭제하면 이 백업으로는 복원할 수 없습니다.")) return;
        foreach (var b in items)
        {
            try { BackupService.Delete(b); } catch { }
        }
        Load();
    }
}
