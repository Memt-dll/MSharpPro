using System.Windows.Media;
using Windows.Media.Control;

namespace MSharpPro.Services;

/// <summary>Windows'un medya oturumlarını (Spotify, tarayıcı, Medya Oynatıcı...) okur ve kontrol eder.</summary>
public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _ready;

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string AppName { get; private set; } = "";
    public bool IsPlaying { get; private set; }
    public ImageSource? Art { get; private set; }
    /// <summary>Bulanık arka plan için çok küçük çözülmüş kapak.</summary>
    public ImageSource? AmbientArt { get; private set; }
    public bool HasMedia => _session != null && Title.Length > 0;

    /// <summary>Herhangi bir bilgi değişti (arka plan thread'inden gelebilir).</summary>
    public event Action? Changed;
    /// <summary>Yeni bir şarkıya geçildi.</summary>
    public event Action? TrackChanged;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ = AttachAsync(_manager.GetCurrentSession());
            await AttachAsync(_manager.GetCurrentSession());
        }
        catch { /* medya API'si yoksa sessizce geç */ }
        _ready = true;
    }

    private async Task AttachAsync(GlobalSystemMediaTransportControlsSession? s)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
        }
        _session = s;
        if (s != null)
        {
            s.MediaPropertiesChanged += OnProps;
            s.PlaybackInfoChanged += OnPlayback;
        }
        else
        {
            Title = Artist = AppName = "";
            Art = AmbientArt = null;
            IsPlaying = false;
        }
        AppName = s != null ? PrettyAppName(s.SourceAppUserModelId) : "";
        ReadPlayback();
        await RefreshPropsAsync();
    }

    private void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshPropsAsync();

    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
    {
        ReadPlayback();
        Changed?.Invoke();
    }

    private void ReadPlayback()
    {
        try
        {
            IsPlaying = _session?.GetPlaybackInfo()?.PlaybackStatus ==
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { IsPlaying = false; }
    }

    private async Task RefreshPropsAsync()
    {
        var s = _session;
        if (s == null) { Changed?.Invoke(); return; }
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            string title = p?.Title ?? "";
            bool newTrack = title.Length > 0 && title != Title;

            Title = title;
            Artist = p?.Artist ?? "";
            if (newTrack || Art == null)
            {
                var bytes = await ImageUtil.ReadBytesAsync(p?.Thumbnail);
                Art = ImageUtil.FromBytes(bytes, 200);
                AmbientArt = ImageUtil.FromBytes(bytes, 14);
            }

            Changed?.Invoke();
            if (newTrack && _ready) TrackChanged?.Invoke();
        }
        catch { }
    }

    /// <summary>Şarkının o anki konumu ve toplam süresi (bilinmiyorsa null).</summary>
    public (TimeSpan Position, TimeSpan Duration)? GetProgress()
    {
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
            "zunemusic" => "Medya Oynatıcı",
            "spotify" => "Spotify",
            _ => n.Length > 0 ? char.ToUpper(n[0]) + n[1..] : n
        };
    }
}
