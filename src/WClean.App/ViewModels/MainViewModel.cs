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

    public IReadOnlyList<NavItem> Items { get; }

    private NavItem _selected;
    public NavItem Selected { get => _selected; private set => Set(ref _selected, value); }

    public bool IsAdmin { get; } = Elevation.IsAdmin();
    public bool NotAdmin => !IsAdmin;
    public string AdminText => IsAdmin ? "관리자 권한으로 실행 중" : "일반 권한 · 일부 기능 제한";

    private bool _isDark = ThemeManager.LoadPreference();
    public bool IsDark
    {
        get => _isDark;
        set { if (Set(ref _isDark, value)) ThemeManager.Apply(value, save: true); }
    }

    public ICommand RestartAsAdminCommand { get; }

    public string Version { get; } = "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");

    public MainViewModel()
    {
        Items = new[] { new NavItem(Startup), new NavItem(Junk), new NavItem(Registry), new NavItem(Large) };
        foreach (var item in Items)
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NavItem.IsSelected) && s is NavItem n && n.IsSelected) Selected = n;
            };
        _selected = Items[0];
        _selected.IsSelected = true;

        RestartAsAdminCommand = new RelayCommand(_ => RestartAsAdmin());
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
