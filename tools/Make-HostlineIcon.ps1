# Make-HostlineIcon.ps1 - Hostline RD logosunu (sekme + cerceve + H + imlec) ICO/PNG olarak uretir.
# Kullanim: powershell -ExecutionPolicy Bypass -File tools\Make-HostlineIcon.ps1
# Cikti: src\RdpManager\Resources\app.ico (uygulama ikonu) ve Branding\Hostline\logo-256.png

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\RdpManager\Branding\Hostline'
New-Item -ItemType Directory -Force $outDir | Out-Null

$black = [System.Drawing.Color]::FromArgb(255, 10, 10, 10)
$white = [System.Drawing.Color]::White
$red   = [System.Drawing.Color]::FromArgb(255, 227, 93, 91)   # #E35D5B, Hostline yumusak kirmizi

# Koordinatlar 100 birimlik kare uzerinde: x1, y1, x2, y2, renk.
$full = @(
    @(0, 0, 100, 100, $black),     # dis kenarlik
    @(3, 3, 97, 97, $white),       # beyaz zemin
    @(12, 16, 48, 28, $black),     # sol sekme
    @(52, 16, 88, 28, $red),       # sag sekme (aktif)
    @(12, 28, 88, 86, $black),     # cerceve
    @(17, 33, 83, 81, $white),     # cerceve ici
    @(30, 41, 37, 73, $black),     # H sol
    @(51, 41, 58, 73, $black),     # H sag
    @(37, 54, 51, 60, $black),     # H orta
    @(63, 65, 71, 73, $red)        # imlec
)
# 16/24 px: cerceve detaylari secilmiyor; siyah kare + H + imlec.
$small = @(
    @(0, 0, 100, 100, $black),
    @(18, 18, 33, 82, $white),
    @(52, 18, 67, 82, $white),
    @(33, 43, 52, 57, $white),
    @(73, 66, 89, 82, $red)
)

function New-Png([int]$size) {
    $shapes = if ($size -le 24) { $small } else { $full }
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'None'   # keskin koseler: piksele hizali dikdortgenler
    foreach ($s in $shapes) {
        $x1 = [math]::Round($s[0] * $size / 100); $y1 = [math]::Round($s[1] * $size / 100)
        $x2 = [math]::Round($s[2] * $size / 100); $y2 = [math]::Round($s[3] * $size / 100)
        if ($x2 -le $x1) { $x2 = $x1 + 1 }; if ($y2 -le $y1) { $y2 = $y1 + 1 }
        $brush = New-Object System.Drawing.SolidBrush $s[4]
        $g.FillRectangle($brush, $x1, $y1, $x2 - $x1, $y2 - $y1)
        $brush.Dispose()
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { ,(New-Png $s) }

# ICO: 6 bayt baslik + 16 bayt/girdi dizin + PNG verileri (Vista+ PNG gomulu ICO destekler).
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path (Split-Path $PSScriptRoot -Parent) 'src\RdpManager\Resources\app.ico'), $ico.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $outDir 'logo-256.png'), $pngs[-1])
Write-Host "Yazildi: Resources\app.ico, $outDir\logo-256.png"
