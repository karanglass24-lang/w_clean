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

        // Optional switches for screenshots/testing:  --page=0..3   --scan
        var args = Environment.GetCommandLineArgs();
        var page = args.FirstOrDefault(a => a.StartsWith("--page="))?[7..];
        if (int.TryParse(page, out var idx) && idx >= 0 && idx < _vm.Items.Count) _vm.Items[idx].IsSelected = true;

        var path = args.FirstOrDefault(a => a.StartsWith("--path="))?[7..];
        if (path is not null) { _vm.Large.SearchPath = path; _vm.Duplicates.SearchPath = path; _vm.Disk.CurrentPath = path; }

        Loaded += async (_, _) =>
        {
            await _vm.Startup.RefreshAsync();
            if (args.Contains("--scan"))
            {
                switch (_vm.Selected.Page)
                {
                    case JunkViewModel j: j.ScanCommand.Execute(null); break;
                    case RegistryViewModel r: r.ScanCommand.Execute(null); break;
                    case LargeFileViewModel l: l.ScanCommand.Execute(null); break;
                    case DuplicateViewModel d: d.ScanCommand.Execute(null); break;
                    case DiskViewModel k: k.ScanCommand.Execute(null); break;
                    case TidyViewModel t: t.ScanCommand.Execute(null); break;
                }
            }
        };
    }
}
