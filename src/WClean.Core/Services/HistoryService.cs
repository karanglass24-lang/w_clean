using System.Text.Json;

namespace WClean;

public sealed record HistoryEntry(DateTime Time, string Action, long Bytes, long Count)
{
    public string BytesText => Bytes > 0 ? SizeFormat.ToText(Bytes) : "-";
    public string TimeText => Time.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>Remembers what was cleaned (stored in %AppData%\WClean\history.json).</summary>
public static class HistoryService
{
    private static readonly object Gate = new();
    private const int MaxEntries = 500;

    public static string FilePath => Path.Combine(AppData.Dir, "history.json");

    public static List<HistoryEntry> Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return new();
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { return new(); }
        }
    }

    public static void Add(string action, long bytes, long count)
    {
        lock (Gate)
        {
            var list = Load();
            list.Add(new HistoryEntry(DateTime.Now, action, bytes, count));
            if (list.Count > MaxEntries) list.RemoveRange(0, list.Count - MaxEntries);
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(list)); } catch { }
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            try { File.Delete(FilePath); } catch { }
        }
    }
}

public static class AppData
{
    public static string Dir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WClean");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
