using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace WClean.App.ViewModels;

public sealed class NavItem : ObservableObject
{
    public NavItem(PageViewModel page) => Page = page;
    public PageViewModel Page { get; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class MainViewModel : ObservableObject
{
    public StartupViewModel Startup { get; } = new();
    public JunkViewModel Junk { get; } = new();
    public RegistryViewModel Registry { get; } = new();
    public LargeFileViewModel Large { get; } = new();
    public DuplicateViewModel Duplicates { get; } = new();
    public DiskViewModel Disk { get; } = new();
    public ProgramsViewModel Programs { get; } = new();
    public ServicesViewModel Services { get; } = new();
    public TidyViewModel Tidy { get; } = new();
    public BackupViewModel Backup { get; } = new();
    public HistoryViewModel History { get; } = new();

    public IReadOnlyList<NavItem> Items { get; }

    private NavItem _selected;
    public NavItem Selected
    {
        get => _selected;
        private set { if (Set(ref _selected, value)) OnPageSelected(value.Page); }
    }

    private bool _programsLoaded, _servicesLoaded;

    /// <summary>Heavier lists are loaded the first time their page is opened.</summary>
    private void OnPageSelected(PageViewModel page)
    {
        switch (page)
        {
            case ProgramsViewModel p when !_programsLoaded: _programsLoaded = true; _ = p.LoadAsync(); break;
            case ServicesViewModel s when !_servicesLoaded: _servicesLoaded = true; _ = s.LoadAsync(); break;
            case BackupViewModel b: b.Load(); break;
            case HistoryViewModel h: h.Load(); break;
        }
    }

    public bool IsAdmin { get; } = Elevation.IsAdmin();
    public bool NotAdmin => !IsAdmin;
    public string AdminText => IsAdmin ? "관리자 권한으로 실행 중" : "일반 권한 · 일부 기능 제한";

    private bool _isDark = ThemeManager.LoadPreference();
    public bool IsDark
    {
        get => _isDark;
        set { if (Set(ref _isDark, value)) ThemeManager.Apply(value, save: true); }
    }

    private string _updateBadge = "";
    public string UpdateBadge { get => _updateBadge; private set { if (Set(ref _updateBadge, value)) Raise(nameof(HasUpdateBadge)); } }
    public bool HasUpdateBadge => UpdateBadge.Length > 0;

    public ICommand RestartAsAdminCommand { get; }

    public string Version { get; } = "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

    public MainViewModel()
    {
        Items = new[]
        {
            new NavItem(Startup), new NavItem(Junk), new NavItem(Registry), new NavItem(Large), new NavItem(Duplicates), new NavItem(Disk),
            new NavItem(Programs), new NavItem(Services), new NavItem(Tidy), new NavItem(Backup), new NavItem(History),
        };
        foreach (var item in Items)
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NavItem.IsSelected) && s is NavItem n && n.IsSelected) Selected = n;
            };
        _selected = Items[0];
        _selected.IsSelected = true;

        RestartAsAdminCommand = new RelayCommand(_ => RestartAsAdmin());

        History.UpdateFound += info => UpdateBadge = $"새 버전 {info.Tag}";
        if (AppSettings.Load().AutoCheckUpdates) _ = History.CheckUpdateAsync(manual: false);
    }

    private static void RestartAsAdmin()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception) { /* user declined the UAC prompt */ }
    }
}
