using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;

namespace EasyNotch.Services;

/// <summary>
/// Checks GitHub for a newer release. It only tells you about one and links to the
/// release page; nothing is downloaded or installed automatically.
/// </summary>
public sealed class UpdateChecker
{
    public const string RepoUrl = "https://github.com/danny19862025/easy-notch";
    const string LatestApi = "https://api.github.com/repos/danny19862025/easy-notch/releases/latest";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("EasyNotch-UpdateCheck");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    readonly Settings _settings;

    public UpdateChecker(Settings settings) => _settings = settings;

    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    public bool Checking { get; private set; }
    public Version? Latest { get; private set; }
    public string? ReleaseUrl { get; private set; }
    public string Status { get; private set; } = "";
    public bool UpdateAvailable => Latest is not null && Latest > Current;

    public event Action? Changed;

    /// <summary>Checks at most once a day, if automatic checks are on.</summary>
    public async Task CheckIfDueAsync()
    {
        if (!_settings.CheckForUpdates) return;
        if (DateTime.Now - _settings.LastUpdateCheck < TimeSpan.FromDays(1)) return;
        await CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (Checking) return;
        Checking = true;
        Status = "Checking…";
        Changed?.Invoke();
        try
        {
            using var resp = await Http.GetAsync(LatestApi);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Latest = null;
                Status = "No releases published yet.";
            }
            else
            {
                resp.EnsureSuccessStatusCode();
                var root = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
                string tag = root?["tag_name"]?.GetValue<string>() ?? "";
                ReleaseUrl = root?["html_url"]?.GetValue<string>() ?? $"{RepoUrl}/releases";
                Latest = ParseVersion(tag);
                Status = Latest is null ? $"Latest release \"{tag}\" has no version number."
                    : UpdateAvailable ? $"Version {Latest.Major}.{Latest.Minor}.{Latest.Build} is available."
                    : "You're up to date.";
            }
            _settings.LastUpdateCheck = DateTime.Now;
            _settings.Save();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Status = "Couldn't check for updates. Check your connection.";
        }
        finally
        {
            Checking = false;
            Changed?.Invoke();
        }
    }

    /// <summary>"v1.2.3", "1.2", "release-1.2.3" → Version.</summary>
    static Version? ParseVersion(string tag)
    {
        var m = System.Text.RegularExpressions.Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
            m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }
}
