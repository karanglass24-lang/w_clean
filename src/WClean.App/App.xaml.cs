using System.Windows;

namespace WClean.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "W Clean 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
