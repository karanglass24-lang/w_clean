namespace WClean.App.ViewModels;

public sealed class MainViewModel
{
    public StartupViewModel Startup { get; } = new();
    public JunkViewModel Junk { get; } = new();
    public RegistryViewModel Registry { get; } = new();
    public LargeFileViewModel Large { get; } = new();

    public bool IsAdmin { get; } = Elevation.IsAdmin();
    public bool ShowAdminWarning => !IsAdmin;
}
