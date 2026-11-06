using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class StartupViewModel : ObservableObject
{
    private readonly StartupService _svc = new();

    public ObservableCollection<StartupEntry> Entries { get; } = new();

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public ICommand RefreshCommand { get; }
    public ICommand EnableCommand { get; }
    public ICommand DisableCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenLocationCommand { get; }

    public StartupViewModel()
    {
        static bool Has(object? p) => p is IList { Count: > 0 };
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        EnableCommand = new AsyncRelayCommand(p => SetEnabledAsync(p, true), Has);
        DisableCommand = new AsyncRelayCommand(p => SetEnabledAsync(p, false), Has);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, Has);
        OpenLocationCommand = new RelayCommand(OpenLocation, Has);
    }

    public async Task RefreshAsync()
    {
        Status = "불러오는 중…";
        var list = await Task.Run(_svc.Load);
        Entries.Clear();
        foreach (var e in list) Entries.Add(e);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int on = Entries.Count(e => e.IsEnabled);
        Status = $"총 {Entries.Count}개 · 사용 {on}개 · 사용 안 함 {Entries.Count - on}개";
    }

    private static List<StartupEntry> Selected(object? p) => ((IList)p!).Cast<StartupEntry>().ToList();

    private async Task SetEnabledAsync(object? p, bool enabled)
    {
        var items = Selected(p);
        var failed = new List<string>();
        await Task.Run(() =>
        {
            foreach (var e in items)
            {
                try { _svc.SetEnabled(e, enabled); }
                catch (Exception ex) { failed.Add($"{e.Name}: {ex.Message}"); }
            }
        });
        UpdateSummary();
        if (failed.Count > 0)
            Dialogs.Error("일부 항목을 변경하지 못했습니다. 관리자 권한으로 실행했는지 확인하세요.\n\n" + string.Join("\n", failed));
    }

    private async Task DeleteAsync(object? p)
    {
        var items = Selected(p);
        var names = string.Join("\n", items.Take(8).Select(i => "• " + i.Name)) + (items.Count > 8 ? $"\n… 외 {items.Count - 8}개" : "");
        if (!Dialogs.Confirm($"선택한 {items.Count}개 항목을 삭제할까요?\n\n{names}\n\n" +
                             "레지스트리·작업 스케줄러 항목은 백업 파일을 남기고, 시작 폴더 바로가기는 휴지통으로 이동합니다."))
            return;

        var failed = new List<string>();
        string? lastBackup = null;
        await Task.Run(() =>
        {
            foreach (var e in items)
            {
                try { lastBackup = _svc.Delete(e) ?? lastBackup; }
                catch (Exception ex) { failed.Add($"{e.Name}: {ex.Message}"); }
            }
        });
        await RefreshAsync();

        if (failed.Count > 0)
            Dialogs.Error("일부 항목을 삭제하지 못했습니다. 관리자 권한으로 실행했는지 확인하세요.\n\n" + string.Join("\n", failed));
        else if (lastBackup is not null)
            Status += $"  ·  백업 폴더: {BackupStore.Directory}";
    }

    private void OpenLocation(object? p)
    {
        foreach (var e in Selected(p).Take(5))
        {
            var exe = e.FilePath ?? PathHelper.ExtractExecutable(e.Command);
            if (exe is not null) Dialogs.RevealInExplorer(exe);
        }
    }
}
