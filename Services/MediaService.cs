using System.Windows.Media;
using System.Windows.Threading;
using Windows.Media.Control;

using static MSharpPro.Services.Loc;

namespace MSharpPro.Services;

/// <summary>
/// Windows'un medya oturumlarını (Spotify, Chrome/YouTube, Edge, Medya Oynatıcı...) okur ve kontrol eder.
/// Tüm olaylar UI thread'ine aktarılır; bu yüzden tek bir örnek bütün çentik pencerelerince paylaşılır.
/// </summary>
public sealed class MediaService
{
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _scan;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _ready;
    private int _refreshId;
    private string _artKey = "";

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string AppName { get; private set; } = "";
    public bool IsPlaying { get; private set; }
    /// <summary>Kaynak uygulama ileri/geri sarmayı destekliyor mu.</summary>
    public bool CanSeek { get; private set; }
    public ImageSource? Art { get; private set; }
    /// <summary>Bulanık arka plan için çok küçük çözülmüş kapak.</summary>
    public ImageSource? AmbientArt { get; private set; }
    public bool HasMedia => (_session != null || Demo.On) && Title.Length > 0;

    /// <summary>Herhangi bir bilgi değişti (UI thread).</summary>
    public event Action? Changed;
    /// <summary>Yeni bir şarkı / video başladı (UI thread).</summary>
    public event Action? TrackChanged;

