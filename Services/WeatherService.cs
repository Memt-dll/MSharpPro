using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Windows.Devices.Geolocation;

using static MSharpPro.Services.Loc;

namespace MSharpPro.Services;

/// <summary>
/// Hava durumu: Open-Meteo (ücretsiz, API anahtarı gerektirmez).
/// HttpClient yalnızca istek anında oluşturulur; 30 dakikada bir yenilenir.
/// </summary>
public sealed class WeatherService
{
    /// <summary>Bir gün. Bar* değerleri, sıcaklık aralığı çubuğunun konumu içindir (iPhone hava durumu gibi).</summary>
    public sealed record Day(string Name, System.Windows.Media.ImageSource Icon, int Max, int Min)
    {
        public System.Windows.GridLength BarLeft { get; init; }
        public System.Windows.GridLength BarFill { get; init; }
        public System.Windows.GridLength BarRight { get; init; }
    }

    /// <summary>Şehir aramasında bir sonuç.</summary>
    public sealed record Place(string Name, string Detail, double Lat, double Lon)
    {
        public string Label => Detail.Length > 0 ? $"{Name}, {Detail}" : Name;
        public override string ToString() => Label;
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Tr = new("tr-TR");

    private readonly AppSettings _settings;
    private bool _busy;

    public bool HasData { get; private set; }
    public int Temp { get; private set; }
    public int Feels { get; private set; }
    public int Humidity { get; private set; }
    public int Wind { get; private set; }
    public WeatherKind Kind { get; private set; } = WeatherKind.Sun;
    public System.Windows.Media.ImageSource Icon => WeatherIcons.Get(Kind);
    public System.Windows.Media.Brush CardBrush => WeatherIcons.CardBrush(Kind);
    public int High { get; private set; }
    public int Low { get; private set; }
    public string Condition { get; private set; } = "";
    public IReadOnlyList<Day> Days { get; private set; } = Array.Empty<Day>();
    public DateTime? Updated { get; private set; }
    /// <summary>Gösterilecek bir sorun varsa (konum seçilmemiş, bağlantı yok...).</summary>
    public string? Problem { get; private set; }

    /// <summary>Veri değişti (UI thread).</summary>
    public event Action? Changed;

    public WeatherService(AppSettings settings) => _settings = settings;

    public async Task RefreshAsync()
    {
        if (_busy || !_settings.WeatherEnabled) return;
        _busy = true;
        try
        {
            if (_settings.WeatherLat is null || _settings.WeatherLon is null)
            {
                // Şehir seçilmemiş: Windows konumu izinliyse onu kullan (sessizce)
                if (!await TryUseDeviceLocationAsync())
                {
                    Problem = L("Ayarlar'dan şehrinizi seçin");
                    HasData = false;
                    Changed?.Invoke();
                    return;
                }
            }

            string url = string.Format(Inv,
                "https://api.open-meteo.com/v1/forecast?latitude={0:0.####}&longitude={1:0.####}" +
                "&current=temperature_2m,apparent_temperature,weather_code,is_day,relative_humidity_2m,wind_speed_10m" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=4",
                _settings.WeatherLat, _settings.WeatherLon);

            using var http = CreateClient();
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");

            Temp = (int)Math.Round(cur.GetProperty("temperature_2m").GetDouble());
            Feels = (int)Math.Round(cur.GetProperty("apparent_temperature").GetDouble());
            Humidity = (int)Math.Round(cur.GetProperty("relative_humidity_2m").GetDouble());
            Wind = (int)Math.Round(cur.GetProperty("wind_speed_10m").GetDouble());
            bool isDay = cur.GetProperty("is_day").GetInt32() == 1;
            (Kind, Condition) = Describe(cur.GetProperty("weather_code").GetInt32(), isDay);

            var daily = doc.RootElement.GetProperty("daily");
            var times = daily.GetProperty("time").EnumerateArray().Select(t => t.GetString() ?? "").ToList();
            var codes = daily.GetProperty("weather_code").EnumerateArray().Select(c => c.GetInt32()).ToList();
            var maxs = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(c => c.GetDouble()).ToList();
            var mins = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(c => c.GetDouble()).ToList();

            // Aralık çubukları: tüm günlerin en düşük / en yüksek sıcaklığına göre konumlanır
            double lo = mins.Min(), hi = maxs.Max(), span = Math.Max(1, hi - lo);
            var days = new List<Day>();
            for (int i = 0; i < times.Count; i++)
            {
                string name = i == 0
                    ? L("Bugün")
                    : DateTime.TryParse(times[i], Inv, DateTimeStyles.None, out var d)
                        ? (Loc.English ? System.Globalization.CultureInfo.GetCultureInfo("en-US") : Tr).DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek)
                        : "";
                double left = (mins[i] - lo) / span, fill = Math.Max(0.06, (maxs[i] - mins[i]) / span);
                days.Add(new Day(name, WeatherIcons.Get(Describe(codes[i], true).Kind), (int)Math.Round(maxs[i]), (int)Math.Round(mins[i]))
                {
                    BarLeft = new System.Windows.GridLength(left, System.Windows.GridUnitType.Star),
                    BarFill = new System.Windows.GridLength(fill, System.Windows.GridUnitType.Star),
                    BarRight = new System.Windows.GridLength(Math.Max(0, 1 - left - fill), System.Windows.GridUnitType.Star)
                });
            }
            Days = days;
            High = days.Count > 0 ? days[0].Max : Temp;
            Low = days.Count > 0 ? days[0].Min : Temp;

            HasData = true;
            Problem = null;
            Updated = DateTime.Now;
        }
        catch (Exception ex)
        {
            Problem = HasData ? null : L("Hava durumu alınamadı");
            App.LogError(ex);
        }
        finally
        {
            _busy = false;
        }
        Changed?.Invoke();
    }

