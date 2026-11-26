using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using MSharpPro.Services;

namespace MSharpPro;

public partial class MainWindow : Window
{
    private enum Mode { Compact, Peek, Expanded }
    private sealed record PeekItem(string Title, string Text, string Glyph, ImageSource? Image);

    private readonly MediaService _media = new();
    private readonly BluetoothService _bt;
    private readonly NotificationService _notifService = new();
    private readonly ObservableCollection<NotifItem> _notifs = new();
    private readonly Queue<PeekItem> _peekQueue = new();

    private readonly DispatcherTimer _hoverTimer, _leaveTimer, _peekTimer, _trimTimer, _fullscreenTimer, _progressTimer;
    private readonly DispatcherTimer _rehideTimer, _revealCheckTimer;
    private Storyboard? _eq;
    private bool _eqRunning;
    private Mode _mode = Mode.Compact;
    private bool _hiddenForFullscreen;
    private int _unread;
    private IntPtr _hwnd;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly CtrlWinHook _hotkey = new();
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    /// <summary>Gizli moddayken fare üst kenara gelip çentiği indirdi mi.</summary>
    private bool _revealed;
    private bool _slidUp;

    // Ölçeklenmemiş pencere boyutu
    private const double BaseWidth = 480, BaseHeight = 270;

    public MainWindow()
    {
        InitializeComponent();

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

        HotZone.MouseEnter += (_, _) => Reveal();
        Island.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(); };

        _hotkey.Triggered += () => Dispatcher.InvokeAsync(ToggleHidden);
        _settings.Changed += ApplySettings;
        if (Application.Current is { } app)
            app.Exit += (_, _) => { _hotkey.Dispose(); _tray?.Dispose(); };

        _bt = new BluetoothService(Dispatcher);
        BtList.ItemsSource = _bt.Devices;
        NotifList.ItemsSource = _notifs;
        _bt.Devices.CollectionChanged += (_, _) => BtEmpty.Visibility = Vis(_bt.Devices.Count == 0);

        _hoverTimer = MakeTimer(140, () => { if (Island.IsMouseOver) SetMode(Mode.Expanded); });
        _leaveTimer = MakeTimer(350, () =>
        {
            if (Island.IsMouseOver) return;
            if (_peekQueue.Count > 0) ShowPeekNow(_peekQueue.Dequeue());
            else SetMode(Mode.Compact);
        });
        _peekTimer = MakeTimer(3500, () =>
        {
            if (_mode != Mode.Peek) return;
            if (_peekQueue.Count > 0) ShowPeekNow(_peekQueue.Dequeue());
            else SetMode(Mode.Compact);
        });
        _trimTimer = MakeTimer(3000, () => { if (_mode == Mode.Compact) Native.TrimWorkingSet(); });
        _fullscreenTimer = MakeTimer(2000, CheckFullscreen, repeat: true);
        _progressTimer = MakeTimer(1000, UpdateProgress, repeat: true);
        _eq = (Storyboard)Resources["EqStoryboard"];
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress();

