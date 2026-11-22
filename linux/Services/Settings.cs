using System.Text.Json;

namespace EasyNotch.Services;

public sealed class WidgetSetting
{
    public string Id { get; set; } = "";
    public bool On { get; set; } = true;
}

/// <summary>A place on the clock page: the city's name and its IANA time zone.</summary>
public sealed record ClockPlace(string Name, string Zone)
{
    public string Key => $"{Name}|{Zone}";

    public TimeZoneInfo? TimeZone
    {
        get { try { return TimeZoneInfo.FindSystemTimeZoneById(Zone); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; } }
    }
}

/// <summary>Same settings as the Windows app (same JSON), stored in ~/.local/share/easy-notch.</summary>
public sealed class Settings
{
    /// <summary>Extra places on the clock page (this computer's own zone is always shown first).</summary>
    public List<ClockPlace> Places { get; set; } = [];
    public List<WidgetSetting> Widgets { get; set; } = [];
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public string Noise { get; set; } = "off";
    public bool TuckOverFullScreen { get; set; } = true;
    /// <summary>"pomodoro": focus then break, repeating. "zen": one focus session.</summary>
    public string FocusMode { get; set; } = "pomodoro";
    public bool ClipboardPaused { get; set; }
    /// <summary>Place shown big on the clock page (a <see cref="ClockPlace.Key"/>); null = this computer's.</summary>
    public string? ClockZone { get; set; }
    /// <summary>Set once the starter clock places have been added, so removing them sticks.</summary>
    public bool CitiesSeeded { get; set; }
    /// <summary>Where the weather page looks; null until first chosen (defaults to this computer's time-zone city).</summary>
    public WeatherPlace? WeatherPlace { get; set; }
    /// <summary>Set once "Start when I log in" has been switched on by default, so turning it off sticks.</summary>
    public bool StartupDefaulted { get; set; }
    /// <summary>Check GitHub for a newer release (at most once a day).</summary>
    /// <summary>Weather temperature unit: "C" or "F". Starts from the system region.</summary>
    public string TempUnit { get; set; } = System.Globalization.RegionInfo.CurrentRegion.IsMetric ? "C" : "F";
    public bool CheckForUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }

    public static readonly string[] AllWidgets = ["clock", "weather", "media", "focus", "notes", "clipboard", "mixer", "about"];

    /// <summary>Everything Easy Notch stores lives here ($XDG_DATA_HOME/easy-notch), on this computer only.</summary>
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "easy-notch");

    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        Settings s;
        try { s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { s = new(); }

        // Keep the widget list complete and free of unknown ids, whatever is on disk.
        s.Widgets.RemoveAll(w => !AllWidgets.Contains(w.Id));
        s.Widgets = s.Widgets.DistinctBy(w => w.Id).ToList();
        // New pages slot in after the page that comes before them in the default order.
        for (int i = 0; i < AllWidgets.Length; i++)
        {
            string id = AllWidgets[i];
            if (s.Widgets.Any(w => w.Id == id)) continue;
            int after = i == 0 ? -1 : s.Widgets.FindIndex(w => w.Id == AllWidgets[i - 1]);
            s.Widgets.Insert(after + 1, new WidgetSetting { Id = id });
        }

        if (!s.StartupDefaulted)
        {
            try { LinuxDesktop.SetStartAtLogin(true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            s.StartupDefaulted = true;
            s.Save();
        }
        else if (LinuxDesktop.StartsAtLogin)
        {
            // Keep the autostart entry pointing at this copy, in case the app was moved.
            try { LinuxDesktop.SetStartAtLogin(true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        if (!s.CitiesSeeded)
        {
            foreach (var p in new[] { new ClockPlace("London", "Europe/London"), new ClockPlace("New York", "America/New_York"), new ClockPlace("Tokyo", "Asia/Tokyo") })
                if (!s.Places.Any(x => x.Zone == p.Zone)) s.Places.Add(p);
            s.CitiesSeeded = true;
            s.Save();
        }
        if (s.TempUnit is not ("C" or "F")) s.TempUnit = System.Globalization.RegionInfo.CurrentRegion.IsMetric ? "C" : "F";
        s.FocusMinutes = Math.Clamp(s.FocusMinutes, 1, 180);
        s.BreakMinutes = Math.Clamp(s.BreakMinutes, 1, 60);
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* settings are a convenience; never crash over them */ }
    }
}
