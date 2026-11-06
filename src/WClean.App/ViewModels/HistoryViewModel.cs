using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class HistoryViewModel : PageViewModel
{
    private readonly ScheduleService _schedule = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _loading;

    public override string Title => "기록 · 설정";
    public override string Description => "지금까지 정리한 용량과 내역을 보고, 자동 정리 예약과 업데이트 확인을 설정합니다.";
    public override string Glyph => "";

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    private string _totalFreed = "—";
    public string TotalFreed { get => _totalFreed; private set => Set(ref _totalFreed, value); }
    private string _runs = "—";
    public string Runs { get => _runs; private set => Set(ref _runs, value); }
    private string _last = "—";
    public string Last { get => _last; private set => Set(ref _last, value); }

    // ---- schedule
    public IReadOnlyList<string> Frequencies { get; } = new[] { "매일 12:00", "매주 월요일 12:00", "매월 1일 12:00" };

    private bool _scheduleOn;
    public bool ScheduleOn
    {
        get => _scheduleOn;
        set { if (Set(ref _scheduleOn, value) && !_loading) ApplySchedule(); }
    }

    private string _frequency = "매주 월요일 12:00";
    public string Frequency
    {
        get => _frequency;
        set { if (Set(ref _frequency, value) && !_loading && ScheduleOn) ApplySchedule(); }
    }

    // ---- update
    public string CurrentVersion { get; } = typeof(HistoryViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private string _updateText = "";
    public string UpdateText { get => _updateText; private set => Set(ref _updateText, value); }

    private string? _updateUrl;
    public string? UpdateUrl { get => _updateUrl; private set { if (Set(ref _updateUrl, value)) Raise(nameof(HasUpdate)); } }
    public bool HasUpdate => UpdateUrl is not null;

    public bool AutoCheck
    {
        get => _settings.AutoCheckUpdates;
        set { _settings.AutoCheckUpdates = value; _settings.Save(); Raise(); }
    }

    public ICommand RefreshCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CheckUpdateCommand { get; }
    public ICommand OpenUpdateCommand { get; }

    /// <summary>Raised when a newer version is found (used for the sidebar badge).</summary>
    public event Action<UpdateInfo>? UpdateFound;

    public HistoryViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Load());
        ClearCommand = new RelayCommand(_ =>
        {
            if (Dialogs.Confirm("정리 기록을 모두 지울까요?")) { HistoryService.Clear(); Load(); }
        });
        CheckUpdateCommand = new AsyncRelayCommand(_ => CheckUpdateAsync(manual: true));
        OpenUpdateCommand = new RelayCommand(_ =>
        {
            if (UpdateUrl is not null) Process.Start(new ProcessStartInfo(UpdateUrl) { UseShellExecute = true });
        }, _ => UpdateUrl is not null);
        Load();
    }


    public void Load()
    {
        _loading = true;
        try
        {
            var list = HistoryService.Load();
            Entries.Clear();
            foreach (var e in list.OrderByDescending(e => e.Time)) Entries.Add(e);
            TotalFreed = SizeFormat.ToText(list.Sum(e => e.Bytes));
            Runs = list.Count.ToString();
            Last = list.Count > 0 ? list.Max(e => e.Time).ToString("MM-dd HH:mm") : "—";

            var f = _schedule.GetCurrent();
            ScheduleOn = f is not null;
            if (f is not null) Frequency = Frequencies[(int)f.Value];
            Status = Entries.Count == 0 ? "아직 정리 기록이 없습니다." : $"기록 {Entries.Count}건";
        }
        finally { _loading = false; }
    }

    private void ApplySchedule()
    {
        try
        {
            if (ScheduleOn)
            {
                var exe = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 찾지 못했습니다.");
                _schedule.Enable((ScheduleFrequency)Math.Max(0, Array.IndexOf(Frequencies.ToArray(), Frequency)), exe);
                Status = $"자동 정리를 예약했습니다 · {Frequency}";
            }
            else
            {
                _schedule.Disable();
                Status = "자동 정리 예약을 해제했습니다.";
            }
        }
        catch (Exception ex)
        {
            _loading = true;
            ScheduleOn = _schedule.GetCurrent() is not null;
            _loading = false;
            Dialogs.Error($"예약을 변경하지 못했습니다. 관리자 권한으로 실행했는지 확인하세요.\n\n{ex.Message}");
        }
    }

    public async Task CheckUpdateAsync(bool manual)
    {
        if (manual) UpdateText = "확인 중…";
        var cur = Version.TryParse(CurrentVersion, out var v) ? v : new Version(0, 0, 0);
        var info = await UpdateService.CheckAsync(cur);
        if (info is null) { if (manual) UpdateText = "업데이트 정보를 가져오지 못했습니다. 인터넷 연결을 확인하세요."; return; }
        if (info.IsNewer)
        {
            UpdateUrl = info.PageUrl;
            UpdateText = $"새 버전 {info.Tag} 이(가) 있습니다.";
            UpdateFound?.Invoke(info);
        }
        else
        {
            UpdateUrl = null;
            if (manual) UpdateText = "최신 버전을 사용 중입니다.";
        }
    }
}
