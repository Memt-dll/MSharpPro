<p align="center">
  <img src="Assets/icon-1024.png" width="128" alt="M#Pro"/>
</p>

<h1 align="center">M#Pro</h1>

<p align="center">Windows için hafif, "dynamic island" tarzı çentik uygulaması (WPF / .NET 10)</p>

---

## Kurulum

<p align="center">
  <a href="https://github.com/Memt-dll/MSharpPro/raw/main/Setup/MSharpPro-Setup.exe">
    <img src="https://img.shields.io/badge/%E2%AC%87%20M%23Pro%20Kurulumunu%20%C4%B0ndir-Windows%2010%20%2F%2011-222622?style=for-the-badge" alt="M#Pro Kurulumunu İndir"/>
  </a>
</p>

1. Yukarıdaki butona tıklayın ya da [`Setup/MSharpPro-Setup.exe`](Setup/MSharpPro-Setup.exe) dosyasını açıp **Download** deyin. Aynı dosya [Releases](https://github.com/Memt-dll/MSharpPro/releases/latest) sayfasında da var.
2. İndirilen `MSharpPro-Setup.exe` dosyasını çalıştırın. Yönetici izni gerekmez, ayrıca .NET kurmanıza da gerek yoktur.
3. **"Windows bilgisayarınızı korudu"** uyarısı çıkarsa **Ek bilgi › Yine de çalıştır** deyin. Uygulama ücretli bir sertifikayla imzalı olmadığı için bu uyarı çıkar.
4. Kurulumda isterseniz "Bilgisayar açılınca M#Pro'yu başlat" ve masaüstü kısayolu seçeneklerini işaretleyin.

**Gereksinim:** 64 bit Windows 10 (sürüm 2004 ve üzeri) veya Windows 11.

Kaldırmak için: **Ayarlar › Uygulamalar › M#Pro › Kaldır**.

## Özellikler
- **Çentik:** Ekranın üst ortasında durur. Çalan şarkının kapağını ve hareketli bir ekolayzer gösterir; okunmamış bildirim varsa küçük bir nokta çıkar.
- **İki görünüm stili:** ekranın üstüne yapışık **Çentik** ya da yüzen, oval **Ada**. Kompakt hâlin ortasında saat gösterilir (kapatılabilir).
- **Fareyle üzerine gelince açılır:** Medya / Bluetooth / Bildirimler / Hava sekmeleri.
- **Hava durumu:** Müzik çalmıyorken çentikte sıcaklık; "Hava" sekmesinde hava durumuna göre renklenen kart ve 4 günlük tahmin (sıcaklık aralığı çubuklarıyla). Veri [Open-Meteo](https://open-meteo.com)'dan alınır (ücretsiz, anahtar gerektirmez), 30 dakikada bir yenilenir. Konum: Windows konumu ya da ayarlardan aranan şehir.
- **Medya:** Kapak resmi, şarkı adı, sanatçı, uygulama, ilerleme çubuğu; önceki / oynat-duraklat / sonraki. Arka planda kapak resminden bulanık bir ortam rengi oluşur. Spotify, YouTube (Chrome / Edge / Firefox), Medya Oynatıcı vb. desteklenir; birden fazla kaynak varsa çalan otomatik seçilir.
- **İleri / geri sarma:** İlerleme çubuğuna tıklayın ya da sürükleyin; fare tekerleğiyle ±10 saniye.
- **Birden fazla ekran:** Çentik ana ekranda, seçilen bir ekranda ya da tüm ekranlarda gösterilebilir. Servisler tek kez çalışır, her ekran yalnızca hafif bir pencere ekler.
- **Otomatik güncelleme:** GitHub'a her push'tan sonra yeni sürüm derlenir; kurulu uygulamalar saatte bir denetleyip kendini sessizce günceller ve yeniden açılır (kurulum ekranı çıkmaz). Ayarlardan kapatılabilir, "Şimdi denetle" / "Güncelle" ile elle de yapılabilir.
- **Bluetooth:** Eşleştirilmiş cihazlar ve bağlantı durumu. Bir cihaz bağlanınca veya bağlantısı kopunca çentikte kısa bir bildirim çıkar.
- **Bildirimler:** Windows bildirimleri çentikte kısa süre görünür ve listede birikir.
- **Gizleme:** `Ctrl` + `Win` tuşlarına birlikte basıp bırakınca çentik yukarı kayarak gizlenir. Gizliyken fareyi ekranın üst kenarına götürünce aşağı iner. Bildirimler gizliyken de görünür.
- **Sistem tepsisi:** Saatin yanındaki M#Pro ikonuna tıklayınca ayarlar açılır, sağ tıkla menü çıkar (çentiğe sağ tıklamak da aynı menüyü açar).
- **Ayarlar penceresi:** gösterileceği ekran, yazı tipi, çentik boyutu (%80 – %140), bilgisayar açılınca başlat, çentiği gizle, Ctrl + Win kısayolu, tam ekranda gizle, otomatik güncelleme.
- **Teams ve diğer uygulamaların bildirimleri:** Windows bildirimi olarak gönderilen her bildirim okunur. Teams'te **Ayarlar › Bildirimler › Bildirim stili** "Windows" olmalıdır ("Teams yerleşik" seçiliyse bildirimler Windows'a hiç ulaşmaz).
- Tam ekran oyun, video ya da sunumda kendini gizler.

Ayarlar `%AppData%\MSharpPro\settings.json` dosyasında, hatalar `%AppData%\MSharpPro\error.log` dosyasında tutulur.

## Performans
- Boşta yaklaşık **20–35 MB** çalışma belleği, ekolayzer çalışırken yaklaşık **%0,5 CPU** kullanır.
- Tepsi ikonu ve kısayol, WinForms yüklenmeden doğrudan Windows API ile yazıldı. Ayarlar penceresi yalnızca açıkken bellekte durur.

## Geliştirme
```powershell
dotnet build -c Release
.\bin\Release\net10.0-windows10.0.19041.0\MSharpPro.exe
```

### Kurulum dosyasını yerelde üretmek
[Inno Setup 6](https://jrsoftware.org/isdl.php) gerekir (`winget install JRSoftware.InnoSetup`).
```powershell
.\build-installer.ps1
```
Çıktı: `dist\MSharpPro-Setup-<sürüm>.exe`

### Yeni sürüm yayınlamak
`main` dalına push etmeniz yeterli: sürüm numarası otomatik artar (`1.0.<çalıştırma no>`). Büyük bir sürüm atlamak için `MSharpPro.csproj` içindeki `<Version>` değerinin ilk iki hanesini değiştirin (ör. `1.1.0`). README ve `Setup/` değişiklikleri yeni sürüm üretmez.

GitHub Actions (`.github/workflows/release.yml`) uygulamayı derler, kurulum dosyasını üretir, Releases sayfasına ekler ve repodaki `Setup/MSharpPro-Setup.exe` dosyasını da yeni sürümle günceller. Bu güncelleme `main` dalına ayrı bir commit olarak gelir; sonrasında yerelde `git pull` yapın.

## Proje yapısı
```
Assets/                    ikon (app.ico, png'ler, orijinal kaynak)
Services/                  medya, Bluetooth, bildirim, ayarlar, tepsi ikonu, Ctrl+Win kısayolu
MainWindow.xaml(.cs)       çentik arayüzü ve animasyonlar
SettingsWindow.xaml(.cs)   ayarlar penceresi
Setup/MSharpPro-Setup.exe  hazır kurulum dosyası (en güncel sürüm)
installer/MSharpPro.iss    Inno Setup kurulum betiği
build-installer.ps1        yayınla + kurulum dosyası üret
```
