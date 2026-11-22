using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace EasyNotch.Services;

public sealed record WeatherPlace(string Name, double Lat, double Lon, string? Region)
{
    public string Label => string.IsNullOrEmpty(Region) ? Name : $"{Name}, {Region}";
    /// <summary>IANA time zone of the place, e.g. "Asia/Manila" (from the search).</summary>
    public string? TimeZone { get; init; }
}

public sealed record WeatherNow(double Temp, double Feels, int Code, bool IsDay, double Wind, int Humidity);
public sealed record WeatherHour(DateTime Time, double Temp, int Code, bool IsDay, int RainChance);
public sealed record WeatherDay(DateTime Date, int Code, double Max, double Min, int RainChance);

public sealed class WeatherData
{
    public required WeatherPlace Place { get; init; }
    public required WeatherNow Now { get; init; }
    public required List<WeatherHour> Hours { get; init; }
    public required List<WeatherDay> Days { get; init; }
    public required DateTime Fetched { get; init; }
    public required string TempUnit { get; init; }
    public required string WindUnit { get; init; }
}

/// <summary>
/// Weather from Open-Meteo (free, no account or key). Only the chosen place's
/// coordinates are sent. The last forecast is cached so the page works offline.
/// </summary>
public sealed class WeatherService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(20);
    static string CachePath => Path.Combine(Settings.Dir, "weather.json");

    readonly Settings _settings;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(20) };
    bool _busy;

    public WeatherData? Data { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public WeatherService(Settings settings)
    {
        _settings = settings;
        try { Data = JsonSerializer.Deserialize<WeatherData>(File.ReadAllText(CachePath)); } catch { }
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>"C" or "F", from Settings (the first default follows the system region).</summary>
    public string Unit => _settings.TempUnit == "F" ? "F" : "C";
    bool Metric => Unit == "C";

    public void Start()
    {
        _timer.Start();
        _ = RefreshIfStaleAsync();
    }

    public async Task RefreshIfStaleAsync()
    {
        if (Data is null || DateTime.Now - Data.Fetched > MaxAge || Data.Place.Label != _settings.WeatherPlace?.Label || Data.TempUnit != Unit)
            await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var place = _settings.WeatherPlace;
            if (place is null)
            {
                // First run: start with the city of this PC's time zone.
                place = (await SearchAsync(Pages.Fmt.City(TimeZoneInfo.Local))).FirstOrDefault();
                if (place is null) { Error = "Pick a place to see the weather."; return; }
                _settings.WeatherPlace = place;
                _settings.Save();
            }
            Data = await FetchAsync(place, Metric);
            Error = null;
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(Data));
            }
            catch { }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or NullReferenceException)
        {
            Error = Data is null ? "Couldn't reach the weather service." : $"Offline · updated {TimeAgo.Of(Data.Fetched)}";
        }
        finally
        {
            _busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Switches between °C and °F; the forecast is fetched again in the new unit.</summary>
    public async Task SetUnitAsync(string unit)
    {
        _settings.TempUnit = unit == "F" ? "F" : "C";
        _settings.Save();
        await RefreshAsync();
    }

    public async Task SetPlaceAsync(WeatherPlace place)
    {
        _settings.WeatherPlace = place;
        _settings.Save();
        await RefreshAsync();
    }

    public static async Task<List<WeatherPlace>> SearchAsync(string query)
    {
        var list = new List<WeatherPlace>();
        if (query.Trim().Length < 2) return list;
        string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query.Trim())}&count=5&language=en&format=json";
        var root = JsonNode.Parse(await Http.GetStringAsync(url));
        if (root?["results"] is not JsonArray results) return list;
        foreach (var r in results)
        {
            if (r is null) continue;
            string name = r["name"]?.GetValue<string>() ?? "";
            string? admin = r["admin1"]?.GetValue<string>();
            string? country = r["country"]?.GetValue<string>();
            string? region = admin is not null && admin != name ? $"{admin}{(country is null ? "" : $", {country}")}" : country;
            list.Add(new WeatherPlace(name, r["latitude"]!.GetValue<double>(), r["longitude"]!.GetValue<double>(), region)
            {
                TimeZone = r["timezone"]?.GetValue<string>(),
            });
        }
        return list;
    }

    static async Task<WeatherData> FetchAsync(WeatherPlace p, bool metric)
    {
        string unit = metric ? "celsius" : "fahrenheit";
        string wind = metric ? "kmh" : "mph";
        string url = "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={p.Lat.ToString(CultureInfo.InvariantCulture)}&longitude={p.Lon.ToString(CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m,relative_humidity_2m" +
            "&hourly=temperature_2m,weather_code,is_day,precipitation_probability" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
            $"&timezone=auto&forecast_days=7&temperature_unit={unit}&wind_speed_unit={wind}";
        var root = JsonNode.Parse(await Http.GetStringAsync(url)) ?? throw new JsonException("empty response");

        var c = root["current"]!;
        var now = new WeatherNow(
            D(c["temperature_2m"]), D(c["apparent_temperature"]), I(c["weather_code"]), I(c["is_day"]) == 1,
            D(c["wind_speed_10m"]), I(c["relative_humidity_2m"]));

        // Times are in the place's own time zone; compare against "now" there.
        int offset = I(root["utc_offset_seconds"]);
        var placeNow = DateTime.UtcNow.AddSeconds(offset);
        var h = root["hourly"]!;
        var hourTimes = (JsonArray)h["time"]!;
        var hours = new List<WeatherHour>();
        for (int i = 0; i < hourTimes.Count && hours.Count < 8; i++)
        {
            var t = DateTime.Parse(hourTimes[i]!.GetValue<string>(), CultureInfo.InvariantCulture);
            if (t < placeNow.AddMinutes(-placeNow.Minute).AddHours(1)) continue; // start at the next full hour
            hours.Add(new WeatherHour(t, D(h["temperature_2m"]![i]), I(h["weather_code"]![i]), I(h["is_day"]![i]) == 1, I(h["precipitation_probability"]![i])));
        }

        var d = root["daily"]!;
        var dayTimes = (JsonArray)d["time"]!;
        var days = new List<WeatherDay>();
        for (int i = 0; i < dayTimes.Count && days.Count < 6; i++)
            days.Add(new WeatherDay(DateTime.Parse(dayTimes[i]!.GetValue<string>(), CultureInfo.InvariantCulture),
                I(d["weather_code"]![i]), D(d["temperature_2m_max"]![i]), D(d["temperature_2m_min"]![i]), I(d["precipitation_probability_max"]![i])));

        return new WeatherData
        {
            Place = p, Now = now, Hours = hours, Days = days, Fetched = DateTime.Now,
            TempUnit = metric ? "C" : "F", WindUnit = metric ? "km/h" : "mph",
        };
    }

    static double D(JsonNode? n) => n is null ? 0 : n.GetValue<double>();
    static int I(JsonNode? n) => n is null ? 0 : (int)Math.Round(n.GetValue<double>());

    // ------------------------------------------------------------ WMO weather codes

    public static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 or 81 => "Showers",
        82 => "Heavy showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm, hail",
        _ => "—",
    };

    /// <summary>Icon family for a code: clear, partly, cloud, fog, rain, snow, storm.</summary>
    public static string IconKind(int code) => code switch
    {
        0 or 1 => "clear",
        2 => "partly",
        3 => "cloud",
        45 or 48 => "fog",
        >= 51 and <= 67 or >= 80 and <= 82 => "rain",
        >= 71 and <= 77 or 85 or 86 => "snow",
        >= 95 => "storm",
        _ => "cloud",
    };
}