        Island.MouseEnter += (_, _) => { _leaveTimer.Stop(); _hoverTimer.Start(); };
        Island.MouseLeave += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_mode == Mode.Expanded) _leaveTimer.Start();
            else if (_settings.Hidden && _revealed) _rehideTimer.Start();
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(_hwnd);
        };
        Loaded += async (_, _) => await InitAsync();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(PositionWindow);
    }

    private async Task InitAsync()
    {
        ApplySettings();
        SetMode(Mode.Compact);
        _fullscreenTimer.Start();

        _tray = new TrayIcon("M#Pro");
        _tray.LeftClick += OpenSettings;
        _tray.RightClick += ShowMenu;

        _media.Changed += () => Dispatcher.InvokeAsync(UpdateMedia);
        _media.TrackChanged += () => Dispatcher.InvokeAsync(() =>
            Peek(_media.Title, _media.Artist, "", _media.Art));

        _bt.ConnectionChanged += d =>
            Peek(d.Name, d.IsConnected ? "Bağlandı" : "Bağlantı kesildi", d.Glyph, null);

        _notifService.Received += n => Dispatcher.InvokeAsync(() =>
        {
            _notifs.Insert(0, n);
            while (_notifs.Count > 20) _notifs.RemoveAt(_notifs.Count - 1);
            _unread++;
            UpdateNotifUi();
            Peek(n.Title, n.Body.Length > 0 ? n.Body : n.App, "", n.Icon);
        });

        _bt.Start();
        await _media.StartAsync();
        UpdateMedia();

        bool hasAccess = await _notifService.StartAsync();
        NotifNoAccess.Visibility = Vis(!hasAccess);
        UpdateNotifUi();

        _trimTimer.Start();
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
            if (TabNotif.IsChecked == true) MarkRead();
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

    private void ToggleHidden()
    {
        _settings.Hidden = !_settings.Hidden;
        _revealed = false;
        if (_settings.Hidden && _mode == Mode.Expanded) SetMode(Mode.Compact);
        _settings.Save(); // ApplySettings -> ApplySlide
    }

    // ---------------------------------------------------------------- ayarlar

    private void ApplySettings()
    {
        double s = Math.Clamp(_settings.Scale, AppSettings.MinScale, AppSettings.MaxScale);
        Root.LayoutTransform = Math.Abs(s - 1) < 0.001 ? Transform.Identity : new ScaleTransform(s, s);
        Width = BaseWidth * s;
        Height = BaseHeight * s;
        PositionWindow();

        try { FontFamily = new FontFamily(_settings.FontFamily + ", Segoe UI"); } catch { }

        if (_settings.HotkeyEnabled) _hotkey.Start(); else _hotkey.Stop();

        if (!_settings.HideInFullscreen && _hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            Show();
        }

        if (!_settings.Hidden) _revealed = false;
        ApplySlide();
        _tray?.SetTooltip(_settings.Hidden ? "M#Pro (gizli · Ctrl+Win)" : "M#Pro");
    }

    private void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _trimTimer.Stop(); _trimTimer.Start();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Hem tepsi ikonunda hem çentikte sağ tıkla açılan menü.</summary>
    private void ShowMenu()
    {
        _tray?.ShowMenu(new[]
        {
            new TrayIcon.MenuItem("Ayarlar", OpenSettings),
            TrayIcon.MenuItem.Separator,
            new TrayIcon.MenuItem("Çentiği gizle\tCtrl+Win", ToggleHidden, _settings.Hidden),
            new TrayIcon.MenuItem("Bilgisayar açılınca başlat", () =>
            {
                _settings.StartWithWindows = !_settings.StartWithWindows;
                _settings.Save();
            }, _settings.StartWithWindows),
            TrayIcon.MenuItem.Separator,
            new TrayIcon.MenuItem("Çıkış", () => Application.Current.Shutdown())
        });
    }

    /// <summary>İkinci kez çalıştırılınca (ör. Başlat menüsünden) ayarları aç.</summary>
    public void OnSecondInstance() => OpenSettings();

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
        CompactDot.Visibility = Vis(_unread > 0 && EqPanel.Visibility != Visibility.Visible);
        if (run == _eqRunning || _eq == null) return;
        _eqRunning = run;
        if (run) _eq.Begin(this, true);
        else _eq.Stop(this);
    }

    private void UpdateProgress()
    {
        var p = _media.HasMedia ? _media.GetProgress() : null;
        ProgressRow.Visibility = Vis(p != null);
        if (p == null) return;
        var (pos, dur) = p.Value;
        PosText.Text = Fmt(pos);
        DurText.Text = Fmt(dur);
        double w = ProgressTrack.ActualWidth;
        ProgressFill.Width = w > 0 ? Math.Max(4, w * pos.TotalSeconds / dur.TotalSeconds) : 0;

        static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
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

    private void Peek(string title, string text, string glyph, ImageSource? image)
    {
        if (_hiddenForFullscreen || _mode == Mode.Expanded) return;
        var item = new PeekItem(title, text, glyph, image);
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

        // Peek içeriği değişirken hafif bir "nefes" efekti
        if (_mode == Mode.Peek) PeekView.Opacity = 0;
        SetMode(Mode.Peek);
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    // ---------------------------------------------------------------- içerik güncelleme

    private void UpdateMedia()
    {
        bool has = _media.HasMedia;
        MediaTitle.Text = has ? _media.Title : "Şu anda çalan bir şey yok";
        MediaArtist.Text = has ? _media.Artist : "";
        MediaApp.Text = has ? _media.AppName : "";
        MediaArtBrush.ImageSource = _media.Art;
        MediaArtGlyph.Visibility = Vis(_media.Art == null);
        PlayBtn.Content = _media.IsPlaying ? "" : "";
        PrevBtn.IsEnabled = PlayBtn.IsEnabled = NextBtn.IsEnabled = has;

        CompactArtBox.Visibility = Vis(has);
        CompactArtBrush.ImageSource = _media.Art;
        CompactArtGlyph.Visibility = Vis(_media.Art == null);
        MediaAppGlyph.Visibility = Vis(has);

        if (_mode == Mode.Compact) SetMode(Mode.Compact); // genişliği ayarla
        else { UpdateAmbient(); UpdateEqualizer(); UpdateProgress(); }
    }

    private void UpdateNotifUi()
    {
        bool noAccess = NotifNoAccess.Visibility == Visibility.Visible;
        NotifEmpty.Visibility = Vis(!noAccess && _notifs.Count == 0);
        ClearBtn.Visibility = Vis(_notifs.Count > 0);
        NotifBadge.Visibility = Vis(_unread > 0);
        NotifBadgeText.Text = _unread > 9 ? "9+" : _unread.ToString();
        UpdateEqualizer();
    }

    private void MarkRead()
    {
        _unread = 0;
        UpdateNotifUi();
    }

    // ---------------------------------------------------------------- olaylar

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (MediaPanel == null) return; // InitializeComponent sırasında
        MediaPanel.Visibility = Vis(TabMedia.IsChecked == true);
        BtPanel.Visibility = Vis(TabBt.IsChecked == true);
        NotifPanel.Visibility = Vis(TabNotif.IsChecked == true);
        if (TabNotif.IsChecked == true) MarkRead();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.PlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
    private async void Prev_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();

    private void OpenBtSettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:bluetooth");
    private void OpenNotifSettings_Click(object sender, RoutedEventArgs e) => OpenUri("ms-settings:privacy-notifications");

    private void ClearNotifs_Click(object sender, RoutedEventArgs e)
    {
        _notifs.Clear();
        MarkRead();
    }

    // ---------------------------------------------------------------- yardımcılar

    private void PositionWindow()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    private void CheckFullscreen()
    {
        bool fs = _settings.HideInFullscreen && Native.IsFullscreenBusy();
        if (!fs) Native.BringToTop(_hwnd);
        if (fs == _hiddenForFullscreen) return;
        _hiddenForFullscreen = fs;
        if (fs) Hide();
        else { Show(); Topmost = false; Topmost = true; }
    }

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
