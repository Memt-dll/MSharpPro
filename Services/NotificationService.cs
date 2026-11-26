using System.Windows.Media;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace MSharpPro.Services;

public sealed class NotifItem
{
    public string App { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string Time { get; init; } = "";
    public ImageSource? Icon { get; init; }
    public System.Windows.Visibility IconVisibility =>
        Icon != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
}

/// <summary>Windows bildirimlerini okur (Ayarlar > Gizlilik > Bildirimler erişimi gerekir).</summary>
public sealed class NotificationService
{
    private UserNotificationListener? _listener;
    private readonly HashSet<uint> _seen = new();

    public event Action<NotifItem>? Received;

    public async Task<bool> StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed) return false;

            // Uygulama açılmadan önceki bildirimleri "görüldü" say
            foreach (var n in await _listener.GetNotificationsAsync(NotificationKinds.Toast))
                _seen.Add(n.Id);

            _ = PollAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Paketlenmemiş masaüstü uygulamalarında NotificationChanged olayı çalışmadığı için
    // saniyede bir hafif bir kontrol yapılır.
    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var list = await _listener!.GetNotificationsAsync(NotificationKinds.Toast);
                var current = new HashSet<uint>();
                foreach (var n in list)
                {
                    current.Add(n.Id);
                    if (!_seen.Add(n.Id)) continue;
                    var item = await ToItemAsync(n);
                    if (item != null) Received?.Invoke(item);
                }
                _seen.IntersectWith(current);
            }
            catch { }
        }
    }

    private static async Task<NotifItem?> ToItemAsync(UserNotification n)
    {
        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        if (binding == null) return null;

        var texts = binding.GetTextElements()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        string app = "";
        ImageSource? icon = null;
        try
        {
            app = n.AppInfo.DisplayInfo.DisplayName;
            icon = await ImageUtil.LoadAsync(
                n.AppInfo.DisplayInfo.GetLogo(new global::Windows.Foundation.Size(48, 48)), 48);
        }
        catch { }

        return new NotifItem
        {
            App = app,
            Title = texts.FirstOrDefault() ?? app,
            Body = string.Join(" · ", texts.Skip(1)),
            Time = n.CreationTime.LocalDateTime.ToString("HH:mm"),
            Icon = icon
        };
    }
}
