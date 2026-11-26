using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MSharpPro.Services;

namespace MSharpPro;

/// <summary>
/// Bir ekrandaki çentik. Servisler <see cref="AppHost"/>'ta tek kez çalışır; bu pencere yalnızca gösterir.
/// </summary>
public partial class MainWindow : Window
{
    private enum Mode { Compact, Peek, Expanded }

    private readonly AppHost _host;
    private readonly AppSettings _settings;
    private readonly MediaService _media;
    private MonitorInfo _monitor;

    private readonly Queue<PeekItem> _peekQueue = new();
    private readonly DispatcherTimer _hoverTimer, _leaveTimer, _peekTimer, _trimTimer, _fullscreenTimer, _progressTimer;
    private readonly DispatcherTimer _rehideTimer, _revealCheckTimer;
    private readonly Storyboard? _eq;
    private bool _eqRunning;
    private Mode _mode = Mode.Compact;
    private bool _hiddenForFullscreen;
    private IntPtr _hwnd;
    private bool _closed;

    /// <summary>Gizli moddayken fare üst kenara gelip çentiği indirdi mi.</summary>
    private bool _revealed;
    private bool _slidUp;
    private bool _wasHidden;

    // İleri / geri sarma
    private bool _seeking;
    private TimeSpan _seekDuration;
    private TimeSpan _pendingSeek;
    private DateTime _pendingSeekUntil;

    // Ölçeklenmemiş pencere boyutu
    private const double BaseWidth = 480, BaseHeight = 270;

