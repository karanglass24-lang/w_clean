using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class JunkViewModel : ObservableObject
{
    private readonly JunkService _svc = new();

    public ObservableCollection<JunkCategory> Categories { get; } = new();

    private string _status = "‘검사’를 눌러 정리할 수 있는 파일을 찾아보세요.";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string _selectedSummary = "";
    public string SelectedSummary { get => _selectedSummary; private set => Set(ref _selectedSummary, value); }

    public ICommand ScanCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }

    public JunkViewModel()
    {
        foreach (var c in _svc.CreateCategories())
        {
            c.PropertyChanged += OnCategoryChanged;
            Categories.Add(c);
        }
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync());
        CleanCommand = new AsyncRelayCommand(_ => CleanAsync(), _ => Categories.Any(c => c.IsChecked && c.Scanned && c.Size > 0));
        SelectAllCommand = new RelayCommand(_ => { foreach (var c in Categories) c.IsChecked = true; });
        SelectNoneCommand = new RelayCommand(_ => { foreach (var c in Categories) c.IsChecked = false; });
        UpdateSummary();
    }

    private void OnCategoryChanged(object? sender, PropertyChangedEventArgs e) => UpdateSummary();

    private void UpdateSummary()
    {
        // Property changes can arrive from scan threads; marshal to the UI thread.
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is not null && !disp.CheckAccess()) { disp.BeginInvoke(UpdateSummary); return; }

        long size = Categories.Where(c => c.IsChecked && c.Scanned).Sum(c => c.Size);
        SelectedSummary = $"선택한 항목: {SizeFormat.ToText(size)}";
    }

    private async Task ScanAsync()
    {
        Status = "검사 중…";
        await _svc.ScanAsync(Categories);
        long total = Categories.Sum(c => c.Size);
        Status = $"검사 완료 · 정리 가능한 용량 {SizeFormat.ToText(total)}";
        UpdateSummary();
    }

    private async Task CleanAsync()
    {
        var targets = Categories.Where(c => c.IsChecked && c.Scanned && c.Size > 0).ToList();
        long size = targets.Sum(c => c.Size);
        if (!Dialogs.Confirm($"{targets.Count}개 항목, 약 {SizeFormat.ToText(size)}을(를) 삭제할까요?\n" +
                             "삭제한 파일은 복구할 수 없습니다. 브라우저 캐시는 브라우저를 종료한 뒤 정리하세요."))
            return;

        var progress = new Progress<string>(s => Status = $"정리 중… {s}");
        var result = await _svc.CleanAsync(targets, progress);
        foreach (var c in targets) { c.Size = 0; c.Count = 0; }
        Status = $"정리 완료 · {SizeFormat.ToText(result.FreedBytes)} 확보 · 삭제 {result.Deleted:N0}개" +
                 (result.Skipped > 0 ? $" · 사용 중이라 건너뜀 {result.Skipped:N0}개" : "");
        UpdateSummary();
    }
}
