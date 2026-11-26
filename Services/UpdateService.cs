using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace MSharpPro.Services;

/// <summary>
/// GitHub Releases'tan yeni sürüm denetler, kurulum dosyasını indirir ve sessizce kurar.
/// HttpClient yalnızca denetim sırasında oluşturulup atılır (boşta ek RAM yok).
/// </summary>
public sealed class UpdateService
{
    public const string Repo = "Memt-dll/MSharpPro";
    private const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

    public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Installing, Error }

    public Version Current { get; } = ReadCurrentVersion();
    public Version? Latest { get; private set; }
    public UpdateState State { get; private set; } = UpdateState.Idle;
    public string Message { get; private set; } = "";
    public DateTime? LastCheck { get; private set; }

    /// <summary>Uygulama kurulum dosyasıyla mı kuruldu (geliştirme kopyası otomatik güncellenmez).</summary>
    public bool IsInstalled { get; } = DetectInstalled();

    /// <summary>Durum değişti (UI thread).</summary>
    public event Action? StateChanged;
    /// <summary>Kurulum başlamak üzere: çentikte haber vermek için.</summary>
    public event Action<Version>? Installing;

    private string? _assetUrl;
    private string? _assetName;
    private long _assetSize;
    private bool _busy;

    /// <summary>
    /// Microsoft Store sürümü: güncellemeleri Store yapar; uygulama kendini güncellemez
    /// (Store kuralı), bu yüzden GitHub denetimi tamamen kapalıdır.
    /// </summary>
    public bool IsStoreVersion => Package.IsPackaged;

    /// <summary>Yeni sürüm var mı diye bakar; <paramref name="install"/> ise bulunca indirip kurar.</summary>
    public async Task CheckAsync(bool install)
    {
        if (_busy || IsStoreVersion) return;
        _busy = true;
        try
        {
            Set(UpdateState.Checking, "Güncellemeler denetleniyor…");
            using var http = CreateClient();

            // Önce github.com'un normal "en son sürüm" adresi (saatlik sınırı yok); olmazsa API.
            // API aynı internet bağlantısından saatte 60 istekle sınırlıdır; aşılınca denetim başarısız oluyordu.
            if (!await TryLatestFromWebAsync() && !await TryLatestFromApiAsync(http))
                throw new InvalidOperationException("Sürüm bilgisi alınamadı");
            LastCheck = DateTime.Now;

            if (Latest == null || Latest <= Current || _assetUrl == null)
            {
                Set(UpdateState.UpToDate, $"M#Pro güncel (sürüm {Fmt(Current)})");
                return;
            }

            Set(UpdateState.Available, $"Yeni sürüm hazır: {Fmt(Latest)}");
            if (install) await DownloadAndInstallAsync(http);
        }
        catch (Exception ex)
        {
            LastCheck = DateTime.Now;
            Set(UpdateState.Error, ex is HttpRequestException { StatusCode: null } or TaskCanceledException
                ? "Güncelleme denetlenemedi (internet bağlantısını kontrol edin)"
                : "Güncelleme denetlenemedi, daha sonra tekrar denenecek");
            App.LogError(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Kullanıcı "Şimdi güncelle" dediğinde.</summary>
    public async Task InstallNowAsync()
    {
        if (_busy || IsStoreVersion) return;
        if (_assetUrl == null) { await CheckAsync(install: true); return; }
        _busy = true;
        try
        {
            using var http = CreateClient();
            await DownloadAndInstallAsync(http);
        }
        catch (Exception ex)
        {
            Set(UpdateState.Error, "Güncelleme indirilemedi");
            App.LogError(ex);
        }
        finally { _busy = false; }
    }

    private async Task DownloadAndInstallAsync(HttpClient http)
    {
        if (_assetUrl == null || _assetName == null || Latest == null) return;

        string dir = Path.Combine(Path.GetTempPath(), "MSharpPro-Update");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "*.exe"))
            try { File.Delete(old); } catch { }
        string file = Path.Combine(dir, _assetName);

        Set(UpdateState.Downloading, $"{Fmt(Latest)} indiriliyor… %0");
        using (var resp = await http.GetAsync(_assetUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            if (_assetSize <= 0) _assetSize = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(file);
            var buf = new byte[81920];
            long done = 0;
            int lastPct = -1, n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                int pct = _assetSize > 0 ? (int)(done * 100 / _assetSize) : 0;
                if (pct / 5 != lastPct / 5) { lastPct = pct; Set(UpdateState.Downloading, $"{Fmt(Latest)} indiriliyor… %{pct}"); }
            }
        }

        if (_assetSize > 0 && new FileInfo(file).Length != _assetSize)
            throw new IOException("İndirilen dosya eksik");

        Set(UpdateState.Installing, $"{Fmt(Latest)} kuruluyor…");
        Installing?.Invoke(Latest);
        await Task.Delay(2500); // çentikteki "güncelleniyor" bildirimi görünsün

        // Her zaman sessiz kurulum: "İleri / İleri" ekranları çıkmaz. Kurulum açık uygulamayı kapatır,
        // dosyaları değiştirir ve bitince uygulamayı kendisi yeniden açar (installer'daki [Run] / WizardSilent).
        Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS")
        {
            UseShellExecute = true
        });
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>
    /// github.com/…/releases/latest, en son sürümün sayfasına yönlendirir (…/releases/tag/v1.0.6).
    /// Etiket bu yönlendirmeden okunur; kurulum dosyasının adresi sabittir.
    /// </summary>
    private async Task<bool> TryLatestFromWebAsync()
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var web = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            web.DefaultRequestHeaders.UserAgent.ParseAdd("MSharpPro-Updater");
            using var resp = await web.GetAsync($"https://github.com/{Repo}/releases/latest",
                HttpCompletionOption.ResponseHeadersRead);
            string? loc = resp.Headers.Location?.ToString();
            int i = loc?.LastIndexOf("/tag/", StringComparison.Ordinal) ?? -1;
            if (i < 0) return false;
            string tag = Uri.UnescapeDataString(loc![(i + 5)..]);
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return false;

            Latest = Normalize(latest);
            _assetName = $"MSharpPro-Setup-{Fmt(Latest)}.exe";
            _assetUrl = $"https://github.com/{Repo}/releases/download/{tag}/{_assetName}";
            _assetSize = 0; // indirirken Content-Length'ten alınır
            return true;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private async Task<bool> TryLatestFromApiAsync(HttpClient http)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi));
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            throw new InvalidOperationException($"Geçersiz sürüm etiketi: {tag}");
        Latest = Normalize(latest);

        _assetUrl = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "";
            if (name.StartsWith("MSharpPro-Setup", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                _assetName = name;
                _assetUrl = a.GetProperty("browser_download_url").GetString();
                _assetSize = a.GetProperty("size").GetInt64();
                break;
            }
        }
        return true;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MSharpPro-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private void Set(UpdateState s, string msg)
    {
        State = s;
        Message = msg;
        StateChanged?.Invoke();
    }

    public static string Fmt(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static Version ReadCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return Normalize(v);
    }

    private static bool DetectInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\MSharpPro");
            if (key?.GetValue("InstallPath") is not string path) return false;
            string a = Path.GetFullPath(path).TrimEnd('\\');
            string b = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
