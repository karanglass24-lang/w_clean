using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace WClean.App;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _exec;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action<object?> exec, Func<object?, bool>? can = null) { _exec = exec; _can = can; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _exec(parameter);
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _exec;
    private readonly Func<object?, bool>? _can;
    private bool _running;

    public AsyncRelayCommand(Func<object?, Task> exec, Func<object?, bool>? can = null) { _exec = exec; _can = can; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (_can?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try { await _exec(parameter); }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

public static class Dialogs
{
    public static bool Confirm(string message, string title = "W Clean")
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Info(string message, string title = "W Clean")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string message, string title = "W Clean")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }
}
