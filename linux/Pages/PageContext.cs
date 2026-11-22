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

    /// <summary>"Europe/London" → "London", "America/Argentina/Buenos_Aires" → "Buenos Aires".</summary>
    public static string City(TimeZoneInfo z)
    {
        string id = z.Id;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) id = iana; // settings copied from Windows
        if (id is "UTC" or "Etc/UTC" or "Etc/GMT" or "GMT" or "Universal") return "UTC";
        int slash = id.LastIndexOf('/');
        string city = (slash >= 0 ? id[(slash + 1)..] : id).Replace('_', ' ');
        return city.Length > 0 ? city : z.StandardName;
    }

    public static string Offset(TimeZoneInfo z, DateTime utc)
    {
        var o = z.GetUtcOffset(utc);
        if (o == TimeSpan.Zero) return "UTC";
        return $"UTC{(o < TimeSpan.Zero ? "−" : "+")}{Math.Abs(o.Hours)}{(o.Minutes != 0 ? $":{Math.Abs(o.Minutes):00}" : "")}";
    }
}

/// <summary>Material Icons Round code points used across the app (stand-ins for the Segoe Fluent glyphs on Windows).</summary>
public static class Glyph
{
    public const string Play = "", Pause = "", Next = "", Prev = "", Stop = "";
    public const string Shuffle = "", Repeat = "", RepeatOne = "";
    public const string Settings = "", Add = "", Remove = "", Delete = "", Close = "", Back = "";
    public const string Pin = "", Checklist = "", Checked = "", Unchecked = "", Check = "";
    public const string Music = "", Clock = "", Timer = "", Link = "", File = "", Apps = "";
    public const string VolUp = "", VolDown = "", VolMute = "", VolOff = "", Mic = "", MicOff = "";
    public const string Up = "", Down = "", ExpandMore = "", Refresh = "", Place = "", Code = "";
}
