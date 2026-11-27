using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MSharpPro.Services;

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

        VersionText.Text = $"Sürüm {UpdateService.Fmt(_updates.Current)}";
        ShowPage(NavLook);
        ApplyTheme();

        // Yazı tiplerini isme göre sırala (liste sanallaştırılmış, sadece görünenler çizilir)
        FontCombo.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
        FillDisplays();

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
        (StackPanel page, string desc) = nav == NavBehavior ? (PageBehavior, "Başlangıç, bildirimler, gizleme ve kısayol")
            : nav == NavWeather ? (PageWeather, "Çentikte ve panelde gösterilen hava durumu")
            : nav == NavUpdate ? (PageUpdate, "Sürüm bilgisi ve otomatik güncelleme")
            : (PageLook, "Tema, çentiğin biçimi, ekranı, yazı tipi ve boyutu");
        foreach (var p in new[] { PageLook, PageBehavior, PageWeather, PageUpdate })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = nav.Content as string ?? "";
        PageDesc.Text = desc;
        Scroller.ScrollToTop();
    }

    private void FillDisplays()
    {
        var monitors = Monitors.GetAll();
        var items = new List<DisplayChoice> { new(AppSettings.DisplayPrimary, "Ana ekran") };
        items.AddRange(monitors.Select(m => new DisplayChoice(m.Device, m.Label)));
        if (monitors.Count > 1) items.Add(new DisplayChoice(AppSettings.DisplayAll, "Tüm ekranlar"));
        DisplayCombo.ItemsSource = items;
        DisplayDesc.Text = monitors.Count > 1
            ? $"{monitors.Count} ekran bağlı"
            : "Tek ekran bağlı; ikinci ekran takınca seçenekler çıkar";
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
        WeatherLocationBox.IsEnabled = _settings.WeatherEnabled;
        WeatherLocationBox.Opacity = _settings.WeatherEnabled ? 1 : 0.45;
        WeatherLocText.Text = _settings.WeatherLat is null
            ? "Seçilmedi: bir şehir arayın ya da konumunuzu kullanın"
            : $"Seçili: {(_settings.WeatherCity.Length > 0 ? _settings.WeatherCity : "Konumum")}";
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
        WeatherLocText.Text = "Aranıyor…";
        var results = await WeatherService.SearchAsync(q);
        if (results.Count == 0)
        {
            WeatherLocText.Text = "Sonuç bulunamadı (internet bağlantısını ya da yazımı kontrol edin)";
            CityResults.Visibility = Visibility.Collapsed;
            return;
        }
        WeatherLocText.Text = "Listeden şehrinizi seçin";
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
        WeatherLocText.Text = "Konum alınıyor…";
        if (!await _host.Weather.TryUseDeviceLocationAsync())
            WeatherLocText.Text = "Konum alınamadı: Ayarlar › Gizlilik › Konum'da masaüstü uygulamalarına izin verin ya da şehir arayın";
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
        Save();
        LoadValues();
    }

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
            UpdateStatus.Text = $"Yüklü sürüm: {UpdateService.Fmt(_updates.Current)}";
            UpdateDetail.Text = "Güncellemeler Microsoft Store üzerinden otomatik gelir";
            CheckUpdateBtnText.Text = "Store'da aç";
            return;
        }

        var s = _updates.State;
        UpdateStatus.Text = s == UpdateService.UpdateState.Idle
            ? $"Yüklü sürüm: {UpdateService.Fmt(_updates.Current)}"
            : _updates.Message;

        string detail = _updates.LastCheck is { } t ? $"Son denetim: {t:HH:mm}" : "Henüz denetlenmedi";
        if (!_updates.IsInstalled) detail += "  ·  Geliştirme kopyası: arka planda kurulmaz, 'Güncelle' ile kurulur";
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
