using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class ServicesViewModel : PageViewModel
{
    private readonly WindowsServiceManager _svc = new();
    private List<ServiceEntry> _all = new();

    public override string Title => "서비스 관리";
    public override string Description => "백그라운드에서 자동으로 시작되는 서비스를 관리합니다. 기본으로는 Microsoft 서비스를 숨깁니다. 모르는 서비스는 건드리지 마세요.";
    public override string Glyph => "";

    public ObservableCollection<ServiceEntry> Services { get; } = new();

    private bool _showMicrosoft;
    public bool ShowMicrosoft { get => _showMicrosoft; set { if (Set(ref _showMicrosoft, value)) Apply(); } }

    private string _filter = "";
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) Apply(); } }

    public int AutoCount => _all.Count(s => !s.IsMicrosoft && s.Mode == ServiceStartMode.Auto);
    public int RunningCount => _all.Count(s => !s.IsMicrosoft && s.Running);
    public int DisabledCount => _all.Count(s => !s.IsMicrosoft && s.Mode == ServiceStartMode.Disabled);

    public ICommand RefreshCommand { get; }
    public ICommand AutoCommand { get; }
    public ICommand ManualCommand { get; }
    public ICommand DisableCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand StartCommand { get; }

    public ServicesViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync());
        AutoCommand = new AsyncRelayCommand(p => SetModeAsync(p, ServiceStartMode.Auto), p => p is ServiceEntry { Mode: not ServiceStartMode.Auto });
        ManualCommand = new AsyncRelayCommand(p => SetModeAsync(p, ServiceStartMode.Manual), p => p is ServiceEntry { Mode: not ServiceStartMode.Manual });
        DisableCommand = new AsyncRelayCommand(p => SetModeAsync(p, ServiceStartMode.Disabled), p => p is ServiceEntry { Mode: not ServiceStartMode.Disabled });
        StopCommand = new AsyncRelayCommand(p => ActAsync(p, stop: true), p => p is ServiceEntry { Running: true });
        StartCommand = new AsyncRelayCommand(p => ActAsync(p, stop: false), p => p is ServiceEntry { Running: false });
    }

    public Task LoadAsync() => BusyAsync(async () =>
    {
        Status = "서비스를 불러오는 중…";
        _all = await Task.Run(_svc.Load);
        Apply();
        Raise(nameof(AutoCount)); Raise(nameof(RunningCount)); Raise(nameof(DisabledCount));
        Badge = AutoCount.ToString();
        Status = $"Microsoft 외 서비스 {_all.Count(s => !s.IsMicrosoft)}개 · 전체 {_all.Count}개";
    });

    private void Apply()
    {
        IEnumerable<ServiceEntry> q = _all;
        if (!ShowMicrosoft) q = q.Where(s => !s.IsMicrosoft);
        if (!string.IsNullOrWhiteSpace(Filter))
            q = q.Where(s => s.DisplayName.Contains(Filter, StringComparison.CurrentCultureIgnoreCase) ||
                             s.Name.Contains(Filter, StringComparison.CurrentCultureIgnoreCase));
        Services.Clear();
        foreach (var s in q) Services.Add(s);
    }

    private async Task SetModeAsync(object? p, ServiceStartMode mode)
    {
        if (p is not ServiceEntry s) return;
        if (s.Mode == mode) return;
        if (s.IsMicrosoft &&
            !Dialogs.Confirm($"‘{s.DisplayName}’은(는) Microsoft 서비스입니다. 변경하면 Windows가 오작동할 수 있습니다.\n\n그래도 변경할까요?"))
            return;
        try
        {
            await Task.Run(() => _svc.SetStartMode(s, mode));
            Raise(nameof(AutoCount)); Raise(nameof(DisabledCount));
            Status = $"‘{s.DisplayName}’ 시작 유형: {s.ModeText} (이전 값으로는 언제든 되돌릴 수 있습니다)";
        }
        catch (Exception ex)
        {
            Dialogs.Error($"변경하지 못했습니다. 관리자 권한으로 실행했는지 확인하세요.\n\n{ex.Message}");
        }
    }

    private async Task ActAsync(object? p, bool stop)
    {
        if (p is not ServiceEntry s) return;
        if (stop && s.IsMicrosoft && !Dialogs.Confirm($"‘{s.DisplayName}’은(는) Microsoft 서비스입니다. 중지할까요?")) return;
        try
        {
            await Task.Run(() => { if (stop) _svc.Stop(s); else _svc.Start(s); });
            Raise(nameof(RunningCount));
            Status = $"‘{s.DisplayName}’ {(stop ? "중지" : "시작")} 완료";
        }
        catch (Exception ex)
        {
            Dialogs.Error($"{(stop ? "중지" : "시작")}하지 못했습니다.\n\n{ex.Message}");
        }
    }
}