    public MainWindow(AppHost host, MonitorInfo monitor)
    {
        _host = host;
        _settings = host.Settings;
        _media = host.Media;
        _monitor = monitor;
        _wasHidden = _settings.Hidden;

        InitializeComponent();

        BtList.ItemsSource = host.Bluetooth.Devices;
        NotifList.ItemsSource = host.NotifItems;

        _hoverTimer = MakeTimer(140, () => { if (Island.IsMouseOver) SetMode(Mode.Expanded); });
        _leaveTimer = MakeTimer(350, () =>
        {
            if (Island.IsMouseOver || _seeking) return;
            if (_peekQueue.Count > 0) ShowPeekNow(_peekQueue.Dequeue());
            else SetMode(Mode.Compact);
        });
        _peekTimer = MakeTimer(3500, () =>
        {
            if (_mode != Mode.Peek) return;
            if (_peekQueue.Count > 0) ShowPeekNow(_peekQueue.Dequeue());
            else SetMode(Mode.Compact);
        });
        _rehideTimer = MakeTimer(700, () =>
        {
            if (Island.IsMouseOver || HotZone.IsMouseOver || _mode != Mode.Compact) return;
            _revealed = false;
            ApplySlide();
        });
        _revealCheckTimer = MakeTimer(320, () =>
        {
            if (Island.IsMouseOver) _hoverTimer.Start();
            else _rehideTimer.Start();
        });
        _trimTimer = MakeTimer(3000, () => { if (_mode == Mode.Compact) Native.TrimWorkingSet(); });
        _fullscreenTimer = MakeTimer(2000, CheckFullscreen, repeat: true);
        _progressTimer = MakeTimer(1000, UpdateProgress, repeat: true);
        _eq = (Storyboard)Resources["EqStoryboard"];

        Island.MouseEnter += (_, _) => { _leaveTimer.Stop(); _hoverTimer.Start(); };
        Island.MouseLeave += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_mode == Mode.Expanded) _leaveTimer.Start();
            else if (_settings.Hidden && _revealed) _rehideTimer.Start();
        };
        Island.MouseRightButtonUp += (_, e) => { e.Handled = true; _host.ShowMenu(); };
        HotZone.MouseEnter += (_, _) => Reveal();

        HookSeekBar();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(_hwnd);
            ApplyLayout(); // gösterilmeden önce doğru ekrana yerleş
        };
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(PositionWindow, DispatcherPriority.Background);
        Loaded += (_, _) => Init();
        Closed += (_, _) => Detach();
    }

    private void Init()
    {
        _settings.Changed += OnSettingsChanged;
        _media.Changed += UpdateMedia;
        _media.TimelineChanged += UpdateProgress;
        _host.NotificationsChanged += UpdateNotifUi;
        _host.Bluetooth.Devices.CollectionChanged += OnBtChanged;

        ApplyFont();
        SetMode(Mode.Compact);
        UpdateMedia();
        UpdateNotifUi();
        OnBtChanged(null, EventArgs.Empty);
        _fullscreenTimer.Start();
        _trimTimer.Start();
    }

    private void Detach()
    {
        _closed = true;
        _settings.Changed -= OnSettingsChanged;
        _media.Changed -= UpdateMedia;
        _media.TimelineChanged -= UpdateProgress;
        _host.NotificationsChanged -= UpdateNotifUi;
        _host.Bluetooth.Devices.CollectionChanged -= OnBtChanged;
        foreach (var t in new[] { _hoverTimer, _leaveTimer, _peekTimer, _trimTimer, _fullscreenTimer, _progressTimer, _rehideTimer, _revealCheckTimer })
            t.Stop();
        _eq?.Stop(this);
        BtList.ItemsSource = null;
        NotifList.ItemsSource = null;
    }

    private void OnBtChanged(object? sender, EventArgs e) => BtEmpty.Visibility = Vis(_host.Bluetooth.Devices.Count == 0);

    // ---------------------------------------------------------------- ekran / ayarlar

    public void SetMonitor(MonitorInfo m)
    {
        _monitor = m;
        PositionWindow();
    }

    private void OnSettingsChanged()
    {
        if (_closed) return;
        ApplyLayout();
        ApplyFont();

        if (!_settings.HideInFullscreen && _hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            Show();
        }

        if (_settings.Hidden != _wasHidden)
        {
            _wasHidden = _settings.Hidden;
            _revealed = false;
            if (_settings.Hidden && _mode == Mode.Expanded) SetMode(Mode.Compact);
        }
        ApplySlide();
    }

    private void ApplyLayout()
    {
        double s = Math.Clamp(_settings.Scale, AppSettings.MinScale, AppSettings.MaxScale);
        Root.LayoutTransform = Math.Abs(s - 1) < 0.001 ? Transform.Identity : new ScaleTransform(s, s);
        Width = BaseWidth * s;
        Height = BaseHeight * s;
        PositionWindow();
    }

    private void ApplyFont()
    {
        try { FontFamily = new FontFamily(_settings.FontFamily + ", Segoe UI"); } catch { }
    }

    /// <summary>Pencereyi kendi ekranının üst ortasına yerleştirir (farklı DPI'lı ekranlarda da).</summary>
    private void PositionWindow()
    {
        if (_hwnd == IntPtr.Zero || _closed) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int pxWidth = (int)Math.Round(Width * dpi.DpiScaleX);
        int x = _monitor.Left + (_monitor.Width - pxWidth) / 2;
        Native.MoveTo(_hwnd, x, _monitor.Top);
    }

    private void CheckFullscreen()
    {
        bool fs = _settings.HideInFullscreen && Native.IsFullscreenOn(_monitor);
        if (!fs && !_hiddenForFullscreen) Native.BringToTop(_hwnd);
        if (fs == _hiddenForFullscreen) return;
        _hiddenForFullscreen = fs;
        if (fs) Hide();
        else { Show(); Native.BringToTop(_hwnd); }
        UpdateEqualizer();
    }

    // ---------------------------------------------------------------- durum / animasyon

    private void SetMode(Mode m)
    {
        _mode = m;
        (double w, double h, double r, FrameworkElement view) = m switch
        {
            Mode.Expanded => (450, 236, 30, ExpandedView),
            Mode.Peek => (384, 66, 24, PeekView),
            _ => (_media.HasMedia ? 236 : 170, 32, 16, (FrameworkElement)CompactView)
        };

        if (m == Mode.Expanded)
        {
            ClockText.Text = DateTime.Now.ToString("ddd HH:mm");
            if (TabNotif.IsChecked == true) _host.MarkRead();
            UpdateProgress();
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
        }

        if (m != Mode.Compact) Native.BringToTop(_hwnd);
        Island.CornerRadius = new CornerRadius(0, 0, r, r);
        AnimateSize(w, h);
        ShowView(view);
        UpdateAmbient();
        ApplySlide();

        if (m == Mode.Compact)
        {
            _trimTimer.Stop(); _trimTimer.Start();
            if (_settings.Hidden && _revealed) { _rehideTimer.Stop(); _rehideTimer.Start(); }
        }
    }

    private void AnimateSize(double w, double h)
    {
        var dur = new Duration(TimeSpan.FromMilliseconds(340));
        var ease = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };
        Island.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
        Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
    }

    private void ShowView(FrameworkElement view)
    {
        foreach (var v in new FrameworkElement[] { CompactView, PeekView, ExpandedView })
        {
            if (v == view) continue;
            v.BeginAnimation(OpacityProperty, null);
            v.Opacity = 0;
            v.Visibility = Visibility.Collapsed;
        }
        if (view.Visibility == Visibility.Visible && view.Opacity >= 1) return;

        view.Visibility = Visibility.Visible;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(110)
        });
    }

    // ---------------------------------------------------------------- gizleme (Ctrl + Win)

    /// <summary>Gizli moddaysa ve fare yoksa çentiği yukarı kaydırır; aksi halde aşağı indirir.</summary>
    private void ApplySlide()
    {
        bool up = _settings.Hidden && !_revealed && _mode == Mode.Compact;
        HotZone.Visibility = Vis(up);
        if (up != _slidUp)
        {
            _slidUp = up;
            var anim = new DoubleAnimation(up ? -48 : 0, TimeSpan.FromMilliseconds(up ? 260 : 300))
            {
                EasingFunction = up
                    ? new CubicEase { EasingMode = EasingMode.EaseIn }
                    : new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut }
            };
            Slide.BeginAnimation(TranslateTransform.YProperty, anim);
        }
        UpdateEqualizer();
    }

    /// <summary>Fare ekranın üst kenarına geldi: çentiği indir.</summary>
    private void Reveal()
    {
        if (!_settings.Hidden || _revealed) return;
        _revealed = true;
        Native.BringToTop(_hwnd);
        ApplySlide();
        _revealCheckTimer.Stop();
        _revealCheckTimer.Start();
    }

    // ---------------------------------------------------------------- kısa bildirim (peek)

    public void Peek(PeekItem item)
    {
        if (_closed || _hiddenForFullscreen || _mode == Mode.Expanded) return;
        if (_mode == Mode.Peek)
        {
            if (_peekQueue.Count < 5) _peekQueue.Enqueue(item);
            return;
        }
        ShowPeekNow(item);
    }

    private void ShowPeekNow(PeekItem p)
    {
        PeekTitle.Text = p.Title;
        PeekText.Text = p.Text;
        PeekArtBrush.ImageSource = p.Image;
        PeekGlyph.Text = p.Image == null ? p.Glyph : "";

        if (_mode == Mode.Peek) PeekView.Opacity = 0;
        SetMode(Mode.Peek);
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    // ---------------------------------------------------------------- medya

    private void UpdateMedia()
    {
        if (_closed) return;
        bool has = _media.HasMedia;
        MediaTitle.Text = has ? _media.Title : "Şu anda çalan bir şey yok";
        MediaArtist.Text = has ? _media.Artist : "";
        MediaApp.Text = has ? _media.AppName : "";
        MediaArtBrush.ImageSource = _media.Art;
        MediaArtGlyph.Visibility = Vis(_media.Art == null);
        PlayBtn.Content = _media.IsPlaying ? "" : "";
        PrevBtn.IsEnabled = PlayBtn.IsEnabled = NextBtn.IsEnabled = has;
        ProgressTrack.Cursor = _media.CanSeek ? Cursors.Hand : null;

        CompactArtBox.Visibility = Vis(has);
        CompactArtBrush.ImageSource = _media.Art;
        CompactArtGlyph.Visibility = Vis(_media.Art == null);
        MediaAppGlyph.Visibility = Vis(has);

        if (_mode == Mode.Compact) SetMode(Mode.Compact); // genişliği ayarla
        else { UpdateAmbient(); UpdateEqualizer(); UpdateProgress(); }
    }

    /// <summary>Kapak resminden bulanık arka plan, yalnızca panel açıkken.</summary>
    private void UpdateAmbient()
    {
        bool show = _mode == Mode.Expanded && _media.AmbientArt != null;
        AmbientBrush.ImageSource = _media.AmbientArt;
        if (show == (AmbientLayer.Visibility == Visibility.Visible)) return;

        if (show)
        {
            AmbientLayer.Visibility = Visibility.Visible;
            AmbientLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
        }
        else
        {
            AmbientLayer.BeginAnimation(OpacityProperty, null);
            AmbientLayer.Opacity = 0;
            AmbientLayer.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Ekolayzer yalnızca çentik görünür ve müzik çalıyorken animasyon yapar (CPU tasarrufu).</summary>
    private void UpdateEqualizer()
    {
        bool run = _mode == Mode.Compact && _media.HasMedia && _media.IsPlaying && !_hiddenForFullscreen && !_slidUp;
        EqPanel.Visibility = Vis(_media.HasMedia && _media.IsPlaying);
        CompactDot.Visibility = Vis(_host.Unread > 0 && EqPanel.Visibility != Visibility.Visible);
        if (run == _eqRunning || _eq == null) return;
        _eqRunning = run;
        if (run) _eq.Begin(this, true);
        else _eq.Stop(this);
    }

    private void UpdateProgress()
    {
        if (_closed || _seeking || _mode != Mode.Expanded) return;
        var p = _media.HasMedia ? _media.GetProgress() : null;
        ProgressRow.Visibility = Vis(p != null);
        if (p == null) return;
        var (pos, dur) = p.Value;

        // Sarmadan hemen sonra kaynak uygulama yeni konumu bildirene kadar çubuk geri zıplamasın
        if (DateTime.Now < _pendingSeekUntil)
            pos = _pendingSeek + (_media.IsPlaying ? DateTime.Now - (_pendingSeekUntil - TimeSpan.FromSeconds(1.5)) : TimeSpan.Zero);

        ShowPosition(pos, dur);
    }

    private void ShowPosition(TimeSpan pos, TimeSpan dur)
    {
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (pos > dur) pos = dur;
        PosText.Text = Fmt(pos);
        DurText.Text = Fmt(dur);
        double w = ProgressTrack.ActualWidth;
        double fill = w > 0 && dur.TotalSeconds > 0 ? w * pos.TotalSeconds / dur.TotalSeconds : 0;
        ProgressFill.Width = Math.Max(4, fill);
        SeekKnob.Margin = new Thickness(Math.Clamp(fill - 6, 0, Math.Max(0, w - 12)), 0, 0, 0);
    }

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    // ---------------------------------------------------------------- ileri / geri sarma

    private void HookSeekBar()
    {
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress();

        ProgressTrack.MouseEnter += (_, _) => SetSeekHover(true);
        ProgressTrack.MouseLeave += (_, _) => { if (!_seeking) SetSeekHover(false); };

        ProgressTrack.MouseLeftButtonDown += (_, e) =>
        {
            var p = _media.CanSeek ? _media.GetProgress() : null;
            if (p == null) return;
            _seekDuration = p.Value.Duration;
            _seeking = true;
            ProgressTrack.CaptureMouse();
            PreviewSeek(e.GetPosition(ProgressTrack).X);
            e.Handled = true;
        };
        ProgressTrack.MouseMove += (_, e) =>
        {
            if (_seeking) PreviewSeek(e.GetPosition(ProgressTrack).X);
        };
        ProgressTrack.MouseLeftButtonUp += async (_, e) =>
        {
            if (!_seeking) return;
            var target = PreviewSeek(e.GetPosition(ProgressTrack).X);
            _seeking = false;
            ProgressTrack.ReleaseMouseCapture();
            if (!ProgressTrack.IsMouseOver) SetSeekHover(false);
            await SeekTo(target);
        };
        ProgressTrack.LostMouseCapture += (_, _) =>
        {
            if (!_seeking) return;
            _seeking = false;
            SetSeekHover(false);
        };

        // Fare tekerleği: ileri / geri 10 saniye
        ProgressRow.MouseWheel += async (_, e) =>
        {
            var p = _media.CanSeek ? _media.GetProgress() : null;
            if (p == null) return;
            var target = p.Value.Position + TimeSpan.FromSeconds(e.Delta > 0 ? 10 : -10);
            e.Handled = true;
            ShowPosition(target, p.Value.Duration);
            await SeekTo(target);
        };
    }

    private void SetSeekHover(bool on)
    {
        bool show = on && _media.CanSeek;
        TrackBg.Height = ProgressFill.Height = show ? 6 : 4;
        SeekKnob.Visibility = Vis(show);
    }

    private TimeSpan PreviewSeek(double x)
    {
        double w = Math.Max(1, ProgressTrack.ActualWidth);
        double frac = Math.Clamp(x / w, 0, 1);
        var pos = TimeSpan.FromSeconds(_seekDuration.TotalSeconds * frac);
        ShowPosition(pos, _seekDuration);
        return pos;
    }

    private async Task SeekTo(TimeSpan target)
    {
        _pendingSeek = target < TimeSpan.Zero ? TimeSpan.Zero : target;
        _pendingSeekUntil = DateTime.Now + TimeSpan.FromSeconds(1.5);
        await _media.SeekAsync(_pendingSeek);
    }

    // ---------------------------------------------------------------- bildirimler

    private void UpdateNotifUi()
    {
        if (_closed) return;
        NotifNoAccess.Visibility = Vis(!_host.NotificationAccess);
        NotifEmpty.Visibility = Vis(_host.NotificationAccess && _host.NotifItems.Count == 0);
        ClearBtn.Visibility = Vis(_host.NotifItems.Count > 0);
        NotifBadge.Visibility = Vis(_host.Unread > 0);
        NotifBadgeText.Text = _host.Unread > 9 ? "9+" : _host.Unread.ToString();
        UpdateEqualizer();
    }

    // ---------------------------------------------------------------- olaylar

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (MediaPanel == null) return; // InitializeComponent sırasında
        MediaPanel.Visibility = Vis(TabMedia.IsChecked == true);
        BtPanel.Visibility = Vis(TabBt.IsChecked == true);
        NotifPanel.Visibility = Vis(TabNotif.IsChecked == true);
        if (TabNotif.IsChecked == true) _host.MarkRead();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.PlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();

    private void OpenBtSettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:bluetooth");
    private void OpenNotifSettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:privacy-notifications");
    private void ClearNotifs_Click(object sender, RoutedEventArgs e) => _host.ClearNotifications();

    // ---------------------------------------------------------------- yardımcılar

    private static DispatcherTimer MakeTimer(int ms, Action action, bool repeat = false)
    {
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) =>
        {
            if (!repeat) t.Stop();
            action();
        };
        return t;
    }

    private static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
