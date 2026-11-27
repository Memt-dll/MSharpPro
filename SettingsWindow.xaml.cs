using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MSharpPro.Services;
using static MSharpPro.Services.Loc;

namespace MSharpPro;

public partial class SettingsWindow : Window
{
    /// <summary>Ekran açılır listesindeki bir seçenek.</summary>
    private sealed record DisplayChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppHost _host;
    private readonly AppSettings _settings;
    private readonly UpdateService _updates;
    private bool _loading;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public SettingsWindow(AppHost host)
    {
        _host = host;
        _settings = host.Settings;
        _updates = host.Updates;
        _loading = true; // InitializeComponent sırasında kaydırıcının ilk değeri ayara yazılmasın
        InitializeComponent();

        Translate(this);
        VersionText.Text = L("Sürüm {0}", UpdateService.Fmt(_updates.Current));
        ShowPage(NavLook);
        ApplyTheme();

        // Yazı tiplerini isme göre sırala (liste sanallaştırılmış, sadece görünenler çizilir)
        FontCombo.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
        FillDisplays();
        FocusCombo.ItemsSource = new[] { 15, 20, 25, 30, 45, 50, 60 };
        BreakCombo.ItemsSource = new[] { 3, 5, 10, 15, 20 };

        LoadValues();
        UpdateUpdateUi();
        _settings.Changed += OnSettingsChanged;
        _updates.StateChanged += UpdateUpdateUi;
        Closed += (_, _) =>
        {
            _settings.Changed -= OnSettingsChanged;
            _updates.StateChanged -= UpdateUpdateUi;
        };

        SourceInitialized += (_, _) =>
        {
            // Windows 11 yuvarlak köşe + temaya uygun başlık çubuğu
            var hwnd = new WindowInteropHelper(this).Handle;
            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            ApplyTitleBar();
            // Windows'ta açık/koyu mod değişince (tema "Sistem" ise) anında uy
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
    }

    // ---------------------------------------------------------------- tema

    private const int WM_SETTINGCHANGE = 0x001A;
    private bool _dark = true;

    private static readonly Dictionary<string, (uint Dark, uint Light)> Palette = new()
    {
        ["Bg"] = (0x1E211E, 0xF7F6F1),
        ["Side"] = (0x181A18, 0xEDEBE3),
        ["CardBg"] = (0x262A26, 0xFFFFFF),
        ["CardBorder"] = (0x2F342F, 0xE4E2D9),
        ["Ink"] = (0xF2F1EB, 0x222622),
        ["Sub"] = (0x9EA39C, 0x686C66),
        ["Faint"] = (0x6B706A, 0x9C9F98),
        ["DividerBg"] = (0x2F342F, 0xE6E4DC),
        ["Hover"] = (0x2C312C, 0xE4E2D9),
        ["SelBg"] = (0x2A2E2A, 0xE0DED4),
        ["Accent"] = (0xF2F1EB, 0x222622),
        ["AccentFg"] = (0x222622, 0xF2F1EB),
        ["ControlBg"] = (0x2A2E2A, 0xF2F1EB),
        ["ControlBorder"] = (0x3A3F3A, 0xD9D7CD),
        ["TrackOff"] = (0x454B45, 0xCCCAC0),
        ["KnobOff"] = (0xD6D6CF, 0xFFFFFF),
        ["PopupBg"] = (0x2A2E2A, 0xFFFFFF),
        ["Screen"] = (0x3A403A, 0xD9D7CD),
        ["LogoBg"] = (0x222622, 0xF2F1EB),
        ["LogoFg"] = (0xFFFFFF, 0x222622),
        ["LogoBorder"] = (0x3A3F3A, 0xD5D3C8),
    };

    /// <summary>Windows'ta uygulamalar için koyu mod seçili mi.</summary>
    private static bool SystemUsesDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    private void ApplyTheme()
    {
        _dark = _settings.Theme switch
        {
            AppSettings.ThemeLight => false,
            AppSettings.ThemeDark => true,
            _ => SystemUsesDark(),
        };
        foreach (var (key, c) in Palette)
        {
            uint rgb = _dark ? c.Dark : c.Light;
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            Resources[key] = brush;
        }
        ApplyTitleBar();
    }

    private void ApplyTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = _dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        // Başlık çubuğu rengi pencere zeminiyle aynı olsun (Windows 11; COLORREF = 0x00BBGGRR)
        uint rgb = _dark ? Palette["Bg"].Dark : Palette["Bg"].Light;
        int colorRef = (int)(((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF));
        DwmSetWindowAttribute(hwnd, 35, ref colorRef, sizeof(int));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && _settings.Theme == AppSettings.ThemeSystem
            && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
            ApplyTheme();
        return IntPtr.Zero;
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Theme = sender == ThemeLightBtn ? AppSettings.ThemeLight
            : sender == ThemeDarkBtn ? AppSettings.ThemeDark
            : AppSettings.ThemeSystem;
        Save();
        ApplyTheme();
    }

    // ---------------------------------------------------------------- sol menü

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageLook != null && sender is RadioButton rb) ShowPage(rb);
    }

    private void ShowPage(RadioButton nav)
    {
        (StackPanel page, string desc) = nav == NavBehavior ? (PageBehavior, "Başlangıç, bildirimler, göstergeler ve kısayol")
            : nav == NavTabs ? (PageTabs, "Çentik panelinde hangi sekmelerin hangi sırayla görüneceği")
            : nav == NavWeather ? (PageWeather, "Çentikte ve panelde gösterilen hava durumu")
            : nav == NavUpdate ? (PageUpdate, "Sürüm bilgisi ve otomatik güncelleme")
            : (PageLook, "Tema, dil, çentiğin biçimi, ekranı, yazı tipi ve boyutu");
        foreach (var p in new[] { PageLook, PageTabs, PageBehavior, PageWeather, PageUpdate })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = nav.Content as string ?? "";
        PageDesc.Text = L(desc);
        Scroller.ScrollToTop();
    }

    private void FillDisplays()
    {
        var monitors = Monitors.GetAll();
        var items = new List<DisplayChoice> { new(AppSettings.DisplayPrimary, L("Ana ekran")) };
        items.AddRange(monitors.Select(m => new DisplayChoice(m.Device, m.Label)));
        if (monitors.Count > 1) items.Add(new DisplayChoice(AppSettings.DisplayAll, L("Tüm ekranlar")));
        DisplayCombo.ItemsSource = items;
        DisplayDesc.Text = monitors.Count > 1
            ? L("{0} ekran bağlı", monitors.Count)
            : L("Tek ekran bağlı; ikinci ekran takınca seçenekler çıkar");
    }

    private void OnSettingsChanged()
    {
        // Ctrl+Win ile gizleme durumu değişirse anahtarı güncelle
        if (!_loading) LoadValues();
    }

    private void LoadValues()
    {
        _loading = true;
        FontCombo.SelectedItem = (FontCombo.ItemsSource as IEnumerable<FontFamily>)?
            .FirstOrDefault(f => string.Equals(f.Source, _settings.FontFamily, StringComparison.OrdinalIgnoreCase));
        var displays = (IEnumerable<DisplayChoice>)DisplayCombo.ItemsSource;
        DisplayCombo.SelectedItem = displays.FirstOrDefault(d =>
            string.Equals(d.Value, _settings.Display, StringComparison.OrdinalIgnoreCase)) ?? displays.First();
        ScaleSlider.Value = _settings.Scale;
        ScaleText.Text = $"%{Math.Round(_settings.Scale * 100)}";
        StartupToggle.IsChecked = _settings.StartWithWindows;
        HiddenToggle.IsChecked = _settings.Hidden;
        MuteToggle.IsChecked = _settings.MuteNotifications;
        HotkeyToggle.IsChecked = _settings.HotkeyEnabled;
        FullscreenToggle.IsChecked = _settings.HideInFullscreen;
        AutoUpdateToggle.IsChecked = _settings.AutoUpdate;
        ShapeNotchBtn.IsChecked = _settings.Shape != AppSettings.ShapeIsland;
        ShapeIslandBtn.IsChecked = _settings.Shape == AppSettings.ShapeIsland;
        WeatherToggle.IsChecked = _settings.WeatherEnabled;
        ClockToggle.IsChecked = _settings.ShowClock;
        ThemeSystemBtn.IsChecked = _settings.Theme == AppSettings.ThemeSystem;
        ThemeLightBtn.IsChecked = _settings.Theme == AppSettings.ThemeLight;
        ThemeDarkBtn.IsChecked = _settings.Theme == AppSettings.ThemeDark;
        LangSystemBtn.IsChecked = _settings.Language == AppSettings.LangSystem;
        LangTrBtn.IsChecked = _settings.Language == "tr";
        LangEnBtn.IsChecked = _settings.Language == "en";
        OsdToggle.IsChecked = _settings.ShowOsd;
        BatteryToggle.IsChecked = _settings.ShowBattery;
        PrivacyToggle.IsChecked = _settings.ShowPrivacyDots;
        CalendarToggle.IsChecked = _settings.CalendarReminders;
        LyricsToggle.IsChecked = _settings.LyricsEnabled;
        ClipToggle.IsChecked = _settings.ClipboardHistory;
        FocusCombo.SelectedItem = _settings.PomodoroFocus;
        BreakCombo.SelectedItem = _settings.PomodoroBreak;
        BuildTabsList();
        BuildMutedList();
        WeatherLocationBox.IsEnabled = _settings.WeatherEnabled;
        WeatherLocationBox.Opacity = _settings.WeatherEnabled ? 1 : 0.45;
        WeatherLocText.Text = _settings.WeatherLat is null
            ? L("Seçilmedi: bir şehir arayın ya da konumunuzu kullanın")
            : L("Seçili: {0}", _settings.WeatherCity.Length > 0 ? _settings.WeatherCity : L("Konumum"));
        _loading = false;
    }

    // ---------------------------------------------------------------- görünüm stili

    private void Shape_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Shape = sender == ShapeIslandBtn ? AppSettings.ShapeIsland : AppSettings.ShapeNotch;
        Save();
    }

    // ---------------------------------------------------------------- hava durumu konumu

    private void CitySearch_TextChanged(object sender, TextChangedEventArgs e)
        => CityPlaceholder.Visibility = CitySearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void CitySearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Search_Click(sender, e); }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        string q = CitySearch.Text.Trim();
        if (q.Length < 2) return;
        WeatherLocText.Text = L("Aranıyor…");
        var results = await WeatherService.SearchAsync(q);
        if (results.Count == 0)
        {
            WeatherLocText.Text = L("Sonuç bulunamadı (internet bağlantısını ya da yazımı kontrol edin)");
            CityResults.Visibility = Visibility.Collapsed;
            return;
        }
        WeatherLocText.Text = L("Listeden şehrinizi seçin");
        _loading = true;
        CityResults.ItemsSource = results;
        CityResults.SelectedItem = null;
        _loading = false;
        CityResults.Visibility = Visibility.Visible;
        CityResults.IsDropDownOpen = true;
    }

    private void CityResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CityResults.SelectedItem is not WeatherService.Place p) return;
        _settings.WeatherLat = Math.Round(p.Lat, 3);
        _settings.WeatherLon = Math.Round(p.Lon, 3);
        _settings.WeatherCity = p.Label;
        CityResults.Visibility = Visibility.Collapsed;
        CitySearch.Text = "";
        Save();
        LoadValues();
    }

    private async void UseLocation_Click(object sender, RoutedEventArgs e)
    {
        WeatherLocText.Text = L("Konum alınıyor…");
        if (!await _host.Weather.TryUseDeviceLocationAsync())
            WeatherLocText.Text = L("Konum alınamadı: Ayarlar › Gizlilik › Konum'da masaüstü uygulamalarına izin verin ya da şehir arayın");
        else
            LoadValues();
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontCombo.SelectedItem is not FontFamily f) return;
        _settings.FontFamily = f.Source;
        Save();
    }

    private void DisplayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DisplayCombo.SelectedItem is not DisplayChoice d) return;
        _settings.Display = d.Value;
        Save();
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleText == null) return;
        ScaleText.Text = $"%{Math.Round(e.NewValue * 100)}";
        if (_loading) return;
        _settings.Scale = Math.Round(e.NewValue, 2);
        Save();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender == StartupToggle) _settings.StartWithWindows = StartupToggle.IsChecked == true;
        else if (sender == HiddenToggle) _settings.Hidden = HiddenToggle.IsChecked == true;
        else if (sender == MuteToggle) _settings.MuteNotifications = MuteToggle.IsChecked == true;
        else if (sender == HotkeyToggle) _settings.HotkeyEnabled = HotkeyToggle.IsChecked == true;
        else if (sender == FullscreenToggle) _settings.HideInFullscreen = FullscreenToggle.IsChecked == true;
        else if (sender == AutoUpdateToggle) _settings.AutoUpdate = AutoUpdateToggle.IsChecked == true;
        else if (sender == WeatherToggle) _settings.WeatherEnabled = WeatherToggle.IsChecked == true;
        else if (sender == ClockToggle) _settings.ShowClock = ClockToggle.IsChecked == true;
        else if (sender == OsdToggle) _settings.ShowOsd = OsdToggle.IsChecked == true;
        else if (sender == BatteryToggle) _settings.ShowBattery = BatteryToggle.IsChecked == true;
        else if (sender == PrivacyToggle) _settings.ShowPrivacyDots = PrivacyToggle.IsChecked == true;
        else if (sender == CalendarToggle) _settings.CalendarReminders = CalendarToggle.IsChecked == true;
        else if (sender == LyricsToggle) _settings.LyricsEnabled = LyricsToggle.IsChecked == true;
        else if (sender == ClipToggle) _settings.ClipboardHistory = ClipToggle.IsChecked == true;
        Save();
        LoadValues();
    }

    // ---------------------------------------------------------------- dil

    private void Lang_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Language = sender == LangTrBtn ? "tr" : sender == LangEnBtn ? "en" : AppSettings.LangSystem;
        Save();
        LangRestartRow.Visibility = Visibility.Visible;
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => AppHost.Restart();

    // ---------------------------------------------------------------- sekmeler

    private static readonly Dictionary<string, (string Glyph, string Name)> TabInfo = new()
    {
        ["media"] = ("", "Müzik"),
        ["lyrics"] = ("", "Şarkı sözleri"),
        ["bt"] = ("", "Bluetooth"),
        ["notif"] = ("", "Bildirimler"),
        ["weather"] = ("", "Hava durumu"),
        ["timer"] = ("", "Zamanlayıcı"),
        ["calendar"] = ("", "Takvim"),
        ["clipboard"] = ("", "Kopyalananlar"),
        ["shelf"] = ("", "Dosya rafı"),
        ["system"] = ("", "Sistem"),
    };

    /// <summary>Sekme listesini (sıra okları + açma anahtarı) yeniden çizer.</summary>
    private void BuildTabsList()
    {
        TabsList.Children.Clear();
        var order = _settings.TabOrder;
        for (int i = 0; i < order.Count; i++)
        {
            string id = order[i];
            if (!TabInfo.TryGetValue(id, out var info)) continue;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = info.Glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 14, 0)
            };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Sub");
            var name = new TextBlock { Text = L(info.Name), FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            Grid.SetColumn(name, 1);

            Button Arrow(string glyph, int col, int delta, bool enabled)
            {
                var b = new Button
                {
                    Content = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), FontSize = 11 },
                    Style = (Style)FindResource("GhostButton"), IsEnabled = enabled, Margin = new Thickness(2, 0, 2, 0),
                    ToolTip = delta < 0 ? L("Yukarı taşı") : L("Aşağı taşı"), Padding = new Thickness(0)
                };
                b.Click += (_, _) => MoveTab(id, delta);
                Grid.SetColumn(b, col);
                return b;
            }

            var toggle = new CheckBox
            {
                Style = (Style)FindResource("Toggle"), IsChecked = !_settings.HiddenTabs.Contains(id),
                Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Click += (_, _) =>
            {
                if (toggle.IsChecked == true) _settings.HiddenTabs.Remove(id);
                else if (!_settings.HiddenTabs.Contains(id)) _settings.HiddenTabs.Add(id);
                Save();
            };
            Grid.SetColumn(toggle, 4);

            row.Children.Add(icon);
            row.Children.Add(name);
            row.Children.Add(Arrow("", 2, -1, i > 0));
            row.Children.Add(Arrow("", 3, +1, i < order.Count - 1));
            row.Children.Add(toggle);
            TabsList.Children.Add(row);
        }
    }

    private void MoveTab(string id, int delta)
    {
        var order = _settings.TabOrder;
        int i = order.IndexOf(id), j = i + delta;
        if (i < 0 || j < 0 || j >= order.Count) return;
        (order[i], order[j]) = (order[j], order[i]);
        Save();
        BuildTabsList();
    }

    private void Pomodoro_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (FocusCombo.SelectedItem is int f) _settings.PomodoroFocus = f;
        if (BreakCombo.SelectedItem is int b) _settings.PomodoroBreak = b;
        Save();
    }

    // ---------------------------------------------------------------- sessize alınan uygulamalar

    private void BuildMutedList()
    {
        MutedList.Children.Clear();
        MutedEmpty.Visibility = _settings.MutedApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var app in _settings.MutedApps.ToList())
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var remove = new Button { Content = L("Kaldır"), Style = (Style)FindResource("GhostButton") };
            remove.Click += (_, _) =>
            {
                _settings.MutedApps.Remove(app);
                Save();
                BuildMutedList();
            };
            DockPanel.SetDock(remove, Dock.Right);
            var name = new TextBlock { Text = app, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            row.Children.Add(remove);
            row.Children.Add(name);
            MutedList.Children.Add(row);
        }
    }

    // ---------------------------------------------------------------- yedekleme

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"MSharpPro-{L("ayarlar")}-{DateTime.Now:yyyy-MM-dd}.json",
            Filter = "JSON (*.json)|*.json",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _settings.ExportTo(dlg.FileName);
            ShowBackupStatus(L("Ayarlar kaydedildi"));
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            ShowBackupStatus(L("Dosya yazılamadı"));
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        string oldLang = _settings.Language;
        if (!_settings.ImportFrom(dlg.FileName))
        {
            ShowBackupStatus(L("Bu dosya geçerli bir M#Pro ayar dosyası değil"));
            return;
        }
        LoadValues();
        ApplyTheme();
        ShowBackupStatus(L("Ayarlar yüklendi"));
        if (_settings.Language != oldLang) LangRestartRow.Visibility = Visibility.Visible;
    }

    private void ShowBackupStatus(string text)
    {
        BackupStatus.Text = text;
        BackupStatus.Visibility = Visibility.Visible;
    }

    private void ReleaseNotes_Click(object sender, RoutedEventArgs e) =>
        AppHost.OpenReleaseNotes(UpdateService.Fmt(_updates.Current));

    private void Save()
    {
        _loading = true;
        try { _settings.Save(); }
        finally { _loading = false; }
    }

    // ---------------------------------------------------------------- güncelleme

    private void UpdateUpdateUi()
    {
        if (_updates.IsStoreVersion)
        {
            // Store sürümü: güncellemeleri Microsoft Store yapar
            AutoUpdateToggle.Visibility = Visibility.Collapsed;
            AutoUpdateDivider.Visibility = Visibility.Collapsed;
            InstallUpdateBtn.Visibility = Visibility.Collapsed;
            UpdateStatus.Text = L("Yüklü sürüm: {0}", UpdateService.Fmt(_updates.Current));
            UpdateDetail.Text = L("Güncellemeler Microsoft Store üzerinden otomatik gelir");
            CheckUpdateBtnText.Text = L("Store'da aç");
            return;
        }

        var s = _updates.State;
        UpdateStatus.Text = s == UpdateService.UpdateState.Idle
            ? L("Yüklü sürüm: {0}", UpdateService.Fmt(_updates.Current))
            : _updates.Message;

        string detail = _updates.LastCheck is { } t ? L("Son denetim: {0}", t.ToString("HH:mm")) : L("Henüz denetlenmedi");
        if (!_updates.IsInstalled) detail += "  ·  " + L("Geliştirme kopyası: arka planda kurulmaz, 'Güncelle' ile kurulur");
        UpdateDetail.Text = detail;

        bool busy = s is UpdateService.UpdateState.Checking or UpdateService.UpdateState.Downloading
            or UpdateService.UpdateState.Installing;
        CheckUpdateBtn.IsEnabled = !busy;
        InstallUpdateBtn.Visibility = s == UpdateService.UpdateState.Available ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.IsStoreVersion) AppHost.OpenStorePage();
        else await _updates.CheckAsync(install: false);
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
        => await _updates.InstallNowAsync();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        LoadValues();
        ApplyTheme();
    }
}