    /// <summary>Windows konum servisiyle yaklaşık konumu alır ve ayarlara kaydeder.</summary>
    public async Task<bool> TryUseDeviceLocationAsync()
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed) return false;
            var geo = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var pos = await geo.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10));
            _settings.WeatherLat = Math.Round(pos.Coordinate.Point.Position.Latitude, 3);
            _settings.WeatherLon = Math.Round(pos.Coordinate.Point.Position.Longitude, 3);
            _settings.WeatherCity = "Konumum";
            _settings.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Şehir adıyla arar (Open-Meteo geocoding).</summary>
    public static async Task<List<Place>> SearchAsync(string query)
    {
        var list = new List<Place>();
        if (string.IsNullOrWhiteSpace(query)) return list;
        try
        {
            using var http = CreateClient();
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=6&language=tr&format=json&name="
                         + Uri.EscapeDataString(query.Trim());
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
            foreach (var r in results.EnumerateArray())
            {
                string name = r.GetProperty("name").GetString() ?? "";
                string admin = r.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "";
                string country = r.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
                string detail = string.Join(", ", new[] { admin, country }.Where(x => x.Length > 0 && x != name));
                list.Add(new Place(name, detail, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()));
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
        return list;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MSharpPro-Weather");
        return http;
    }

    /// <summary>WMO hava kodu → simge türü ve Türkçe açıklama.</summary>
    private static (WeatherKind Kind, string Text) Describe(int code, bool day) => code switch
    {
        0 => (day ? WeatherKind.Sun : WeatherKind.Moon, day ? L("Güneşli") : (Loc.English ? "Clear" : "Açık")),
        1 => (day ? WeatherKind.PartlySun : WeatherKind.PartlyMoon, L("Az bulutlu")),
        2 => (day ? WeatherKind.PartlySun : WeatherKind.PartlyMoon, L("Parçalı bulutlu")),
        3 => (WeatherKind.Cloud, L("Kapalı")),
        45 or 48 => (WeatherKind.Fog, L("Sisli")),
        >= 51 and <= 57 => (WeatherKind.Drizzle, L("Çisenti")),
        >= 61 and <= 67 => (WeatherKind.Rain, L("Yağmurlu")),
        >= 71 and <= 77 => (WeatherKind.Snow, L("Karlı")),
        >= 80 and <= 82 => (WeatherKind.Rain, L("Sağanak yağışlı")),
        85 or 86 => (WeatherKind.Snow, L("Kar sağanağı")),
        >= 95 => (WeatherKind.Thunder, L("Gök gürültülü")),
        _ => (WeatherKind.Cloud, L("Bulutlu"))
    };
}