    public MediaService(Dispatcher ui)
    {
        _ui = ui;
        // Hiçbir şey çalmıyorken, başka bir uygulamanın (ör. YouTube sekmesinin) çalmaya
        // başlayıp başlamadığını hafifçe kontrol eder. Bir şey çalarken durur.
        _scan = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2.5) };
        _scan.Tick += (_, _) => PickSession();
    }

    public async Task StartAsync()
    {
        if (Demo.On)
        {
            Title = Demo.Title; Artist = Demo.Artist; AppName = Demo.App;
            IsPlaying = true; CanSeek = true;
            Art = AmbientArt = Demo.Cover();
            _ready = true;
            Changed?.Invoke();
            return;
        }
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(PickSession);
            _manager.SessionsChanged += (_, _) => _ui.InvokeAsync(PickSession);
            await AttachAsync(Choose());
        }
        catch { /* medya API'si yoksa sessizce geç */ }
        _ready = true;
        UpdateScan();
    }

    // ------------------------------------------------------------------ oturum seçimi

    /// <summary>
    /// Çalan bir oturum varsa onu seçer (Windows'un "geçerli" saydığı oturum bazen durdurulmuş
    /// başka bir uygulamada takılı kalır; YouTube'un görünmemesinin sebebi buydu).
    /// </summary>
    private GlobalSystemMediaTransportControlsSession? Choose()
    {
        if (_manager == null) return null;
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> all;
        try { all = _manager.GetSessions(); }
        catch { return _session; }

        bool currentAlive = _session != null && all.Any(x =>
            ReferenceEquals(x, _session) || x.SourceAppUserModelId == _session.SourceAppUserModelId);

        if (currentAlive && IsPlayingSession(_session!)) return _session;

        GlobalSystemMediaTransportControlsSession? system = null;
        try { system = _manager.GetCurrentSession(); } catch { }
        if (system != null && IsPlayingSession(system)) return currentAlive && Same(system, _session) ? _session : system;

        foreach (var s in all)
            if (IsPlayingSession(s)) return s;

        if (currentAlive) return _session;
        return system ?? all.FirstOrDefault();
    }

    private static bool Same(GlobalSystemMediaTransportControlsSession? a, GlobalSystemMediaTransportControlsSession? b)
        => ReferenceEquals(a, b);

    private static bool IsPlayingSession(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { return false; }
    }

    private void PickSession()
    {
        var s = Choose();
        if (!Same(s, _session))
        {
            _ = AttachAsync(s);
        }
        else
        {
            bool was = IsPlaying;
            ReadPlayback();
            if (was != IsPlaying) Changed?.Invoke();
        }
        UpdateScan();
    }

    private void UpdateScan() => _scan.IsEnabled = _manager != null && !IsPlaying;

    private async Task AttachAsync(GlobalSystemMediaTransportControlsSession? s)
    {
        if (_session != null)
        {
            try
            {
                _session.MediaPropertiesChanged -= OnProps;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            catch { }
        }
        _session = s;
        if (s != null)
        {
            s.MediaPropertiesChanged += OnProps;
            s.PlaybackInfoChanged += OnPlayback;
            s.TimelinePropertiesChanged += OnTimeline;
        }
        else
        {
            Title = Artist = "";
            Art = AmbientArt = null;
            _artKey = "";
        }
        AppName = s != null ? PrettyAppName(s.SourceAppUserModelId) : "";
        ReadPlayback();
        UpdateScan();
        await RefreshPropsAsync();
    }

    private void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e)
        => _ui.InvokeAsync(() => _ = RefreshPropsAsync());

    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
        => _ui.InvokeAsync(() =>
        {
            ReadPlayback();
            UpdateScan();
            Changed?.Invoke();
        });

    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e)
        => _ui.InvokeAsync(() => TimelineChanged?.Invoke());

    /// <summary>Şarkı konumu değişti (ör. ileri sarıldı).</summary>
    public event Action? TimelineChanged;

    private void ReadPlayback()
    {
        try
        {
            var info = _session?.GetPlaybackInfo();
            IsPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            CanSeek = info?.Controls?.IsPlaybackPositionEnabled == true;
        }
        catch
        {
            IsPlaying = false;
            CanSeek = false;
        }
    }

    private async Task RefreshPropsAsync()
    {
        int id = ++_refreshId;
        var s = _session;
        if (s == null) { Changed?.Invoke(); return; }
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (id != _refreshId) return; // daha yeni bir güncelleme başladı

            string title = p?.Title ?? "";
            bool newTrack = title.Length > 0 && title != Title;
            Title = title;
            Artist = p?.Artist ?? "";

            // Chrome önce başlığı, biraz sonra kapağı gönderir: kapağı her seferinde kontrol et,
            // ama yalnızca gerçekten değiştiyse yeniden çöz (RAM/CPU tasarrufu).
            var bytes = await ImageUtil.ReadBytesAsync(p?.Thumbnail);
            if (id != _refreshId) return;
            string key = ArtKey(bytes);
            if (key != _artKey)
            {
                _artKey = key;
                Art = ImageUtil.FromBytes(bytes, 200);
                AmbientArt = ImageUtil.FromBytes(bytes, 14);
            }

            ReadPlayback();
            Changed?.Invoke();
            if (newTrack && _ready) TrackChanged?.Invoke();
        }
        catch { }
    }

    private static string ArtKey(byte[]? b)
    {
        if (b == null || b.Length == 0) return "";
        unchecked
        {
            int h = 17;
            int step = Math.Max(1, b.Length / 64);
            for (int i = 0; i < b.Length; i += step) h = h * 31 + b[i];
            return $"{b.Length}:{h}";
        }
    }

    // ------------------------------------------------------------------ zaman çizelgesi / kontrol

    /// <summary>Şarkının o anki konumu ve toplam süresi (bilinmiyorsa null).</summary>
    public (TimeSpan Position, TimeSpan Duration)? GetProgress()
    {
        if (Demo.On) return (Demo.Position, Demo.Duration);
        try
        {
            var t = _session?.GetTimelineProperties();
            if (t == null) return null;
            var dur = t.EndTime - t.StartTime;
            if (dur <= TimeSpan.Zero) return null;

            var pos = t.Position - t.StartTime;
            if (IsPlaying && t.LastUpdatedTime.Year > 2000)
                pos += DateTimeOffset.Now - t.LastUpdatedTime;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (pos > dur) pos = dur;
            return (pos, dur);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Şarkıyı verilen konuma sarar.</summary>
    public async Task SeekAsync(TimeSpan position)
    {
        var s = _session;
        if (s == null || !CanSeek) return;
        try
        {
            var t = s.GetTimelineProperties();
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            var dur = t.EndTime - t.StartTime;
            if (dur > TimeSpan.Zero && position > dur) position = dur - TimeSpan.FromSeconds(1);
            await s.TryChangePlaybackPositionAsync((t.StartTime + position).Ticks);
        }
        catch { }
    }

    public Task PlayPauseAsync() => _session?.TryTogglePlayPauseAsync().AsTask() ?? Task.CompletedTask;
    public Task NextAsync() => _session?.TrySkipNextAsync().AsTask() ?? Task.CompletedTask;
    public Task PreviousAsync() => _session?.TrySkipPreviousAsync().AsTask() ?? Task.CompletedTask;

    private static string PrettyAppName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        string n = id.Trim().TrimEnd('.');
        int bang = n.IndexOf('!');
        if (bang > 0) n = n[..bang];
        int us = n.IndexOf('_');
        if (us > 0) n = n[..us];
        n = n.TrimEnd('.');
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        int dot = n.LastIndexOf('.');
        if (dot >= 0 && dot < n.Length - 1) n = n[(dot + 1)..];

        return n.ToLowerInvariant() switch
        {
            "msedge" => "Microsoft Edge",
            "chrome" => "Google Chrome",
            "firefox" or "308046b0af4a39cb" => "Firefox",
            "opera" => "Opera",
            "brave" => "Brave",
            "zunemusic" => L("Medya Oynatıcı"),
            "spotify" => "Spotify",
            _ => n.Length > 0 ? char.ToUpper(n[0]) + n[1..] : n
        };
    }
}
