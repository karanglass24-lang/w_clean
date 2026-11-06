using System.Windows;
using WClean.App.ViewModels;

namespace WClean.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.Startup.RefreshAsync();
    }
}
