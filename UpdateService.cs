using System.Net.Http;
using System.Text.Json;

namespace CollegeScheduleGadget;

public sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Notes { get; set; } = "";
}

public static class UpdateService
{
    public const string CurrentVersion = "1.0.0";
    private const string ReleasesUrl = "https://api.github.com/repos/Deniska2010/rorklad/releases/latest";
    private static readonly HttpClient Client = CreateClient();
    public static bool IsConfigured => true;

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(ReleasesUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var version = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
        var downloadUrl = root.GetProperty("html_url").GetString() ?? "";

        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? downloadUrl;
                    break;
                }
            }
        }

        var update = new UpdateInfo
        {
            Version = version,
            DownloadUrl = downloadUrl,
            Notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : ""
        };
        return update is not null && IsNewer(update.Version) ? update : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CollegeScheduleGadget/1.0");
        return client;
    }

    private static bool IsNewer(string version)
    {
        return Version.TryParse(version, out var remote)
            && Version.TryParse(CurrentVersion, out var local)
            && remote > local;
    }
}
