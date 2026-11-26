using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MSharpPro.Services;

namespace MSharpPro;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _loading;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        _loading = true; // InitializeComponent sırasında kaydırıcının ilk değeri ayara yazılmasın
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Sürüm {v?.Major}.{v?.Minor}.{v?.Build}  ·  Windows için çentik";

        // Yazı tiplerini isme göre sırala (liste sanallaştırılmış, sadece görünenler çizilir)
        FontCombo.ItemsSource = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();

        LoadValues();
        _settings.Changed += OnSettingsChanged;
        Closed += (_, _) => _settings.Changed -= OnSettingsChanged;

        SourceInitialized += (_, _) =>
        {
            // Koyu başlık çubuğu + Windows 11 yuvarlak köşe
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1, round = 2;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        };
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
        ScaleSlider.Value = _settings.Scale;
        ScaleText.Text = $"%{Math.Round(_settings.Scale * 100)}";
        StartupToggle.IsChecked = _settings.StartWithWindows;
        HiddenToggle.IsChecked = _settings.Hidden;
        HotkeyToggle.IsChecked = _settings.HotkeyEnabled;
        FullscreenToggle.IsChecked = _settings.HideInFullscreen;
        _loading = false;
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontCombo.SelectedItem is not FontFamily f) return;
        _settings.FontFamily = f.Source;
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
        Save();
    }

    private void Save()
    {
        _loading = true;
        try { _settings.Save(); }
        finally { _loading = false; }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        LoadValues();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
