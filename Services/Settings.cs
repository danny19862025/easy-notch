using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace EasyNotch.Services;

public sealed class WidgetSetting
{
    public string Id { get; set; } = "";
    public bool On { get; set; } = true;
}

public sealed class Settings
{
    public List<WidgetSetting> Widgets { get; set; } = [];
    public List<string> Cities { get; set; } = [];
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public string Noise { get; set; } = "off";
    public bool TuckOverFullScreen { get; set; } = true;

    public static readonly string[] AllWidgets = ["media", "focus", "clipboard", "notes", "clock", "mixer"];

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
        foreach (var id in AllWidgets)
            if (!s.Widgets.Any(w => w.Id == id)) s.Widgets.Add(new WidgetSetting { Id = id });
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
