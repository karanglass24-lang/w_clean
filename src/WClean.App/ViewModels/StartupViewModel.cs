using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class StartupViewModel : PageViewModel
{
    private readonly StartupService _svc = new();

    public override string Title => "시작프로그램";
    public override string Description => "Windows가 켜질 때 자동으로 실행되는 프로그램을 관리합니다. 끄면 언제든 다시 켤 수 있습니다.";
    public override string Glyph => "";

    public ObservableCollection<StartupEntry> Entries { get; } = new();

    public int Total => Entries.Count;
    public int EnabledCount => Entries.Count(e => e.IsEnabled);
    public int DisabledCount => Entries.Count(e => !e.IsEnabled);
    public int MissingCount => Entries.Count(e => e.TargetMissing);

    public ICommand RefreshCommand { get; }
    public ICommand ToggleCommand { get; }
    public ICommand DisableCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenLocationCommand { get; }

    public StartupViewModel()
    {
        static bool Has(object? p) => p is IList { Count: > 0 };
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
        DisableCommand = new AsyncRelayCommand(DisableAsync, Has);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, Has);
        OpenLocationCommand = new RelayCommand(OpenLocation, Has);
    }

    public Task RefreshAsync() => BusyAsync(async () =>
    {
        Status = "시작프로그램을 불러오는 중…";
        var list = await Task.Run(_svc.Load);
        Entries.Clear();
        foreach (var e in list) Entries.Add(e);
        UpdateSummary();
    });

    private void UpdateSummary()
    {
        Raise(nameof(Total)); Raise(nameof(EnabledCount)); Raise(nameof(DisabledCount)); Raise(nameof(MissingCount));
        Badge = Entries.Count == 0 ? "" : Entries.Count.ToString();
        Status = $"총 {Entries.Count}개 중 {EnabledCount}개가 시작 시 실행됩니다.";
    }

    private static List<StartupEntry> Selected(object? p) => ((IList)p!).Cast<StartupEntry>().ToList();

    private async Task ToggleAsync(object? p)
    {
        if (p is not StartupEntry e) return;
        bool target = !e.IsEnabled;     // the switch has already flipped visually; model still holds the old value
        try
        {
            await Task.Run(() => _svc.SetEnabled(e, target));
            UpdateSummary();
        }
        catch (Exception ex)
        {
            e.NotifyEnabledChanged();   // snap the switch back
            Dialogs.Error($"'{e.Name}' 항목을 변경하지 못했습니다.\n관리자 권한으로 실행했는지 확인하세요.\n\n{ex.Message}");
        }
    }

    private async Task DisableAsync(object? p)
    {
        var items = Selected(p).Where(i => i.IsEnabled).ToList();
        var failed = new List<string>();
        await Task.Run(() =>
        {
            foreach (var e in items)
            {
                try { _svc.SetEnabled(e, false); }
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
        await Task.Run(() =>
        {
            foreach (var e in items)
            {
                try { _svc.Delete(e); }
                catch (Exception ex) { failed.Add($"{e.Name}: {ex.Message}"); }
            }
        });
        await RefreshAsync();

        if (failed.Count > 0)
            Dialogs.Error("일부 항목을 삭제하지 못했습니다. 관리자 권한으로 실행했는지 확인하세요.\n\n" + string.Join("\n", failed));
        else
            Status = $"{items.Count}개 삭제 완료 · 백업 폴더: {BackupStore.Directory}";
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
