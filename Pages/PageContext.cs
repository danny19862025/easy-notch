using EasyNotch.Services;

namespace EasyNotch.Pages;

/// <summary>Everything a page needs from the app.</summary>
public sealed class PageContext
{
    public required Settings Settings { get; init; }
    public required MediaService Media { get; init; }
    public required FocusTimer Focus { get; init; }
    public required ClipboardHistory Clips { get; init; }
    public required NotesStore Notes { get; init; }
    public required WeatherService Weather { get; init; }
    public required UpdateChecker Updates { get; init; }
    /// <summary>Switch the notch to another page by id ("settings", "clock", …).</summary>
    public required Action<string> GoTo { get; init; }
}

public interface IPage
{
    /// <summary>Called each time the page comes into view.</summary>
    void OnShown();
    /// <summary>Called once a second while the page is visible.</summary>
    void OnTick();
}

public static class Fmt
{
    public static string Time(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>The city people think of for common zones (Windows lists them alphabetically).</summary>
    static readonly Dictionary<string, string> KnownCities = new()
    {
        ["GMT Standard Time"] = "London",
        ["Greenwich Standard Time"] = "Reykjavik",
        ["W. Europe Standard Time"] = "Berlin",
        ["Romance Standard Time"] = "Paris",
        ["Central Europe Standard Time"] = "Prague",
        ["Eastern Standard Time"] = "New York",
        ["Central Standard Time"] = "Chicago",
        ["Mountain Standard Time"] = "Denver",
        ["Pacific Standard Time"] = "Los Angeles",
        ["Tokyo Standard Time"] = "Tokyo",
        ["Korea Standard Time"] = "Seoul",
        ["China Standard Time"] = "Beijing",
        ["Singapore Standard Time"] = "Singapore",
        ["India Standard Time"] = "Mumbai",
        ["Arabian Standard Time"] = "Dubai",
        ["AUS Eastern Standard Time"] = "Sydney",
        ["E. Australia Standard Time"] = "Brisbane",
        ["W. Australia Standard Time"] = "Perth",
        ["New Zealand Standard Time"] = "Auckland",
        ["Taipei Standard Time"] = "Taipei",
        ["SE Asia Standard Time"] = "Bangkok",
        ["Russian Standard Time"] = "Moscow",
        ["E. South America Standard Time"] = "São Paulo",
    };

    /// <summary>"(UTC+09:00) Osaka, Sapporo, Tokyo" → "Tokyo" (known zones) or the first city listed.</summary>
    public static string City(TimeZoneInfo z)
    {
        if (KnownCities.TryGetValue(z.Id, out var known)) return known;
        string n = z.DisplayName;
        int close = n.IndexOf(") ", StringComparison.Ordinal);
        if (n.StartsWith("(UTC") && close > 0) n = n[(close + 2)..];
        int comma = n.IndexOf(',');
        return (comma > 0 ? n[..comma] : n).Trim();
    }

    public static string Offset(TimeZoneInfo z, DateTime utc)
    {
        var o = z.GetUtcOffset(utc);
        if (o == TimeSpan.Zero) return "UTC";
        return $"UTC{(o < TimeSpan.Zero ? "−" : "+")}{Math.Abs(o.Hours)}{(o.Minutes != 0 ? $":{Math.Abs(o.Minutes):00}" : "")}";
    }
}
