using System.Windows;

namespace MSharpPro;

public partial class App : Application
{
    private const string MutexName = "MSharpPro.SingleInstance";
    private const string ShowEventName = "MSharpPro.ShowSettings";
    /// <summary>Uygulama kendini yeniden başlatırken yeni kopyaya verilen argüman.</summary>
    public const string RestartArg = "--restarting";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _wait;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool created);
        // Yeniden başlatma: eski kopya kapanıp kilidi bırakana kadar bekle (en fazla ~10 sn)
        for (int i = 0; !created && e.Args.Contains(RestartArg) && i < 40; i++)
        {
            _mutex.Dispose();
            Thread.Sleep(250);
            _mutex = new Mutex(true, MutexName, out created);
        }
        if (!created)
        {
            // Zaten çalışıyor: açık olan kopyaya "ayarları göster" sinyali gönder ve çık
            try
            {
                // Kullanıcı bizi başlattığı için öne gelme hakkı bizde; bunu açık olan kopyaya devret
                AllowSetForegroundWindow(-1);
                using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
            }
            catch { }
            Shutdown();
            return;
        }

        // Beklenmeyen bir hata uygulamayı kapatmasın
        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            LogError(ex.Exception);
        };

        base.OnStartup(e);
        _host = new AppHost(Dispatcher);
        _ = StartHostAsync(_host);

        // İkinci kopyadan gelen sinyali bekle (ayrı thread açmadan, thread havuzunda)
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.InvokeAsync(() => _host?.OpenSettings()), null, Timeout.Infinite, false);
    }

    private static async Task StartHostAsync(AppHost host)
    {
        try { await host.StartAsync(); }
        catch (Exception ex) { LogError(ex); }
    }

    private AppHost? _host;

    /// <summary>Hataları %AppData%\MSharpPro\error.log dosyasına yazar (en fazla ~100 KB).</summary>
    public static void LogError(Exception ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSharpPro");
            System.IO.Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, "error.log");
            if (System.IO.File.Exists(file) && new System.IO.FileInfo(file).Length > 100_000) System.IO.File.Delete(file);
            System.IO.File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Shutdown();
        _wait?.Unregister(null);
        _showEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
