using Microsoft.Win32;

namespace WClean;

public enum StartupKind { Registry, Folder, Task }

public sealed class StartupEntry : ObservableObject
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Source { get; init; } = "";
    public StartupKind Kind { get; init; }
    public bool TargetMissing { get; init; }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (Set(ref _isEnabled, value)) Raise(nameof(StatusText)); }
    }

    /// <summary>Re-raises IsEnabled so a toggle that was undone by an error snaps back in the UI.</summary>
    public void NotifyEnabledChanged() { Raise(nameof(IsEnabled)); Raise(nameof(StatusText)); }

    public string Glyph => Kind switch
    {
        StartupKind.Registry => "",
        StartupKind.Folder => "",
        _ => "",
    };

    public string StatusText => IsEnabled ? "사용" : "사용 안 함";
    public string MissingText => TargetMissing ? "파일 없음" : "";

    // --- where the entry lives (used by StartupService) ---
    internal RegistryHive Hive { get; init; }
    internal RegistryView View { get; init; }
    internal string ApprovedKey { get; init; } = "";   // Run / Run32 / StartupFolder
    internal string ValueName { get; init; } = "";     // registry value or startup-folder file name
    public string? FilePath { get; init; }           // startup-folder file
    internal string? TaskPath { get; init; }           // scheduled task
}
