using System.Collections.ObjectModel;
using System.Windows.Threading;
using Windows.ApplicationModel.Appointments;
using static MSharpPro.Services.Loc;

namespace MSharpPro.Services;

public sealed class CalendarEvent
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Location { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool AllDay { get; init; }

    public string When
    {
        get
        {
            string day = Start.Date == DateTime.Today ? L("Bugün")
                : Start.Date == DateTime.Today.AddDays(1) ? L("Yarın")
                : Start.ToString("d MMM ddd", Loc.English ? System.Globalization.CultureInfo.GetCultureInfo("en-US")
                    : System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
            return AllDay ? $"{day} · {L("Tüm gün")}" : $"{day} · {Start:HH:mm}–{End:HH:mm}";
        }
    }

    public string NowLabel => L("Şimdi");
    public bool IsNow => !AllDay && Start <= DateTime.Now && DateTime.Now < End;
    public System.Windows.Visibility NowVisibility => IsNow ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility LocationVisibility =>
        Location.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
}

/// <summary>
/// Windows takviminden (Takvim / Outlook hesapları) yaklaşan etkinlikleri okur.
/// 5 dakikada bir yenilenir; etkinlikten 5 dakika önce haber verir.
/// </summary>
public sealed class CalendarService
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _refresh, _reminder;
    private readonly HashSet<string> _reminded = new();
    private AppointmentStore? _store;

    public ObservableCollection<CalendarEvent> Events { get; } = new();
    /// <summary>Hata / erişim sorunu metni; sorun yoksa null.</summary>
    public string? Problem { get; private set; } = L("Takvim yükleniyor…");

    public event Action? Changed;
    public event Action<CalendarEvent>? Upcoming;

    public CalendarService(AppSettings settings)
    {
        _settings = settings;
        _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(5) };
        _refresh.Tick += async (_, _) => await RefreshAsync();
        _reminder = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _reminder.Tick += (_, _) => CheckReminders();
    }

    public async Task StartAsync()
    {
        await RefreshAsync();
        _refresh.Start();
        _reminder.Start();
    }

    public async Task RefreshAsync()
    {
        if (Demo.On) { Problem = null; Changed?.Invoke(); return; }
        try
        {
            _store ??= await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
            if (_store == null) throw new UnauthorizedAccessException();
            var options = new FindAppointmentsOptions { MaxCount = 12, IncludeHidden = false };
            foreach (var p in new[] { AppointmentProperties.Subject, AppointmentProperties.Location,
                         AppointmentProperties.StartTime, AppointmentProperties.Duration, AppointmentProperties.AllDay })
                options.FetchProperties.Add(p);

            var start = DateTime.Today;
            var list = await _store.FindAppointmentsAsync(start, TimeSpan.FromDays(3), options);
            var items = list
                .Select(a => new CalendarEvent
                {
                    Id = a.LocalId + "|" + a.StartTime.ToString("o"),
                    Title = string.IsNullOrWhiteSpace(a.Subject) ? L("(Başlıksız etkinlik)") : a.Subject,
                    Location = a.Location ?? "",
                    Start = a.StartTime.LocalDateTime,
                    End = a.StartTime.LocalDateTime + a.Duration,
                    AllDay = a.AllDay,
                })
                .Where(e => e.End > DateTime.Now)
                .OrderBy(e => e.AllDay ? e.Start.Date : e.Start)
                .Take(8)
                .ToList();

            Events.Clear();
            foreach (var e in items) Events.Add(e);
            Problem = items.Count == 0 ? L("Önümüzdeki 3 günde etkinlik yok") : null;
        }
        catch (Exception ex)
        {
            _store = null;
            Events.Clear();
            Problem = ex is UnauthorizedAccessException
                ? L("Takvime erişim izni yok")
                : L("Takvim okunamadı");
            if (ex is not UnauthorizedAccessException) App.LogError(ex);
        }
        Changed?.Invoke();
    }

    public bool NoAccess => Problem == L("Takvime erişim izni yok");

    private void CheckReminders()
    {
        if (!_settings.CalendarReminders) return;
        var now = DateTime.Now;
        foreach (var e in Events)
        {
            if (e.AllDay || e.Start <= now || e.Start - now > TimeSpan.FromMinutes(5)) continue;
            if (_reminded.Add(e.Id)) Upcoming?.Invoke(e);
        }
    }
}
