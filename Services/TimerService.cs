using System.Windows.Threading;
using static MSharpPro.Services.Loc;

namespace MSharpPro.Services;

/// <summary>Geri sayım zamanlayıcısı ve Pomodoro (odak / mola döngüsü). Tüm ekranlar aynı zamanlayıcıyı paylaşır.</summary>
public sealed class TimerService
{
    public enum Phase { None, Countdown, Focus, Break }

    private readonly DispatcherTimer _tick;
    private readonly AppSettings _settings;
    private DateTime _endsAt;
    private TimeSpan _remainingWhenPaused;

    public Phase Current { get; private set; } = Phase.None;
    public bool Running { get; private set; }
    public TimeSpan Total { get; private set; }
    public int PomodoroRound { get; private set; }

    public TimeSpan Remaining => Current == Phase.None ? TimeSpan.Zero
        : Running ? Max0(_endsAt - DateTime.Now) : _remainingWhenPaused;

    public bool Active => Current != Phase.None;

    /// <summary>Her saniye ve durum değişince (UI thread).</summary>
    public event Action? Changed;
    /// <summary>Süre doldu: (başlık, açıklama).</summary>
    public event Action<string, string>? Finished;

    public TimerService(AppSettings settings)
    {
        _settings = settings;
        _tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => OnTick();
    }

    public void StartCountdown(TimeSpan length)
    {
        Current = Phase.Countdown;
        PomodoroRound = 0;
        Begin(length);
    }

    public void StartPomodoro()
    {
        PomodoroRound = 1;
        Current = Phase.Focus;
        Begin(TimeSpan.FromMinutes(_settings.PomodoroFocus));
    }

    /// <summary>Süreye dakika ekler (çalışıyorsa).</summary>
    public void Add(TimeSpan t)
    {
        if (!Active) { StartCountdown(t); return; }
        Total += t;
        if (Running) _endsAt += t; else _remainingWhenPaused += t;
        Changed?.Invoke();
    }

    public void PauseResume()
    {
        if (!Active) return;
        if (Running)
        {
            _remainingWhenPaused = Remaining;
            Running = false;
            _tick.Stop();
        }
        else
        {
            _endsAt = DateTime.Now + _remainingWhenPaused;
            Running = true;
            _tick.Start();
        }
        Changed?.Invoke();
    }

    public void Stop()
    {
        Current = Phase.None;
        Running = false;
        PomodoroRound = 0;
        _tick.Stop();
        Changed?.Invoke();
    }

    private void Begin(TimeSpan length)
    {
        Total = length;
        _endsAt = DateTime.Now + length;
        Running = true;
        _tick.Stop();
        _tick.Start();
        Changed?.Invoke();
    }

    private void OnTick()
    {
        if (Remaining > TimeSpan.Zero) { Changed?.Invoke(); return; }

        System.Media.SystemSounds.Asterisk.Play();
        switch (Current)
        {
            case Phase.Focus:
                Finished?.Invoke(L("Odak süresi bitti"), L("{0} dakika mola zamanı", _settings.PomodoroBreak));
                Current = Phase.Break;
                Begin(TimeSpan.FromMinutes(_settings.PomodoroBreak));
                return;
            case Phase.Break:
                Finished?.Invoke(L("Mola bitti"), L("{0}. odak turu başlıyor", PomodoroRound + 1));
                PomodoroRound++;
                Current = Phase.Focus;
                Begin(TimeSpan.FromMinutes(_settings.PomodoroFocus));
                return;
            default:
                Finished?.Invoke(L("Süre doldu"), L("{0} zamanlayıcısı tamamlandı", Format(Total)));
                Stop();
                return;
        }
    }

    public string PhaseText => Current switch
    {
        Phase.Focus => L("Odak · {0}. tur", PomodoroRound),
        Phase.Break => L("Mola"),
        Phase.Countdown => L("Zamanlayıcı"),
        _ => L("Zamanlayıcı"),
    };

    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}
