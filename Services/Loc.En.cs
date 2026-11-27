namespace MSharpPro.Services;

public static partial class Loc
{
    /// <summary>Türkçe → İngilizce sözlük. Yeni bir arayüz metni eklenince buraya da eklenmeli.</summary>
    private static readonly Dictionary<string, string> En = new()
    {
        // ---- Genel
        ["Ayarlar"] = "Settings",
        ["Ayarları aç"] = "Open settings",
        ["Temizle"] = "Clear",
        ["Yenile"] = "Refresh",
        ["Kaldır"] = "Remove",
        ["Sistem"] = "System",
        ["Açık"] = "Light",
        ["Koyu"] = "Dark",
        ["Türkçe"] = "Türkçe",
        ["Şimdi"] = "Now",
        ["Bugün"] = "Today",
        ["Yarın"] = "Tomorrow",
        ["Tüm gün"] = "All day",
        ["Konumum"] = "My location",
        ["Çıkış"] = "Exit",
        ["ayarlar"] = "settings",

        // ---- Çentik: sekmeler
        ["Müzik"] = "Music",
        ["Medya"] = "Media",
        ["Şarkı sözleri"] = "Lyrics",
        ["Bildirimler"] = "Notifications",
        ["Hava durumu"] = "Weather",
        ["Zamanlayıcı"] = "Timer",
        ["Takvim"] = "Calendar",
        ["Kopyalananlar"] = "Clipboard",
        ["Dosya rafı"] = "File shelf",

        // ---- Çentik: müzik
        ["Şu anda çalan bir şey yok"] = "Nothing is playing",
        ["Şimdi çalıyor · {0}"] = "Now playing · {0}",
        ["Önceki"] = "Previous",
        ["Sonraki"] = "Next",
        ["Oynat / Duraklat"] = "Play / Pause",
        ["Medya Oynatıcı"] = "Media Player",

        // ---- Çentik: bluetooth
        ["Bluetooth ayarları"] = "Bluetooth settings",
        ["Eşleştirilmiş Bluetooth cihazı yok"] = "No paired Bluetooth devices",
        ["Bağlı"] = "Connected",
        ["Bağlı değil"] = "Not connected",
        ["Bağlandı"] = "Connected",
        ["Bağlantı kesildi"] = "Disconnected",

        // ---- Çentik: bildirimler
        ["Yeni bildirim yok"] = "No new notifications",
        ["Bildirimlere erişim izni yok"] = "No access to notifications",
        ["Ayarlar › Gizlilik › Bildirimler'den izin ver"] = "Allow it in Settings › Privacy › Notifications",
        ["Teams bildirimleri için: Teams › Ayarlar › Bildirimler › Bildirim stili ›"] =
            "For Teams notifications: Teams › Settings › Notifications › Notification style ›",
        ["Bildirimleri sessize al"] = "Mute notifications",
        ["Bildirimler sessizde · açmak için tıkla"] = "Notifications muted · click to unmute",
        ["Bu uygulamanın bildirimlerini çentikte gösterme"] = "Don't pop up this app's notifications",
        ["Uygulamayı aç"] = "Open app",

        // ---- Çentik: hava durumu
        ["Ayarlar'dan şehrinizi seçin"] = "Choose your city in Settings",
        ["Hava durumu yükleniyor…"] = "Loading weather…",
        ["Hava durumu alınamadı"] = "Couldn't load the weather",
        ["En yüksek {0}°  ·  En düşük {1}°"] = "High {0}°  ·  Low {1}°",
        ["Hissedilen {0}°  ·  Nem %{1}  ·  Rüzgâr {2} km/s"] = "Feels like {0}°  ·  Humidity {1}%  ·  Wind {2} km/h",
        ["Güneşli"] = "Sunny",
        ["Az bulutlu"] = "Mostly clear",
        ["Parçalı bulutlu"] = "Partly cloudy",
        ["Bulutlu"] = "Cloudy",
        ["Sisli"] = "Foggy",
        ["Çisenti"] = "Drizzle",
        ["Yağmurlu"] = "Rain",
        ["Karlı"] = "Snow",
        ["Sağanak yağışlı"] = "Showers",
        ["Kar sağanağı"] = "Snow showers",
        ["Gök gürültülü"] = "Thunderstorm",

        // ---- Çentik: sözler
        ["Sözler aranıyor…"] = "Looking for lyrics…",
        ["Bu şarkının sözleri bulunamadı"] = "No lyrics found for this song",
        ["Sözler alınamadı (internet bağlantısını kontrol edin)"] = "Couldn't load lyrics (check your internet connection)",

        // ---- Çentik: zamanlayıcı
        ["Hızlı başlat"] = "Quick start",
        ["Başlat / Duraklat"] = "Start / Pause",
        ["Durdur"] = "Stop",
        ["1 dk"] = "1 min",
        ["5 dk"] = "5 min",
        ["10 dk"] = "10 min",
        ["15 dk"] = "15 min",
        ["30 dk"] = "30 min",
        ["1 sa"] = "1 hr",
        ["+1 dk"] = "+1 min",
        ["Mola"] = "Break",
        ["Odak · {0}. tur"] = "Focus · round {0}",
        ["Odak süresi bitti"] = "Focus time is over",
        ["{0} dakika mola zamanı"] = "Time for a {0}-minute break",
        ["Mola bitti"] = "Break is over",
        ["{0}. odak turu başlıyor"] = "Focus round {0} is starting",
        ["Süre doldu"] = "Time's up",
        ["{0} zamanlayıcısı tamamlandı"] = "{0} timer finished",

        // ---- Çentik: takvim
        ["Takvim yükleniyor…"] = "Loading calendar…",
        ["Takvime erişim izni yok"] = "No access to the calendar",
        ["Takvim okunamadı"] = "Couldn't read the calendar",
        ["Önümüzdeki 3 günde etkinlik yok"] = "No events in the next 3 days",
        ["(Başlıksız etkinlik)"] = "(Untitled event)",
        ["{0} başlıyor"] = "Starts at {0}",

        // ---- Çentik: kopyalananlar ve raf
        ["Kopyaladığınız metinler burada görünür"] = "Text you copy shows up here",
        ["Tıklayınca yeniden kopyalanır · yalnızca bellekte tutulur"] = "Click to copy again · kept in memory only",
        ["Kopyalandı"] = "Copied",
        ["Dosyaları çentiğe sürükleyip bırakın"] = "Drag and drop files onto the notch",
        ["Dışarı sürükleyerek kopyalayın · çift tıkla aç · sağ tıkla kaldır"] =
            "Drag out to copy · double-click to open · right-click to remove",

        // ---- Çentik: sistem
        ["İşlemci"] = "CPU",
        ["Bellek"] = "Memory",
        ["Ağ"] = "Network",
        ["{0:0.0} / {1:0} GB kullanımda"] = "{0:0.0} / {1:0} GB in use",
        // ---- Çentik: denetim
        ["Denetim"] = "Controls",
        ["Mikrofon"] = "Microphone",
        ["Uyutma"] = "Stay awake",
        ["Uçak modu"] = "Airplane mode",
        ["Kapalı"] = "Off",
        ["Bulunamadı"] = "Not found",
        ["Kapatana kadar"] = "Until turned off",
        ["Bitiş {0}"] = "Until {0}",
        ["Ses çıkışı"] = "Sound output",
        ["Ses aygıtı"] = "Audio device",
        ["Uygulama sesleri"] = "App volume",
        ["Sistem sesleri"] = "System sounds",
        ["Şu an ses çıkaran uygulama yok"] = "No apps are playing sound",
        ["Bilgisayarın uyumasını ve ekranın kararmasını engeller"] = "Keeps the PC from sleeping and the screen from dimming",
        ["Bilgisayar uyumayacak"] = "PC will stay awake",
        ["Mikrofon kapalı"] = "Microphone off",
        ["Mikrofon açık"] = "Microphone on",
        ["Mikrofon bulunamadı"] = "No microphone found",
        ["Bağlı bir mikrofon yok"] = "No microphone is connected",
        ["Tüm uygulamalarda sessiz · Ctrl+Alt+M ile aç"] = "Muted in every app · Ctrl+Alt+M to unmute",
        ["Sizi yeniden duyabiliyorlar"] = "People can hear you again",
        ["Tüm uygulamalarda sessize al / aç · Ctrl+Alt+M"] = "Mute / unmute in every app · Ctrl+Alt+M",
        ["Uçak modu değiştirilemedi"] = "Couldn't change airplane mode",
        ["Ayarlar açılıyor"] = "Opening Settings",
        ["Uçak modu açık"] = "Airplane mode on",
        ["Uçak modu kapalı"] = "Airplane mode off",
        ["Wi-Fi, Bluetooth ve mobil bağlantı kapalı"] = "Wi-Fi, Bluetooth and cellular are off",
        ["Kablosuz bağlantılar açık"] = "Wireless connections are on",
        ["Katıl"] = "Join",
        ["katılmak için tıklayın"] = "click to join",
        ["Mikrofon kısayolu"] = "Microphone shortcut",
        ["Mikrofonu Teams, Zoom, Discord dahil tüm uygulamalarda tek tuşla sessize alır / açar; kapalıyken çentikte kırmızı mikrofon görünür"] =
            "Mutes / unmutes the microphone in every app, including Teams, Zoom and Discord; a red mic shows in the notch while muted",

        ["Belleği boşalt"] = "Free up memory",
        ["Boşaltılıyor…"] = "Freeing…",
        ["{0} boşaltıldı"] = "{0} freed",
        ["Zaten boş"] = "Already lean",
        ["Uygulamaların kullanmadığı belleği geri alır; hiçbir uygulama kapanmaz"] =
            "Reclaims memory apps aren't using; no app is closed",

        // ---- Çentik: göstergeler
        ["Ses"] = "Volume",
        ["Sessiz"] = "Muted",
        ["Parlaklık"] = "Brightness",
        ["Şarj oluyor"] = "Charging",
        ["Şarj aleti çıkarıldı"] = "Charger unplugged",
        ["Pil %{0}"] = "Battery {0}%",
        ["Pil azaldı"] = "Battery low",
        ["%{0} kaldı · şarj aletini takın"] = "{0}% left · plug in the charger",
        ["Mikrofon kullanımda"] = "Microphone in use",
        ["Kamera kullanımda"] = "Camera in use",

        // ---- Güncelleme
        ["M#Pro güncelleniyor"] = "Updating M#Pro",
        ["Sürüm {0} kuruluyor, birazdan yeniden açılacak"] = "Installing version {0}, it will reopen shortly",
        ["M#Pro güncellendi"] = "M#Pro updated",
        ["Sürüm {0} yüklendi · yenilikler için tıklayın"] = "Version {0} installed · click to see what's new",
        ["Güncellemeler denetleniyor…"] = "Checking for updates…",
        ["M#Pro güncel (sürüm {0})"] = "M#Pro is up to date (version {0})",
        ["Yeni sürüm hazır: {0}"] = "New version available: {0}",
        ["Güncelleme denetlenemedi (internet bağlantısını kontrol edin)"] = "Couldn't check for updates (check your internet connection)",
        ["Güncelleme denetlenemedi, daha sonra tekrar denenecek"] = "Couldn't check for updates, will try again later",
        ["Güncelleme indirilemedi"] = "Couldn't download the update",
        ["Güncellemeleri denetle"] = "Check for updates",
        ["Microsoft Store'da aç"] = "Open in Microsoft Store",

        // ---- Tepsi menüsü
        ["Çentiği gizle"] = "Hide notch",
        ["Bilgisayar açılınca başlat"] = "Start with Windows",

        // ---- Ayarlar: genel
        ["Sürüm {0}"] = "Version {0}",
        ["Sürüm 1.0.0"] = "Version 1.0.0",
        ["Görünüm"] = "Appearance",
        ["Sekmeler"] = "Tabs",
        ["Davranış"] = "Behavior",
        ["Güncelleme"] = "Updates",
        ["Varsayılanlara dön"] = "Reset to defaults",
        ["Varsayılanlara dönülsün mü?"] = "Reset to defaults?",
        ["Görünüm, sekmeler, davranış ve göstergelerdeki tüm ayarlarınız ilk hâline döner. Bu işlem geri alınamaz."] =
            "All your appearance, tab, behavior and indicator settings go back to how they were at first. This can't be undone.",
        ["Seçtiğiniz şehir ve arayüz dili korunur."] = "Your chosen city and interface language are kept.",
        ["Vazgeç"] = "Cancel",
        ["Ayarlar varsayılanlara döndü"] = "Settings were reset to defaults",
        ["Ayarları dışa aktar"] = "Export settings",
        ["Ayarları içe aktar"] = "Import settings",
        ["Ayarlar kaydedildi"] = "Settings saved",
        ["Ayarlar yüklendi"] = "Settings imported",
        ["Dosya yazılamadı"] = "Couldn't write the file",
        ["Bu dosya geçerli bir M#Pro ayar dosyası değil"] = "This isn't a valid M#Pro settings file",
        ["Tema, dil, çentiğin biçimi, ekranı, yazı tipi ve boyutu"] = "Theme, language, and the notch's shape, display, font and size",
        ["Çentik panelinde hangi sekmelerin hangi sırayla görüneceği"] = "Which tabs the notch panel shows, and in what order",
        ["Başlangıç, bildirimler, göstergeler ve kısayol"] = "Startup, notifications, indicators and shortcut",
        ["Çentikte ve panelde gösterilen hava durumu"] = "Weather shown in the notch and the panel",
        ["Sürüm bilgisi ve otomatik güncelleme"] = "Version info and automatic updates",

        // ---- Ayarlar: görünüm
        ["Uygulama"] = "App",
        ["Tema"] = "Theme",
        ["Sistem: Windows'ta açık mod ya da koyu mod hangisi seçiliyse"] = "System: follows Windows light or dark mode",
        ["Dil"] = "Language",
        ["Sistem: Windows Türkçe ise Türkçe, değilse İngilizce"] = "System: Turkish if Windows is in Turkish, otherwise English",
        ["Dil, M#Pro yeniden başlatılınca değişir"] = "The language changes after M#Pro restarts",
        ["Yeniden başlat"] = "Restart",
        ["Çentik"] = "Notch",
        ["Ada"] = "Island",
        ["Görünüm stili"] = "Style",
        ["Ekranın üstüne yapışık çentik ya da yüzen oval ada"] = "A notch attached to the top of the screen, or a floating pill",
        ["Ortada saati göster"] = "Show clock in the middle",
        ["Kompakt çentiğin ortasında saat"] = "Clock in the middle of the compact notch",
        ["Gösterileceği ekran"] = "Display",
        ["Birden fazla ekran varsa"] = "If you have more than one display",
        ["Ana ekran"] = "Main display",
        ["Tüm ekranlar"] = "All displays",
        ["Ekran {0}"] = "Display {0}",
        [" (ana)"] = " (main)",
        ["{0} ekran bağlı"] = "{0} displays connected",
        ["Tek ekran bağlı; ikinci ekran takınca seçenekler çıkar"] = "One display connected; more options appear with a second one",
        ["Yazı tipi"] = "Font",
        ["Çentikteki tüm yazılar"] = "All text in the notch",
        ["Çentik boyutu"] = "Notch size",
        ["Çentiği ve içindekileri büyütür ya da küçültür"] = "Makes the notch and its contents bigger or smaller",

        // ---- Ayarlar: sekmeler
        ["Sıra ve görünürlük"] = "Order and visibility",
        ["Yukarı taşı"] = "Move up",
        ["Aşağı taşı"] = "Move down",
        ["Sekme seçenekleri"] = "Tab options",
        ["LRCLIB'den zamanlı olarak alınır; bunun için şarkı adı ve sanatçı bu servise gönderilir"] =
            "Fetched with timing from LRCLIB; the song title and artist are sent to that service",
        ["Son 12 kopyalanan metin yalnızca bellekte tutulur; parola yöneticilerinin kopyaları alınmaz"] =
            "The last 12 copied texts are kept in memory only; password manager copies are skipped",
        ["Pomodoro"] = "Pomodoro",
        ["Odak ve mola süresi (dakika)"] = "Focus and break length (minutes)",

        // ---- Ayarlar: davranış
        ["Bilgisayar açılınca başlat"] = "Start with Windows",
        ["Windows oturumu açıldığında M#Pro otomatik çalışır"] = "M#Pro starts automatically when you sign in to Windows",
        ["Tam ekranda gizle"] = "Hide in full screen",
        ["Oyun, video veya sunum tam ekrandayken çentik görünmez"] = "The notch hides while a game, video or presentation is full screen",
        ["Bildirimler ve gizleme"] = "Notifications and hiding",
        ["Çentikte açılır bildirim gösterilmez; gelen bildirimler yine listede birikir"] =
            "No pop-ups in the notch; incoming notifications still collect in the list",
        ["Çentiği yukarı gizle"] = "Hide the notch",
        ["Gizliyken fareyi ekranın üst kenarına götürünce çentik aşağı iner; bildirimler yine görünür"] =
            "While hidden, move the mouse to the top edge to bring it down; notifications still show",
        ["Kısayol"] = "Shortcut",
        ["İkisine birlikte basıp bırakınca çentik yukarı gizlenir / geri gelir"] = "Press and release both together to hide / show the notch",
        ["Göstergeler"] = "Indicators",
        ["Ses ve parlaklık"] = "Volume and brightness",
        ["Ses ya da ekran parlaklığı değişince çentikte düzey çubuğu gösterilir"] = "Shows a level bar in the notch when volume or brightness changes",
        ["Pil"] = "Battery",
        ["Şarj aleti takılınca / çıkarılınca ve pil %20 ile %10'a düşünce uyarır"] =
            "Alerts when the charger is plugged in or out, and at 20% and 10% battery",
        ["Mikrofon ve kamera"] = "Microphone and camera",
        ["Bir uygulama kullanırken çentikte turuncu (mikrofon) ya da yeşil (kamera) nokta yanar"] =
            "An orange (microphone) or green (camera) dot lights up while an app uses them",
        ["Takvim hatırlatıcısı"] = "Calendar reminders",
        ["Windows takvimindeki etkinlikten 5 dakika önce çentikte haber verir"] = "Alerts in the notch 5 minutes before a Windows calendar event",
        ["Sessize alınan uygulamalar"] = "Muted apps",
        ["Yok. Çentikteki bildirim listesinde bir bildirimin yanındaki zil simgesiyle ekleyebilirsiniz"] =
            "None. Add one with the bell icon next to a notification in the notch's list",

        // ---- Ayarlar: hava durumu
        ["Hava durumunu göster"] = "Show weather",
        ["Müzik çalmıyorken çentikte, ayrıca panelde 'Hava' sekmesinde (30 dakikada bir yenilenir)"] =
            "In the notch when no music is playing, and in the panel's Weather tab (refreshed every 30 minutes)",
        ["Konum"] = "Location",
        ["Konumumu kullan"] = "Use my location",
        ["Şehir ara (ör. Ankara)"] = "Search for a city (e.g. London)",
        ["Ara"] = "Search",
        ["Aranıyor…"] = "Searching…",
        ["Sonuç bulunamadı (internet bağlantısını ya da yazımı kontrol edin)"] = "No results (check your connection or spelling)",
        ["Listeden şehrinizi seçin"] = "Pick your city from the list",
        ["Konum alınıyor…"] = "Getting location…",
        ["Konum alınamadı: Ayarlar › Gizlilik › Konum'da masaüstü uygulamalarına izin verin ya da şehir arayın"] =
            "Couldn't get location: allow desktop apps in Settings › Privacy › Location, or search for a city",
        ["Seçilmedi: bir şehir arayın ya da konumunuzu kullanın"] = "Not set: search for a city or use your location",
        ["Seçili: {0}"] = "Selected: {0}",

        // ---- Ayarlar: güncelleme
        ["Otomatik güncelle"] = "Update automatically",
        ["GitHub'a yeni sürüm gelince arka planda indirir, kurar ve yeniden açar (saatte bir denetlenir)"] =
            "Downloads, installs and reopens in the background when a new version is on GitHub (checked hourly)",
        ["Güncellemeler henüz denetlenmedi"] = "Updates haven't been checked yet",
        ["Güncelle"] = "Update",
        ["Şimdi denetle"] = "Check now",
        ["Yenilikler"] = "What's new",
        ["Yüklü sürüm: {0}"] = "Installed version: {0}",
        ["Güncellemeler Microsoft Store üzerinden otomatik gelir"] = "Updates come automatically through Microsoft Store",
        ["Store'da aç"] = "Open in Store",
        ["Son denetim: {0}"] = "Last checked: {0}",
        ["Henüz denetlenmedi"] = "Not checked yet",
        ["Geliştirme kopyası: arka planda kurulmaz, 'Güncelle' ile kurulur"] = "Development copy: doesn't install in the background, use 'Update'",
    };
}
