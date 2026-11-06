namespace WClean.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Description { get; }
    /// <summary>Segoe Fluent Icons glyph for the sidebar.</summary>
    public abstract string Glyph { get; }

    private string _badge = "";
    public string Badge { get => _badge; protected set => Set(ref _badge, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private string _status = "";
    public string Status { get => _status; protected set => Set(ref _status, value); }

    protected async Task BusyAsync(Func<Task> work)
    {
        IsBusy = true;
        try { await work(); }
        finally { IsBusy = false; }
    }
}
