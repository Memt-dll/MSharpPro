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

        VersionText.Text = $"Sürüm {UpdateService.Fmt(_updates.Current)}  ·  Windows için çentik";
        MaxHeight = SystemParameters.WorkArea.Height - 40;

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
            // Koyu başlık çubuğu + Windows 11 yuvarlak köşe
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1, round = 2;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        };
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
        HotkeyToggle.IsChecked = _settings.HotkeyEnabled;
        FullscreenToggle.IsChecked = _settings.HideInFullscreen;
        AutoUpdateToggle.IsChecked = _settings.AutoUpdate;
        ShapeNotchBtn.IsChecked = _settings.Shape != AppSettings.ShapeIsland;
        ShapeIslandBtn.IsChecked = _settings.Shape == AppSettings.ShapeIsland;
        WeatherToggle.IsChecked = _settings.WeatherEnabled;
        ClockToggle.IsChecked = _settings.ShowClock;
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
        => await _updates.CheckAsync(install: false);

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
        => await _updates.InstallNowAsync();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        LoadValues();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
