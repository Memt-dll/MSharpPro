# M#Pro ikonlarını üretir: Assets\app.ico, icon-64.png, icon-1024.png ve Packaging\Assets\*.png
# Logo: yuvarlak köşeli koyu kare üzerinde ince, yuvarlak uçlu "M#" çizgileri.
# Kullanım:  powershell -ExecutionPolicy Bypass -File .\Assets\make-icons.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent

$Ink   = [System.Drawing.Color]::FromArgb(255, 0x22, 0x26, 0x22)   # koyu zemin
$Paper = [System.Drawing.Color]::FromArgb(255, 0xF2, 0xF1, 0xEB)   # açık zemin
$White = [System.Drawing.Color]::White

# 1024'lük kutuda çizgiler (x1,y1,x2,y2); M ayrı bir çoklu çizgi
$M = @(@(118, 761), @(118, 261), @(352, 599), @(583, 261), @(583, 761))
$Hash = @(
    @(731, 352, 691, 761), @(851, 352, 813, 761),
    @(653, 484, 902, 484), @(639, 628, 888, 628))
$Stroke = 33.0

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# Glif: (ox,oy) köşesinden başlayan size x size kare içine çizilir
function Draw-Glyph($g, [float]$ox, [float]$oy, [float]$size, $color) {
    $s = $size / 1024.0
    # Küçük boyutlarda çizgiyi kalınlaştır, glifi biraz büyüt (okunurluk)
    $small = [Math]::Max(0.0, [Math]::Min(1.0, (128.0 - $size) / 112.0))
    $w = $Stroke * $s + 1.2 * $small
    $grow = 1.0 + 0.10 * $small
    $cx = $ox + $size / 2; $cy = $oy + $size / 2
    $map = { param($x, $y) New-Object System.Drawing.PointF(($cx + ($x - 512) * $s * $grow), ($cy + ($y - 512) * $s * $grow)) }

    $pen = New-Object System.Drawing.Pen($color, [float]$w)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $pts = [System.Drawing.PointF[]]($M | ForEach-Object { & $map $_[0] $_[1] })
    $g.DrawLines($pen, $pts)
    foreach ($l in $Hash) { $g.DrawLine($pen, (& $map $l[0] $l[1]), (& $map $l[2] $l[3])) }
    $pen.Dispose()
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    @($bmp, $g)
}

# Yuvarlak köşeli kare ikon
function New-TileIcon([int]$size, $bg = $Ink, $fg = $White) {
    $bmp, $g = New-Canvas $size $size
    $path = New-RoundRect 0 0 $size $size ([float]($size * 0.225))
    $brush = New-Object System.Drawing.SolidBrush($bg)
    $g.FillPath($brush, $path)
    Draw-Glyph $g 0 0 $size $fg
    $brush.Dispose(); $path.Dispose(); $g.Dispose()
    $bmp
}

function Save-Png($bmp, [string]$file) {
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "  $file"
}

function Get-PngBytes($bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $ms.ToArray()
}

Write-Host "İkonlar üretiliyor..." -ForegroundColor Cyan

# --- Uygulama ikonları
$b = New-TileIcon 1024; Save-Png $b "$root\Assets\icon-1024.png"; $b.Dispose()
$b = New-TileIcon 64;   Save-Png $b "$root\Assets\icon-64.png";   $b.Dispose()

# --- Çok boyutlu .ico (PNG gömülü girdiler)
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { $bm = New-TileIcon $sz; , (Get-PngBytes $bm); $bm.Dispose() }
$ico = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter($ico)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($sz % 256)); $bw.Write([byte]($sz % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write([byte[]]$img) }
$bw.Flush()
[IO.File]::WriteAllBytes("$root\Assets\app.ico", $ico.ToArray())
Write-Host "  $root\Assets\app.ico"

# --- Microsoft Store görselleri
$pk = "$root\Packaging\Assets"
foreach ($pair in @(@('Square150x150Logo.png', 150), @('Square44x44Logo.png', 44),
                    @('Square44x44Logo.targetsize-44_altform-unplated.png', 44),
                    @('StoreLogo.png', 50), @('StoreListingLogo-300.png', 300))) {
    $b = New-TileIcon $pair[1]; Save-Png $b "$pk\$($pair[0])"; $b.Dispose()
}
# Geniş döşeme: düz koyu zemin, ortada glif
$b, $g = New-Canvas 310 150
$g.Clear($Ink)
Draw-Glyph $g 80 0 150 $White
$g.Dispose(); Save-Png $b "$pk\Wide310x150Logo.png"; $b.Dispose()

Write-Host "Tamam." -ForegroundColor Green
