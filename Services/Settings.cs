using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace EasyNotch.Services;

public sealed class WidgetSetting
{
    public string Id { get; set; } = "";
    public bool On { get; set; } = true;
}

/// <summary>A place on the clock page: the city's name and its time zone (IANA or Windows id).</summary>
public sealed record ClockPlace(string Name, string Zone)
{
    public string Key => $"{Name}|{Zone}";

    public TimeZoneInfo? TimeZone
    {
        get { try { return TimeZoneInfo.FindSystemTimeZoneById(Zone); } catch { return null; } }
    }
}

public sealed class Settings
{
    /// <summary>Extra places on the clock page (this PC's own zone is always shown first).</summary>
    public List<ClockPlace> Places { get; set; } = [];
    public List<WidgetSetting> Widgets { get; set; } = [];
    /// <summary>Older settings stored bare time-zone ids here; they're moved into <see cref="Places"/> on load.</summary>
    public List<string> Cities { get; set; } = [];
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public string Noise { get; set; } = "off";
    public bool TuckOverFullScreen { get; set; } = true;
    /// <summary>"pomodoro": focus then break, repeating. "zen": one focus session.</summary>
    public string FocusMode { get; set; } = "pomodoro";
    public bool ClipboardPaused { get; set; }
    /// <summary>Place shown big on the clock page (a <see cref="ClockPlace.Key"/>); null = this PC's.</summary>
    public string? ClockZone { get; set; }
    /// <summary>Set once the starter clock places have been added, so removing them sticks.</summary>
    public bool CitiesSeeded { get; set; }
    /// <summary>Where the weather page looks; null until first chosen (defaults to this PC's time-zone city).</summary>
    public WeatherPlace? WeatherPlace { get; set; }
    /// <summary>Set once "Start with Windows" has been switched on by default, so turning it off sticks.</summary>
    public bool StartupDefaulted { get; set; }
    /// <summary>Check GitHub for a newer release (at most once a day).</summary>
    /// <summary>Weather temperature unit: "C" or "F". Starts from the system region.</summary>
    public string TempUnit { get; set; } = System.Globalization.RegionInfo.CurrentRegion.IsMetric ? "C" : "F";
    public bool CheckForUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }

    public static readonly string[] AllWidgets = ["clock", "weather", "media", "focus", "notes", "clipboard", "mixer", "about"];

    /// <summary>Everything Easy Notch stores lives here, on this PC only.</summary>
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyNotch");

    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        Settings s;
        try { s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { s = new(); }
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
            try { StartWithWindows.Set(true); } catch { }
            s.StartupDefaulted = true;
            s.Save();
        }
        else if (StartWithWindows.IsOn)
        {
            // Keep the startup entry pointing at this copy, in case the app was moved.
            try { StartWithWindows.Set(true); } catch { }
        }
        if (!s.CitiesSeeded)
        {
            foreach (var id in new[] { "GMT Standard Time", "Eastern Standard Time", "Tokyo Standard Time" })
                if (!s.Cities.Contains(id)) s.Cities.Add(id);
            s.CitiesSeeded = true;
            s.Save();
        }
        if (s.Cities.Count > 0)
        {
            // Move bare zone ids into named places.
            foreach (var id in s.Cities)
            {
                TimeZoneInfo z;
                try { z = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { continue; }
                if (!s.Places.Any(p => p.Zone == id)) s.Places.Add(new ClockPlace(Pages.Fmt.City(z), id));
            }
            if (s.ClockZone is { } cz && !cz.Contains('|'))
                s.ClockZone = s.Places.FirstOrDefault(p => p.Zone == cz)?.Key;
            s.Cities.Clear();
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}

public static class StartWithWindows
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "EasyNotch";

    public static bool IsOn
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string;
        }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on && Environment.ProcessPath is { } exe) k.SetValue(Name, $"\"{exe}\"");
        else k.DeleteValue(Name, throwOnMissingValue: false);
    }
}
