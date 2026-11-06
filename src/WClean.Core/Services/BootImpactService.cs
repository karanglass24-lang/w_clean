using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;

namespace WClean;

/// <summary>
/// Reads how much each startup program slowed the last boots from the Windows
/// "Diagnostics-Performance" log (event 101). Needs administrator rights; returns nothing otherwise.
/// Windows only logs programs that were slow, so a missing entry means "no measured delay".
/// </summary>
public sealed class BootImpactService
{
    private const string Log = "Microsoft-Windows-Diagnostics-Performance/Operational";

    public sealed record Result(Dictionary<string, long> DelayMsByFile, long? LastBootMs, bool Available);

    public Result Load(int days = 30)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long? boot = null;
        try
        {
            var since = DateTime.UtcNow.AddDays(-days).ToString("o");
            var query = new EventLogQuery(Log, PathType.LogName,
                $"*[System[(EventID=100 or EventID=101) and TimeCreated[@SystemTime>='{since}']]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (EventRecord? ev = reader.ReadEvent(); ev is not null; ev = reader.ReadEvent())
            {
                using (ev)
                {
                    var data = ParseData(ev.ToXml());
                    if (ev.Id == 100)
                    {
                        if (boot is null && data.TryGetValue("BootTime", out var bt) && long.TryParse(bt, out var b)) boot = b;
                    }
                    else
                    {
                        var file = Get(data, "FileName") ?? Get(data, "Name");
                        if (file is null) continue;
                        file = Path.GetFileName(file.Trim());
                        long ms = 0;
                        if (data.TryGetValue("DegradationTime", out var dt) && long.TryParse(dt, out var d)) ms = d;
                        else if (data.TryGetValue("TotalTime", out var tt) && long.TryParse(tt, out var t)) ms = t;
                        if (ms <= 0) continue;
                        map[file] = map.TryGetValue(file, out var old) ? Math.Max(old, ms) : ms;   // worst boot in the period
                    }
                }
            }
            return new Result(map, boot, true);
        }
        catch { return new Result(map, boot, false); }
    }

    private static string? Get(Dictionary<string, string> d, string key) => d.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    private static Dictionary<string, string> ParseData(string xml)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in XDocument.Parse(xml).Descendants().Where(x => x.Name.LocalName == "Data"))
        {
            var name = (string?)e.Attribute("Name");
            if (name is not null) dict[name] = e.Value;
        }
        return dict;
    }

    public static string ImpactText(long ms) => ms switch
    {
        >= 5000 => $"높음 ({ms / 1000.0:0.#}초)",
        >= 1000 => $"보통 ({ms / 1000.0:0.#}초)",
        _ => $"낮음 ({ms}ms)",
    };
}
