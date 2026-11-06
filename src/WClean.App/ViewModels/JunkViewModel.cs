using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class JunkViewModel : PageViewModel
{
    private readonly JunkService _svc = new();

    public override string Title => "쓰레기파일 정리";
    public override string Description => "임시 파일, 캐시, 휴지통 등 지워도 안전한 파일을 찾아 정리합니다. 먼저 검사한 뒤 체크한 항목만 삭제합니다.";
    public override string Glyph => "";

    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["사용자 임시 파일"] = "",
        ["Windows 임시 파일"] = "",
        ["휴지통"] = "",
        ["썸네일·아이콘 캐시"] = "",
        ["Windows 업데이트 캐시"] = "",
        ["오류 보고·크래시 덤프"] = "",
        ["그래픽 셰이더 캐시"] = "",
        ["Chrome 캐시"] = "",
        ["Edge 캐시"] = "",
        ["Whale 캐시"] = "",
        ["Firefox 캐시"] = "",
    };

    public ObservableCollection<JunkCategory> Categories { get; } = new();

    private string _totalText = "—";
    public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }

    private string _selectedText = "—";
    public string SelectedText { get => _selectedText; private set => Set(ref _selectedText, value); }

    private string _foundText = "—";
    public string FoundText { get => _foundText; private set => Set(ref _foundText, value); }

    public ICommand ScanCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }

    public JunkViewModel()
    {
        foreach (var c in _svc.CreateCategories())
        {
            if (Glyphs.TryGetValue(c.Name, out var g)) c.Glyph = g;
            c.PropertyChanged += OnCategoryChanged;
            Categories.Add(c);
        }
        Status = "‘검사’를 눌러 정리할 수 있는 파일을 찾아보세요.";
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync());
        CleanCommand = new AsyncRelayCommand(_ => CleanAsync(), _ => Categories.Any(c => c.IsChecked && c.Scanned && c.Size > 0));
        SelectAllCommand = new RelayCommand(_ => { foreach (var c in Categories) c.IsChecked = true; });
        SelectNoneCommand = new RelayCommand(_ => { foreach (var c in Categories) c.IsChecked = false; });
    }

    private void OnCategoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JunkCategory.IsChecked) or nameof(JunkCategory.Size) or nameof(JunkCategory.Scanned))
            UpdateSummary();
    }

    private void UpdateSummary()
    {
        // Scan runs on worker threads and changes properties from there.
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is not null && !disp.CheckAccess()) { disp.BeginInvoke(UpdateSummary); return; }

        var scanned = Categories.Where(c => c.Scanned).ToList();
        if (scanned.Count == 0)
        {
            TotalText = SelectedText = FoundText = "—";
            Badge = "";
            return;
        }
        long total = scanned.Sum(c => c.Size);
        long selected = scanned.Where(c => c.IsChecked).Sum(c => c.Size);
        TotalText = SizeFormat.ToText(total);
        SelectedText = SizeFormat.ToText(selected);
        FoundText = $"{scanned.Count(c => c.Size > 0)}개 항목";
        Badge = total > 0 ? SizeFormat.ToText(total) : "";
    }

    private Task ScanAsync() => BusyAsync(async () =>
    {
        Status = "검사 중…";
        await _svc.ScanAsync(Categories);
        long max = Categories.Max(c => c.Size);
        foreach (var c in Categories) c.Ratio = max > 0 ? (double)c.Size / max : 0;
        UpdateSummary();
        Status = $"검사 완료 · 정리 가능한 용량 {TotalText}";
    });

    private Task CleanAsync() => BusyAsync(async () =>
    {
        var targets = Categories.Where(c => c.IsChecked && c.Scanned && c.Size > 0).ToList();
        long size = targets.Sum(c => c.Size);
        if (!Dialogs.Confirm($"{targets.Count}개 항목, 약 {SizeFormat.ToText(size)}을(를) 삭제할까요?\n" +
                             "삭제한 파일은 복구할 수 없습니다. 브라우저 캐시는 브라우저를 종료한 뒤 정리하세요."))
            return;

        var progress = new Progress<string>(s => Status = $"정리 중… {s}");
        var result = await _svc.CleanAsync(targets, progress);
        foreach (var c in targets) { c.Size = 0; c.Count = 0; c.Ratio = 0; }
        UpdateSummary();
        Status = $"정리 완료 · {SizeFormat.ToText(result.FreedBytes)} 확보 · 삭제 {result.Deleted:N0}개" +
                 (result.Skipped > 0 ? $" · 사용 중이라 건너뜀 {result.Skipped:N0}개" : "");
    });
}
