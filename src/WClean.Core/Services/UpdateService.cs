using System.Net.Http;
using System.Text.Json;

namespace WClean;

public sealed record UpdateInfo(Version Latest, string Tag, string PageUrl, bool IsNewer);

/// <summary>Asks the GitHub releases API whether a newer version exists. Only reads; never downloads or installs.</summary>
public static class UpdateService
{
    public const string Repo = "karanglass24-lang/w_clean";

    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WClean-update-check");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.GetProperty("html_url").GetString() ?? $"https://github.com/{Repo}/releases";
            if (!url.StartsWith($"https://github.com/{Repo}/", StringComparison.OrdinalIgnoreCase))
                url = $"https://github.com/{Repo}/releases";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
            return new UpdateInfo(latest, tag, url, latest > current);
        }
        catch { return null; }
    }
}
