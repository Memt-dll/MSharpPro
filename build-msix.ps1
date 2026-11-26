# M#Pro: Microsoft Store için MSIX paketi üretir -> dist\MSharpPro-<sürüm>.msix
#
# Partner Center'a yüklenecek paket için Identity değerlerini Partner Center >
# Ürün > Ürün yönetimi > Ürün kimliği sayfasından alın:
#   .\build-msix.ps1 -Version 1.0.4 -IdentityName "12345Memt-dll.MPro" `
#                    -Publisher "CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX" -PublisherDisplayName "Memt-dll"
#
# Yerelde denemek için (Geliştirici Modu açıkken, imzasız):  .\build-msix.ps1 -Register
# Store'a yüklenen paketi Microsoft kendisi imzalar; sertifika gerekmez.

param(
    [string]$Version,
    [string]$IdentityName = "MSharpPro.Dev",
    [string]$Publisher = "CN=MSharpPro-Dev",
    [string]$PublisherDisplayName = "Memt-dll",
    [switch]$Register
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $Version) {
    $Version = ([xml](Get-Content .\MSharpPro.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Sürüm x.y.z biçiminde olmalı: $Version" }
$pkgVersion = "$Version.0"   # Store: dördüncü hane 0 olmalı
Write-Host "Sürüm: $pkgVersion  Kimlik: $IdentityName" -ForegroundColor Cyan

# 1) Yayınla (paket içinde .NET de olsun diye self-contained; tek dosya DEĞİL)
$layout = Join-Path $PSScriptRoot 'obj\msix-layout'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
dotnet publish .\MSharpPro.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:Version=$Version -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız" }
Get-ChildItem $layout -Filter *.pdb | Remove-Item

# 2) Bildirim + görseller
$manifest = Get-Content .\Packaging\AppxManifest.template.xml -Raw -Encoding UTF8
$manifest = $manifest.Replace('{{IdentityName}}', $IdentityName).Replace('{{Publisher}}', $Publisher).
    Replace('{{PublisherDisplayName}}', $PublisherDisplayName).Replace('{{Version}}', $pkgVersion)
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))
New-Item -ItemType Directory -Force (Join-Path $layout 'Images') | Out-Null
Copy-Item .\Packaging\Assets\*.png (Join-Path $layout 'Images') -Exclude 'StoreListingLogo-*'

if ($Register) {
    # Yerel deneme: paketlemeden, klasörden kaydet (Geliştirici Modu gerekir)
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    Write-Host "Yerelde kaydedildi. Başlat menüsünde 'M#Pro' olarak görünür." -ForegroundColor Green
    return
}

# 3) MSIX paketini oluştur
$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe |
    Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName | Select-Object -Last 1
if (-not $makeappx) { throw "makeappx.exe bulunamadı (Windows SDK gerekli)" }

New-Item -ItemType Directory -Force .\dist | Out-Null
$out = Join-Path $PSScriptRoot "dist\MSharpPro-$Version.msix"
& $makeappx.FullName pack /d $layout /p $out /o
if ($LASTEXITCODE -ne 0) { throw "makeappx başarısız" }

Write-Host "Hazır: $out" -ForegroundColor Green
Write-Host "Bu dosyayı Partner Center > Gönderim > Paketler bölümüne yükleyin." -ForegroundColor Green
