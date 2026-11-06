using System.Text.Json;

namespace WClean;

public sealed class AppSettings
{
    public bool AutoCheckUpdates { get; set; } = true;

    private static string FilePath => Path.Combine(AppData.Dir, "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch { }
    }
}
