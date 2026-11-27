using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MSharpPro.Services;
using static MSharpPro.Services.Loc;

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
    private readonly DispatcherTimer _rehideTimer, _revealCheckTimer, _clockTimer;
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
    private const double BaseWidth = 560, BaseHeight = 270;
    private const double ExpandedWidth = 520;

    // Kısa bildirime tıklanınca yapılacak iş
    private Action? _peekClick;
    private bool _peekIsOsd;
    // Canlı sekmeler: yalnızca panel açık ve sekme seçiliyken çalışır
    private readonly DispatcherTimer _lyricsTimer;
    private bool _statsOn;
    // Raftan dışarı sürükleme
    private Point _dragStart;
    private bool _draggingOut;

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
        CalendarList.ItemsSource = host.Calendar.Events;
        ClipList.ItemsSource = host.Clipboard.Items;
        ShelfList.ItemsSource = host.Shelf.Items;
        // Liste şablonlarındaki ipuçları (şablonlar sonradan oluştuğu için kaynaktan, çevrilmiş olarak)
        Resources["MuteAppTip"] = L("Bu uygulamanın bildirimlerini çentikte gösterme");
        Resources["OpenAppTip"] = L("Uygulamayı aç");

        _lyricsTimer = MakeTimer(300, UpdateLyricsLine, repeat: true);
        PeekView.MouseLeftButtonUp += (_, _) =>
        {
            var click = _peekClick;
            if (click == null) return;
            _peekClick = null;
            SetMode(Mode.Compact);
            click();
        };
        HookShelfDrop();

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

        // Saat: her dakika başında bir kez güncellenir (saniyelik zamanlayıcı yok = CPU tasarrufu)
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background);
        _clockTimer.Tick += (_, _) => UpdateClock();

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
        _host.Weather.Changed += UpdateWeather;
        _host.Timer.Changed += UpdateTimerUi;
        _host.Calendar.Changed += UpdateCalendarUi;
        _host.Privacy.Changed += UpdatePrivacy;
        _host.Stats.Changed += UpdateStatsUi;
        _host.Lyrics.Changed += UpdateLyricsUi;
        _host.Clipboard.Items.CollectionChanged += OnClipChanged;
        _host.Shelf.Items.CollectionChanged += OnShelfChanged;

        Translate(this);
        ApplyTabs();
        UpdateTimerUi();
        UpdateCalendarUi();
        UpdatePrivacy();
        OnClipChanged(null, EventArgs.Empty);
        OnShelfChanged(null, EventArgs.Empty);
        ApplyFont();
        ApplyShape();
        UpdateWeather();
        UpdateClock();
        UpdateMuteUi();
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
        _host.Weather.Changed -= UpdateWeather;
        _host.Timer.Changed -= UpdateTimerUi;
        _host.Calendar.Changed -= UpdateCalendarUi;
        _host.Privacy.Changed -= UpdatePrivacy;
        _host.Stats.Changed -= UpdateStatsUi;
        _host.Lyrics.Changed -= UpdateLyricsUi;
        _host.Clipboard.Items.CollectionChanged -= OnClipChanged;
        _host.Shelf.Items.CollectionChanged -= OnShelfChanged;
        foreach (var t in new[] { _hoverTimer, _leaveTimer, _peekTimer, _trimTimer, _fullscreenTimer, _progressTimer, _rehideTimer, _revealCheckTimer, _clockTimer, _lyricsTimer })
            t.Stop();
        if (_statsOn) { _statsOn = false; _host.Stats.Release(); }
        _eq?.Stop(this);
        BtList.ItemsSource = null;
        NotifList.ItemsSource = null;
        CalendarList.ItemsSource = null;
        ClipList.ItemsSource = null;
        ShelfList.ItemsSource = null;
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
        ApplyShape();
        UpdateWeather();
        ApplyTabs();
        UpdatePrivacy();
        bool clockWas = CompactClock.Visibility == Visibility.Visible;
        bool mutedWas = CompactMuted.Visibility == Visibility.Visible;
        UpdateClock();
        UpdateMuteUi();
        if ((clockWas != _settings.ShowClock || mutedWas != _settings.MuteNotifications) && _mode == Mode.Compact)
            SetMode(Mode.Compact); // genişlik

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

    private bool IsIsland => _settings.Shape == AppSettings.ShapeIsland;
    private string _appliedShape = "";

    private static readonly Brush NotchBorder = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromArgb(0x00, 0, 0, 0), 0), new(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.6),
        new(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 1)
    }, 90));
    // Ada: iPhone'daki gibi neredeyse görünmez ince kenar (koyu duvar kağıdında da seçilsin diye)
    private static readonly Brush IslandBorder = Frozen(new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)));

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    /// <summary>Çentik (üste yapışık) ya da Dynamic Island (yüzen hap) görünümünü uygular.</summary>
    private void ApplyShape()
    {
        if (_appliedShape == _settings.Shape) return;
        _appliedShape = _settings.Shape;
        Island.Margin = IsIsland ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        Island.BorderThickness = IsIsland ? new Thickness(1) : new Thickness(1, 0, 1, 1);
        Island.BorderBrush = IsIsland ? IslandBorder : NotchBorder;
        // İki görünümde de saf siyah zemin. (Önceden çentikte ClearValue ile XAML'deki zemin de siliniyordu:
        // adadan çentiğe geçince çentik şeffaf kalıyor, fare içinden geçtiği için üzerine gelince açılmıyordu.)
        Island.Background = Brushes.Black;
        if (IsLoaded) SetMode(_mode);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        CompactClock.Text = now.ToString("HH:mm");
        CompactClock.Visibility = Vis(_settings.ShowClock);
        if (_mode == Mode.Expanded) ClockText.Text = now.ToString("HH:mm");
        // Bir sonraki dakikanın başında tekrar
        _clockTimer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        _clockTimer.Start();
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
            Mode.Expanded => (ExpandedWidth, 236d, 30d, (FrameworkElement)ExpandedView),
            Mode.Peek => (384d, PeekLevel.Visibility == Visibility.Visible ? 74d : 66d, 24d, (FrameworkElement)PeekView),
            _ => ((double)(_media.HasMedia ? 236 : _settings.ShowClock ? 196 : 170)
                  + (_settings.MuteNotifications ? 20 : 0)
                  + (_host.Timer.Active ? 58 : 0)
                  + (MicDot.Visibility == Visibility.Visible ? 11 : 0)
                  + (CamDot.Visibility == Visibility.Visible ? 13 : 0), 32d, 16d, (FrameworkElement)CompactView)
        };
        // Dynamic Island: tamamen yuvarlak hap (kompakt ve kısa bildirimde yarıçap = yüksekliğin yarısı),
        // açık panelde iPhone'daki gibi çok yuvarlak köşeler
        if (IsIsland) r = m == Mode.Expanded ? 42 : h / 2;

        if (m == Mode.Expanded)
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm");
            if (TabNotif.IsChecked == true) _host.MarkRead();
            UpdateProgress();
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
        }

        if (m != Mode.Peek) _peekClick = null;
        if (m != Mode.Compact) Native.BringToTop(_hwnd);
        SyncLiveTabs();
        Island.CornerRadius = IsIsland ? new CornerRadius(r) : new CornerRadius(0, 0, r, r);
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
            var anim = new DoubleAnimation(up ? -60 : 0, TimeSpan.FromMilliseconds(up ? 260 : 300))
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

    public void Peek(PeekItem item, bool urgent = false)
    {
        if (_closed || _hiddenForFullscreen) return;
        if (urgent)
        {
            // Sırayı beklemeden hemen göster (panel açık olsa bile)
            _hoverTimer.Stop();
            _leaveTimer.Stop();
            ShowPeekNow(item);
            return;
        }
        if (_mode == Mode.Expanded) return;
        if (_mode == Mode.Peek)
        {
            if (_peekQueue.Count < 5) _peekQueue.Enqueue(item);
            return;
        }
        ShowPeekNow(item);
    }

    /// <summary>Ses / parlaklık göstergesi: sırayı beklemez, açık gösterge varsa yerinde günceller.</summary>
    public void ShowOsd(PeekItem item)
    {
        if (_closed || _hiddenForFullscreen || _mode == Mode.Expanded) return;
        // Bir bildirim gösteriliyorsa onu kaybetme: gösterge bitince sıradan devam etsin
        if (_mode == Mode.Peek && !_peekIsOsd && _peekQueue.Count < 5)
            _peekQueue.Enqueue(new PeekItem(PeekTitle.Text, PeekText.Text, PeekGlyph.Text, PeekArtBrush.ImageSource as ImageSource));
        ShowPeekNow(item, osd: true);
    }

    private void ShowPeekNow(PeekItem p, bool osd = false)
    {
        bool sameOsd = osd && _peekIsOsd && _mode == Mode.Peek;
        _peekIsOsd = osd;
        PeekTitle.Text = p.Title;
        PeekText.Text = p.Text;
        PeekArtBrush.ImageSource = p.Image;
        PeekGlyph.Text = p.Image == null ? p.Glyph : "";
        _peekClick = p.Click;
        PeekView.Cursor = p.Click != null ? Cursors.Hand : null;

        bool level = p.Level != null;
        bool levelChanged = (PeekLevel.Visibility == Visibility.Visible) != level;
        PeekLevel.Visibility = Vis(level);
        if (level)
        {
            double w = Math.Max(0, PeekLevel.ActualWidth > 0 ? PeekLevel.ActualWidth : 300);
            PeekLevelFill.Width = w * Math.Clamp(p.Level!.Value, 0, 1);
        }

        // Gösterge yerinde güncellenir (yanıp sönmesin)
        if (!sameOsd || levelChanged)
        {
            if (_mode == Mode.Peek) PeekView.Opacity = 0;
            SetMode(Mode.Peek);
            _peekClick = p.Click;
        }
        _peekTimer.Interval = TimeSpan.FromMilliseconds(osd ? 1600 : 3500);
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    // ---------------------------------------------------------------- medya

    private void UpdateMedia()
    {
        if (_closed) return;
        bool has = _media.HasMedia;
        MediaTitle.Text = has ? _media.Title : L("Şu anda çalan bir şey yok");
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
        // Kapak arka planı yokken (müzik çalmıyor) panelin üstünde hafif parıltı
        GlowLayer.Visibility = Vis(_mode == Mode.Expanded && !show);
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
        bool eq = _media.HasMedia && _media.IsPlaying;
        EqPanel.Visibility = Vis(eq);
        CompactRight.Visibility = Vis(!eq);
        CompactDot.Visibility = Vis(_host.Unread > 0);
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

    // ---------------------------------------------------------------- hava durumu

    private void UpdateWeather()
    {
        if (_closed) return;
        var w = _host.Weather;
        bool on = _settings.WeatherEnabled;
        bool has = on && w.HasData;


        CompactWeather.Visibility = Vis(has);
        if (has)
        {
            CompactWeatherIcon.Source = w.Icon;
            CompactWeatherTemp.Text = $"{w.Temp}°";
            TabWeatherIcon.Source = w.Icon;

            WeatherCard.Background = w.CardBrush;
            WeatherIcon.Source = w.Icon;
            WeatherTemp.Text = $"{w.Temp}°";
            WeatherCondition.Text = w.Condition;
            WeatherHiLo.Text = L("En yüksek {0}°  ·  En düşük {1}°", w.High, w.Low);
            string city = string.IsNullOrEmpty(_settings.WeatherCity) ? L("Konumum") : _settings.WeatherCity;
            int comma = city.IndexOf(',');
            WeatherCity.Text = comma > 0 ? city[..comma] : city;   // kartta yalnızca şehir adı
            WeatherDetail.Text = L("Hissedilen {0}°  ·  Nem %{1}  ·  Rüzgâr {2} km/s", w.Feels, w.Humidity, w.Wind);
            WeatherDays.ItemsSource = w.Days;
        }
        else
        {
            TabWeatherIcon.Source = WeatherIcons.Get(WeatherKind.PartlySun);
            WeatherEmptyIcon.Source = WeatherIcons.Get(WeatherKind.PartlySun);
        }
        WeatherContent.Visibility = Vis(has);
        WeatherEmpty.Visibility = Vis(on && !has);
        WeatherProblem.Text = L(w.Problem ?? "Hava durumu yükleniyor…");
        UpdateEqualizer();
    }

    private void OpenAppSettings_Click(object sender, RoutedEventArgs e) => _host.OpenSettings();

    // ---------------------------------------------------------------- sessize al

    private void Mute_Click(object sender, RoutedEventArgs e) => _host.ToggleMute();

    private static readonly Brush MutedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x6B)));

    private void UpdateMuteUi()
    {
        bool muted = _settings.MuteNotifications;
        MuteGlyph.Text = muted ? "" : "";
        MuteBtn.Foreground = muted ? MutedBrush : (Brush)FindResource("ButtonIdleForeground");
        MuteBtn.ToolTip = muted ? L("Bildirimler sessizde · açmak için tıkla") : L("Bildirimleri sessize al");
        CompactMuted.Visibility = Vis(muted);
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
        WeatherPanel.Visibility = Vis(TabWeather.IsChecked == true);
        LyricsPanel.Visibility = Vis(TabLyrics.IsChecked == true);
        TimerPanel.Visibility = Vis(TabTimer.IsChecked == true);
        CalendarPanel.Visibility = Vis(TabCalendar.IsChecked == true);
        ClipPanel.Visibility = Vis(TabClipboard.IsChecked == true);
        ShelfPanel.Visibility = Vis(TabShelf.IsChecked == true);
        SysPanel.Visibility = Vis(TabSystem.IsChecked == true);
        if (TabNotif.IsChecked == true)
        {
            _host.MarkRead();
            // Sekme açılınca kontrol edilir (bir kayıt defteri okuması): kullanıcı Teams ayarını değiştirince ipucu kaybolur
            TeamsHint.Visibility = Vis(_host.NotificationAccess && NotificationService.TeamsUsesBuiltInNotifications());
        }
        SyncLiveTabs();
    }

    /// <summary>Kullanıcının sekme sırasını ve görünürlüğünü uygular.</summary>
    private void ApplyTabs()
    {
        var tabs = TabBar.Children.OfType<RadioButton>().ToList();
        TabBar.Children.Clear();
        RadioButton? firstVisible = null;
        foreach (var id in _settings.TabOrder)
        {
            var tab = tabs.FirstOrDefault(t => (t.Tag as string) == id);
            if (tab == null) continue;
            tabs.Remove(tab);
            TabBar.Children.Add(tab);
            bool visible = !_settings.HiddenTabs.Contains(id)
                           && (id != "weather" || _settings.WeatherEnabled)
                           && (id != "lyrics" || _settings.LyricsEnabled)
                           && (id != "clipboard" || _settings.ClipboardHistory);
            tab.Visibility = Vis(visible);
            if (visible) firstVisible ??= tab;
        }
        foreach (var rest in tabs) { rest.Visibility = Visibility.Collapsed; TabBar.Children.Add(rest); }

        // Hepsi gizlendiyse müzik sekmesi kalsın
        if (firstVisible == null) { TabMedia.Visibility = Visibility.Visible; firstVisible = TabMedia; }
        var current = TabBar.Children.OfType<RadioButton>().FirstOrDefault(t => t.IsChecked == true);
        if (current == null || current.Visibility != Visibility.Visible) firstVisible.IsChecked = true;
    }

    /// <summary>Canlı veri gerektiren sekmeleri (sözler, sistem) yalnızca görünürken çalıştırır.</summary>
    private void SyncLiveTabs()
    {
        if (TabSystem == null) return;
        bool open = _mode == Mode.Expanded && !_closed;
        bool stats = open && TabSystem.IsChecked == true;
        if (stats != _statsOn)
        {
            _statsOn = stats;
            if (stats) _host.Stats.Acquire(); else _host.Stats.Release();
        }

        bool lyrics = open && TabLyrics.IsChecked == true;
        if (lyrics)
        {
            LoadLyrics();
            if (!_lyricsTimer.IsEnabled) _lyricsTimer.Start();
        }
        else _lyricsTimer.Stop();

        if (open && TabCalendar.IsChecked == true && _host.Calendar.Events.Count == 0 && _host.Calendar.NoAccess)
            _ = _host.Calendar.RefreshAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.PlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();

    private void OpenBtSettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:bluetooth");
    private void OpenNotifSettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:privacy-notifications");
    private void ClearNotifs_Click(object sender, RoutedEventArgs e) => _host.ClearNotifications();

    private void NotifItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return; // sustur butonu
        if ((sender as FrameworkElement)?.DataContext is NotifItem n) NotificationService.OpenApp(n);
    }

    private void NotifMuteApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NotifItem n || n.App.Length == 0) return;
        if (!_settings.MutedApps.Contains(n.App, StringComparer.OrdinalIgnoreCase))
        {
            _settings.MutedApps.Add(n.App);
            _settings.Save();
        }
    }

    // ---------------------------------------------------------------- gizlilik noktaları

    private void UpdatePrivacy()
    {
        if (_closed) return;
        bool on = _settings.ShowPrivacyDots;
        bool micWas = MicDot.Visibility == Visibility.Visible, camWas = CamDot.Visibility == Visibility.Visible;
        MicDot.Visibility = Vis(on && _host.Privacy.MicInUse);
        CamDot.Visibility = Vis(on && _host.Privacy.CamInUse);
        bool changed = micWas != (MicDot.Visibility == Visibility.Visible) || camWas != (CamDot.Visibility == Visibility.Visible);
        if (changed && _mode == Mode.Compact && IsLoaded) SetMode(Mode.Compact);
    }

    // ---------------------------------------------------------------- zamanlayıcı

    private void UpdateTimerUi()
    {
        if (_closed) return;
        var t = _host.Timer;
        bool wasActive = CompactTimer.Visibility == Visibility.Visible;
        CompactTimer.Visibility = Vis(t.Active);
        string text = TimerService.Format(t.Remaining);
        CompactTimerText.Text = text;
        TimerBig.Text = t.Active ? text : "0:00";
        TimerPhase.Text = t.PhaseText;
        TimerPlayBtn.Content = t.Running ? "" : "";
        PomodoroText.Text = $"Pomodoro · {_settings.PomodoroFocus}/{_settings.PomodoroBreak}";
        double track = (TimerFill.Parent as FrameworkElement)?.ActualWidth ?? 0;
        TimerFill.Width = t.Active && t.Total.TotalSeconds > 0
            ? track * Math.Clamp(1 - t.Remaining.TotalSeconds / t.Total.TotalSeconds, 0, 1) : 0;
        if (wasActive != t.Active && _mode == Mode.Compact && IsLoaded) SetMode(Mode.Compact);
    }

    private void TimerPreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string s && int.TryParse(s, out int min))
            _host.Timer.StartCountdown(TimeSpan.FromMinutes(min));
    }

    private void TimerAdd_Click(object sender, RoutedEventArgs e) => _host.Timer.Add(TimeSpan.FromMinutes(1));
    private void Pomodoro_Click(object sender, RoutedEventArgs e) => _host.Timer.StartPomodoro();
    private void TimerStop_Click(object sender, RoutedEventArgs e) => _host.Timer.Stop();

    private void TimerPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_host.Timer.Active) _host.Timer.PauseResume();
        else _host.Timer.StartCountdown(TimeSpan.FromMinutes(5));
    }

    // ---------------------------------------------------------------- takvim

    private void UpdateCalendarUi()
    {
        if (_closed) return;
        var c = _host.Calendar;
        CalendarEmpty.Visibility = Vis(c.Events.Count == 0);
        CalendarProblem.Text = c.Problem ?? "";
        CalendarAccessBtn.Visibility = Vis(c.NoAccess);
    }

    private void OpenCalendarSettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:privacy-calendar");
    private async void CalendarRefresh_Click(object sender, RoutedEventArgs e) => await _host.Calendar.RefreshAsync();

    // ---------------------------------------------------------------- kopyalananlar

    private void OnClipChanged(object? sender, EventArgs e)
    {
        if (_closed) return;
        bool any = _host.Clipboard.Items.Count > 0;
        ClipEmpty.Visibility = Vis(!any);
        ClipClearBtn.Visibility = Vis(any);
        ClipHint.Visibility = Vis(any);
    }

    private void ClipItem_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClipItem item) return;
        _host.Clipboard.Copy(item);
        ClipHint.Text = L("Kopyalandı");
        var reset = MakeTimer(1500, () => ClipHint.Text = L("Tıklayınca yeniden kopyalanır · yalnızca bellekte tutulur"));
        reset.Start();
    }

    private void ClipClear_Click(object sender, RoutedEventArgs e) => _host.Clipboard.Clear();

    // ---------------------------------------------------------------- dosya rafı

    private void OnShelfChanged(object? sender, EventArgs e)
    {
        if (_closed) return;
        bool any = _host.Shelf.Items.Count > 0;
        ShelfEmpty.Visibility = Vis(!any);
        ShelfClearBtn.Visibility = Vis(any);
    }

    private void HookShelfDrop()
    {
        Island.DragEnter += (_, e) =>
        {
            if (_draggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop) || TabShelf.Visibility != Visibility.Visible)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            _leaveTimer.Stop();
            _hoverTimer.Stop();
            TabShelf.IsChecked = true;
            if (_mode != Mode.Expanded) SetMode(Mode.Expanded);
        };
        Island.DragOver += (_, e) =>
        {
            e.Effects = !_draggingOut && e.Data.GetDataPresent(DataFormats.FileDrop) && TabShelf.Visibility == Visibility.Visible
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Island.DragLeave += (_, _) => _leaveTimer.Start();
        Island.Drop += (_, e) =>
        {
            if (!_draggingOut && e.Data.GetData(DataFormats.FileDrop) is string[] files) _host.Shelf.Add(files);
            e.Handled = true;
            _leaveTimer.Start();
        };
    }

    private void ShelfItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        if (e.ClickCount == 2 && (sender as FrameworkElement)?.DataContext is ShelfItem item) OpenUri(item.Path);
    }

    private void ShelfItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggingOut) return;
        if ((sender as FrameworkElement)?.DataContext is not ShelfItem item) return;
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
        _draggingOut = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        catch (Exception ex) { App.LogError(ex); }
        finally { _draggingOut = false; }
        // Taşındıysa raftan da kaldır
        if (!System.IO.File.Exists(item.Path) && !System.IO.Directory.Exists(item.Path)) _host.Shelf.Remove(item);
        _leaveTimer.Start();
    }

    private void ShelfItem_Remove(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // çentik menüsü açılmasın
        if ((sender as FrameworkElement)?.DataContext is ShelfItem item) _host.Shelf.Remove(item);
    }

    private void ShelfClear_Click(object sender, RoutedEventArgs e) => _host.Shelf.Clear();

    // ---------------------------------------------------------------- sistem

    private void UpdateStatsUi()
    {
        if (_closed || !_statsOn) return;
        var s = _host.Stats;
        CpuText.Text = $"%{s.CpuNow:0}";
        MemText.Text = $"%{s.MemPercent:0}";
        MemDetail.Text = L("{0:0.0} / {1:0} GB kullanımda", s.MemUsedGb, s.MemTotalGb);
        double memTrack = (MemFill.Parent as FrameworkElement)?.ActualWidth ?? 0;
        MemFill.Width = memTrack * s.MemPercent / 100;
        NetDown.Text = "↓ " + StatsService.Speed(s.DownKBs);
        NetUp.Text = "↑ " + StatsService.Speed(s.UpKBs);
        double w = Math.Max(10, (CpuSpark.Parent as FrameworkElement)?.ActualWidth ?? 120);
        CpuSpark.Data = StatsService.Spark(s.Cpu, 100, w, CpuSpark.Height);
        NetSpark.Data = StatsService.Spark(s.Net, 64, w, NetSpark.Height);
    }

    private async void FreeMem_Click(object sender, RoutedEventArgs e)
    {
        FreeMemBtn.IsEnabled = false;
        FreeMemText.Text = L("Boşaltılıyor…");
        long freed = 0;
        try { freed = await StatsService.FreeMemoryAsync(); }
        catch (Exception ex) { App.LogError(ex); }
        if (_closed) return;
        double mb = Math.Max(0, freed) / 1048576.0;
        FreeMemText.Text = mb < 1 ? L("Zaten boş")
            : L("{0} boşaltıldı", mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0} MB");
        var reset = MakeTimer(3500, () =>
        {
            FreeMemText.Text = L("Belleği boşalt");
            FreeMemBtn.IsEnabled = true;
        });
        reset.Start();
    }

    // ---------------------------------------------------------------- şarkı sözleri

    private void LoadLyrics()
    {
        if (!_settings.LyricsEnabled) return;
        var p = _media.HasMedia ? _media.GetProgress() : null;
        _host.Lyrics.Load(_media.HasMedia ? _media.Title : "", _media.HasMedia ? _media.Artist : "", p?.Duration);
        UpdateLyricsUi();
    }

    private int _lyricIndex = -2;
    private string _lyricKey = "";

    private void UpdateLyricsUi()
    {
        if (_closed) return;
        var ly = _host.Lyrics;
        bool synced = ly.Lines is { Count: > 0 };
        bool plain = !synced && !string.IsNullOrEmpty(ly.Plain);
        LyricsSynced.Visibility = Vis(synced);
        LyricsPlainBox.Visibility = Vis(plain);
        LyricsEmpty.Visibility = Vis(!synced && !plain);
        LyricsStatus.Text = ly.Status;
        if (plain) LyricsPlain.Text = ly.Plain;
        _lyricIndex = -2;
        UpdateLyricsLine();
    }

    private void UpdateLyricsLine()
    {
        if (_closed) return;
        // Şarkı değiştiyse yeni sözleri iste
        string key = _media.HasMedia ? $"{_media.Artist}|{_media.Title}" : "";
        if (key != _lyricKey)
        {
            _lyricKey = key;
            if (TabLyrics.IsChecked == true && _mode == Mode.Expanded) LoadLyrics();
            return;
        }
        var lines = _host.Lyrics.Lines;
        if (lines is not { Count: > 0 }) return;
        var p = _media.GetProgress();
        if (p == null) return;
        int i = _host.Lyrics.IndexAt(p.Value.Position);
        if (i == _lyricIndex) return;
        _lyricIndex = i;
        string At(int k) => k >= 0 && k < lines.Count ? (lines[k].Text.Length > 0 ? lines[k].Text : "♪") : "";
        LyricPrev2.Text = At(i - 2);
        LyricPrev1.Text = At(i - 1);
        LyricNow.Text = i < 0 ? "♪" : At(i);
        LyricNext1.Text = At(i + 1);
        LyricNext2.Text = At(i + 2);
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

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
