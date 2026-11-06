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

    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.Apply(ThemeManager.LoadPreference());

        if (e.Args.Contains(ScheduleService.AutoCleanArg))
        {
            // Scheduled run: no main window; clean, tell the user, exit.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunAutoCleanAsync();
            return;   // skip StartupUri
        }
        base.OnStartup(e);
    }

    private async Task RunAutoCleanAsync()
    {
        try
        {
            var (freed, deleted) = await Task.Run(ScheduleService.RunAutoClean);
            var toast = new ToastWindow("W Clean 자동 정리 완료",
                freed > 0 ? $"{SizeFormat.ToText(freed)} 확보 · 파일 {deleted:N0}개 삭제" : "정리할 파일이 없었습니다.",
                TimeSpan.FromSeconds(6));
            toast.Closed += (_, _) => Shutdown();
            toast.Show();
        }
        catch { Shutdown(); }
    }
}
