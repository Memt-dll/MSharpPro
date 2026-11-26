# M#Pro: Microsoft Store sayfa metinleri

> Not: 'iPhone' ve 'Dynamic Island' Apple'ın ticari markalarıdır; Store sertifikasyonunda sorun çıkmaması için bu metinlerde kullanılmıyor.

Partner Center > Gönderim > Store listeleri bölümüne kopyalanacak metinler.
Gizlilik politikası URL'si: https://github.com/Memt-dll/MSharpPro/blob/main/PRIVACY.md

---

## Türkçe (tr-TR)

**Ürün adı:** M#Pro

**Kısa açıklama (en fazla 100 karakter):**
Windows için yüzen ada tarzı çentik: müzik, Bluetooth, bildirimler ve hava durumu.

**Açıklama:**
M#Pro, ekranınızın üst ortasına şık, yüzen bir ada (çentik) ekler. Fareyle üzerine geldiğinizde açılır; çalan müziği, Bluetooth cihazlarınızı, bildirimlerinizi ve hava durumunu tek bakışta gösterir. Hafiftir: boşta yalnızca birkaç on MB bellek kullanır.

• Müzik: Spotify, YouTube (Chrome, Edge, Firefox) ve diğer oynatıcılarda çalan şarkının kapağı, adı ve sanatçısı. Oynat / duraklat, önceki / sonraki; ilerleme çubuğuyla ileri-geri sarma.
• Bluetooth: Eşleştirilmiş cihazlar ve bağlantı durumu; kulaklığınız bağlanınca çentikte kısa bildirim.
• Bildirimler: Windows bildirimleri çentikte kısa süre görünür ve listede birikir.
• Hava durumu: Anlık sıcaklık, hava durumuna göre renklenen kart ve 4 günlük tahmin.
• İki görünüm: ekranın üstüne yapışık çentik ya da yüzen oval ada. Ortada saat.
• Ctrl + Win ile çentiği yukarı gizleyin; fareyi ekranın üst kenarına götürünce geri iner.
• Birden fazla ekran: ana ekranda, seçtiğiniz ekranda ya da tüm ekranlarda.
• Kişiselleştirme: yazı tipi, boyut, tam ekranda otomatik gizlenme, bilgisayar açılınca başlatma.

**Özellikler (her satır ayrı madde, en fazla 20):**
- Yüzen oval ada ya da klasik çentik görünümü
- Çalan müziğin kapağı, adı ve kontrolleri
- İlerleme çubuğuyla ileri / geri sarma
- Spotify, YouTube ve tarayıcı medya desteği
- Bluetooth cihazları ve bağlantı bildirimleri
- Windows bildirimlerini çentikte gösterme
- Hava durumu ve 4 günlük tahmin
- Ortada saat
- Ctrl + Win ile gizle / göster
- Çoklu ekran desteği
- Tam ekran oyun ve videolarda otomatik gizlenme
- Düşük bellek ve işlemci kullanımı

**Anahtar sözcükler:** çentik, notch, ada, island, müzik, medya kontrol, bluetooth, bildirim, hava durumu, saat

**Bu sürümdeki yenilikler:**
İlk Microsoft Store sürümü.

---

## English (en-US)

**Product name:** M#Pro

**Short description:**
A floating island-style notch for Windows: music, Bluetooth, notifications and weather.

**Description:**
M#Pro adds a sleek floating island (notch) to the top center of your screen. Hover to expand it and see what's playing, your Bluetooth devices, notifications and the weather at a glance. It's lightweight: just a few tens of MB of memory when idle.

• Music: cover art, title and artist from Spotify, YouTube (Chrome, Edge, Firefox) and other players. Play / pause, previous / next, and seek with the progress bar.
• Bluetooth: paired devices and connection status, with a quick alert when your headphones connect.
• Notifications: Windows notifications briefly appear in the notch and are kept in a list.
• Weather: current temperature, a card that changes color with the conditions, and a 4-day forecast.
• Two styles: a notch attached to the top edge, or a floating island pill. Clock in the middle.
• Hide it with Ctrl + Win; move the mouse to the top edge to bring it back.
• Multiple monitors: primary, a chosen monitor, or all monitors.
• Customize font, size, auto-hide in fullscreen and start with Windows.

**Features:**
- Floating island or classic notch
- Now playing: cover art, title and controls
- Seek forward / back on the progress bar
- Works with Spotify, YouTube and browser media
- Bluetooth devices and connection alerts
- Windows notifications in the notch
- Weather with a 4-day forecast
- Clock in the middle
- Hide / show with Ctrl + Win
- Multi-monitor support
- Auto-hides in fullscreen games and videos
- Low memory and CPU usage

**Keywords:** notch, island, music, media controls, bluetooth, notifications, weather, clock

**What's new in this version:** First Microsoft Store release.

---

## Sertifikasyon notları (Partner Center > Gönderim seçenekleri > "Sertifikasyon için notlar")

M#Pro is a WPF desktop app packaged as MSIX. Spotify / YouTube are only mentioned to describe compatibility with their Windows media controls; the app is not affiliated with them.
- runFullTrust: it is a classic desktop (Win32/WPF) app. It shows an always-on-top notch window and uses a low-level keyboard hook only to detect the Ctrl+Win shortcut (keys are not recorded or blocked).
- userNotificationListener: shows incoming Windows notifications in the notch, only after the user grants access in Windows settings. Nothing is sent or stored.
- location: optional, approximate location for the weather forecast (Open-Meteo). Users can pick a city instead.
- bluetooth: shows paired Bluetooth devices and their connection state.
- internetClient: weather data from api.open-meteo.com.
How to test: after launch, a black pill appears at the top center of the primary screen. Hover it to open the panel (Media, Bluetooth, Notifications, Weather tabs). Settings: click the M#Pro tray icon. No account or sign-in is required.

## Diğer form yanıtları

- **Kategori:** Yardımcı programlar ve araçlar (Utilities & tools)
- **Fiyat:** Ücretsiz
- **Yaş derecelendirmesi (IARC anketi):** Kullanıcılar arası iletişim yok, kullanıcı içeriği paylaşımı yok, konum paylaşımı yok (konum yalnızca hava durumu için, paylaşılmıyor), dijital satın alma yok, şiddet / cinsellik / kumar içeriği yok -> beklenen derece: 3+ / Herkes
- **Sistem gereksinimleri:** Windows 10 sürüm 2004 (19041) veya üstü, x64
