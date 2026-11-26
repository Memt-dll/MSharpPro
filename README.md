<p align="center">
  <img src="Assets/icon-1024.png" width="128" alt="M#Pro"/>
</p>

<h1 align="center">M#Pro</h1>

<p align="center">Windows için hafif, "dynamic island" tarzı çentik uygulaması (WPF / .NET 10)</p>

---

## Kurulum
1. [Releases](https://github.com/Memt-dll/MSharpPro/releases) sayfasından en son `MSharpPro-Setup-x.y.z.exe` dosyasını indirin.
2. Çalıştırın. Yönetici izni gerekmez, ayrıca .NET kurmanıza da gerek yoktur.
3. Kurulumda isterseniz "Bilgisayar açılınca M#Pro'yu başlat" ve masaüstü kısayolu seçeneklerini işaretleyin.

Kaldırmak için: **Ayarlar › Uygulamalar › M#Pro › Kaldır**.

## Özellikler
- **Çentik:** Ekranın üst ortasında durur. Çalan şarkının kapağını ve hareketli bir ekolayzer gösterir; okunmamış bildirim varsa küçük bir nokta çıkar.
- **Fareyle üzerine gelince açılır:** Medya / Bluetooth / Bildirimler sekmeleri.
- **Medya:** Kapak resmi, şarkı adı, sanatçı, uygulama, ilerleme çubuğu; önceki / oynat-duraklat / sonraki. Arka planda kapak resminden bulanık bir ortam rengi oluşur.
- **Bluetooth:** Eşleştirilmiş cihazlar ve bağlantı durumu. Bir cihaz bağlanınca veya bağlantısı kopunca çentikte kısa bir bildirim çıkar.
- **Bildirimler:** Windows bildirimleri çentikte kısa süre görünür ve listede birikir.
- **Gizleme:** `Ctrl` + `Win` tuşlarına birlikte basıp bırakınca çentik yukarı kayarak gizlenir. Gizliyken fareyi ekranın üst kenarına götürünce aşağı iner. Bildirimler gizliyken de görünür.
- **Sistem tepsisi:** Saatin yanındaki M#Pro ikonuna tıklayınca ayarlar açılır, sağ tıkla menü çıkar (çentiğe sağ tıklamak da aynı menüyü açar).
- **Ayarlar penceresi:** yazı tipi, çentik boyutu (%80 – %140), bilgisayar açılınca başlat, çentiği gizle, Ctrl + Win kısayolu, tam ekranda gizle.
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

### Yeni sürüm yayınlamak (GitHub Releases)
`MSharpPro.csproj` içindeki `<Version>` değerini artırın, sonra bir etiket push edin:
```powershell
git tag v1.0.0
git push origin v1.0.0
```
GitHub Actions (`.github/workflows/release.yml`) uygulamayı derler, kurulum dosyasını üretir ve Releases sayfasına ekler.

## Proje yapısı
```
Assets/                    ikon (app.ico, png'ler, orijinal kaynak)
Services/                  medya, Bluetooth, bildirim, ayarlar, tepsi ikonu, Ctrl+Win kısayolu
MainWindow.xaml(.cs)       çentik arayüzü ve animasyonlar
SettingsWindow.xaml(.cs)   ayarlar penceresi
installer/MSharpPro.iss    Inno Setup kurulum betiği
build-installer.ps1        yayınla + kurulum dosyası üret
```
