using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using static MSharpPro.Services.Loc;

namespace MSharpPro.Services;

/// <summary>
/// Şarkı sözleri: ücretsiz ve anahtarsız LRCLIB (lrclib.net) servisinden, zamanlı (LRC) olarak.
/// Yalnızca "Sözler" sekmesi açılınca ve şarkı değişince istenir; sonuç bellekte tutulur.
/// </summary>
public sealed partial class LyricsService
{
    public sealed record Line(TimeSpan Time, string Text);

    private readonly Dictionary<string, (List<Line>? Lines, string? Plain)> _cache = new();
    private string _key = "";
    private Task? _loading;

    public string Key => _key;
    public List<Line>? Lines { get; private set; }
    public string? Plain { get; private set; }
    public string Status { get; private set; } = "";

    public event Action? Changed;

    [GeneratedRegex(@"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$")]
    private static partial Regex LrcLine();

    /// <summary>Çalan şarkının sözlerini getirir (önbellekte varsa hemen).</summary>
    public void Load(string title, string artist, TimeSpan? duration)
    {
        string key = $"{artist}|{title}";
        if (key == _key && (_loading != null || Lines != null || Plain != null || Status.Length > 0)) return;
        _key = key;
        Lines = null; Plain = null;

        if (string.IsNullOrWhiteSpace(title))
        {
            Status = L("Şu anda çalan bir şey yok");
            Changed?.Invoke();
            return;
        }
        if (_cache.TryGetValue(key, out var hit))
        {
            (Lines, Plain) = hit;
            Status = Lines == null && Plain == null ? L("Bu şarkının sözleri bulunamadı") : "";
            Changed?.Invoke();
            return;
        }
        Status = L("Sözler aranıyor…");
        Changed?.Invoke();
        _loading = FetchAsync(key, title, artist, duration);
    }

    private async Task FetchAsync(string key, string title, string artist, TimeSpan? duration)
    {
        List<Line>? lines = null;
        string? plain = null;
        bool failed = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MSharpPro (https://github.com/Memt-dll/MSharpPro)");
            string url = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title)
                         + (artist.Length > 0 ? "&artist_name=" + Uri.EscapeDataString(artist) : "");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));

            // En iyi eşleşme: süresi en yakın ve zamanlı sözü olan
            JsonElement? best = null;
            double bestScore = double.MaxValue;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                bool synced = item.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String;
                bool hasPlain = item.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String;
                if (!synced && !hasPlain) continue;
                double diff = duration is { TotalSeconds: > 0 } d && item.TryGetProperty("duration", out var du) && du.TryGetDouble(out var dv)
                    ? Math.Abs(dv - d.TotalSeconds) : 0;
                double score = diff + (synced ? 0 : 30);
                if (score < bestScore) { bestScore = score; best = item; }
            }

            if (best is { } b)
            {
                if (b.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String)
                    lines = Parse(s.GetString() ?? "");
                if ((lines == null || lines.Count == 0) && b.TryGetProperty("plainLyrics", out var p) && p.ValueKind == JsonValueKind.String)
                {
                    lines = null;
                    plain = p.GetString();
                }
            }
        }
        catch { failed = true; }

        if (!failed) _cache[key] = (lines, plain);
        if (key != _key) return; // bu arada şarkı değişti
        Lines = lines; Plain = plain;
        Status = failed ? L("Sözler alınamadı (internet bağlantısını kontrol edin)")
            : lines == null && plain == null ? L("Bu şarkının sözleri bulunamadı") : "";
        _loading = null;
        Changed?.Invoke();
    }

    private static List<Line> Parse(string lrc)
    {
        var list = new List<Line>();
        foreach (var raw in lrc.Split('\n'))
        {
            var m = LrcLine().Match(raw.Trim());
            if (!m.Success) continue;
            var t = TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value))
                    + TimeSpan.FromSeconds(double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            list.Add(new Line(t, m.Groups[3].Value.Trim()));
        }
        return list;
    }

    /// <summary>Verilen konumda söylenen satırın sırası (-1: henüz başlamadı).</summary>
    public int IndexAt(TimeSpan pos)
    {
        if (Lines == null) return -1;
        int idx = -1;
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Time <= pos + TimeSpan.FromMilliseconds(250)) idx = i; else break;
        }
        return idx;
    }
}
