# M#Pro Gizlilik Politikası

_Son güncelleme: 26 Kasım 2026_

M#Pro, Windows için bir çentik (Dynamic Island) uygulamasıdır. Kişisel verilerinizi toplamaz, satmaz ve hiçbir sunucuya göndermez. Uygulamanın kendi sunucusu yoktur.

## Bilgisayarınızda okunan bilgiler

Aşağıdaki bilgiler yalnızca çentikte göstermek için **bilgisayarınızda** okunur; hiçbir yere gönderilmez ve kaydedilmez:

- **Çalan medya:** Windows'un medya kontrollerinden şarkı / video adı, sanatçı ve kapak resmi.
- **Bildirimler:** Windows'ta izin verdiğinizde gelen bildirimlerin başlığı ve metni. Son 20 bildirim yalnızca uygulama açıkken bellekte tutulur.
- **Bluetooth:** Eşleştirilmiş cihazların adı ve bağlı olup olmadıkları.

## İnternete gönderilen bilgiler

- **Hava durumu:** Hava durumu açıksa, seçtiğiniz şehrin ya da Windows konumunuzun **yaklaşık koordinatları** (enlem / boylam) [Open-Meteo](https://open-meteo.com) servisine gönderilir. Şehir aradığınızda yazdığınız şehir adı Open-Meteo'nun arama servisine gönderilir. Bu istekler kimliğinizi içermez. Hava durumunu ayarlardan kapatabilirsiniz.
- **Güncelleme denetimi (yalnızca GitHub sürümü):** Yeni sürüm olup olmadığını öğrenmek için GitHub'a istek gönderilir. Microsoft Store sürümünde bu yapılmaz; güncellemeleri Store yönetir.

## Bilgisayarınızda saklananlar

Ayarlarınız (yazı tipi, boyut, seçilen şehir vb.) yalnızca bilgisayarınızda, `%AppData%\MSharpPro\settings.json` dosyasında saklanır. Uygulamayı kaldırdığınızda silinir.

## Çocukların gizliliği

M#Pro kimseden, çocuklar dahil, kişisel bilgi toplamaz.

## İletişim

Sorularınız için: https://github.com/Memt-dll/MSharpPro/issues

---

# M#Pro Privacy Policy (English)

M#Pro is a notch (Dynamic Island) app for Windows. It does not collect, sell or send your personal data to any server. The app has no server of its own.

- **Read locally only (never sent or stored):** the currently playing media (title, artist, cover) from Windows media controls; the title and text of notifications, if you grant notification access (the last 20 are kept in memory only while the app runs); names and connection state of paired Bluetooth devices.
- **Sent over the internet:** if weather is enabled, the approximate coordinates of your chosen city or Windows location are sent to [Open-Meteo](https://open-meteo.com); city searches send the typed city name to Open-Meteo's geocoding service. No identity is included. Weather can be turned off in settings. The GitHub edition also checks GitHub for new versions; the Microsoft Store edition does not (the Store handles updates).
- **Stored locally:** your settings in `%AppData%\MSharpPro\settings.json`, removed when the app is uninstalled.
- M#Pro does not collect personal information from anyone, including children.
- Contact: https://github.com/Memt-dll/MSharpPro/issues
