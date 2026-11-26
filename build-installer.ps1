# M#Pro: uygulamayı yayınlar ve kurulum dosyasını (dist\MSharpPro-Setup-<sürüm>.exe) üretir.
# Gerekli: .NET 10 SDK ve Inno Setup 6 (https://jrsoftware.org/isdl.php)
#   .\build-installer.ps1              -> sürüm csproj'dan okunur
#   .\build-installer.ps1 -Version 1.2.0

param([string]$Version)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $Version) {
    $Version = ([xml](Get-Content .\MSharpPro.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
Write-Host "Sürüm: $Version" -ForegroundColor Cyan

# 1) Yayınla: .NET kurulu olmayan bilgisayarlarda da çalışsın diye self-contained
if (Test-Path .\publish) { Remove-Item .\publish -Recurse -Force }
dotnet publish .\MSharpPro.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:Version=$Version -o .\publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish başarısız" }

# 2) Inno Setup derleyicisini bul
$onPath = Get-Command iscc -ErrorAction SilentlyContinue
$iscc = @(
    $(if ($onPath) { $onPath.Source }),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 bulunamadı. Kurun: winget install JRSoftware.InnoSetup"
}

# 3) Kurulumu derle
& $iscc "/DAppVersion=$Version" .\installer\MSharpPro.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup derlemesi başarısız" }

Write-Host "Hazır: dist\MSharpPro-Setup-$Version.exe" -ForegroundColor Green
