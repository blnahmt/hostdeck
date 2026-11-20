# Make-InstallerImages.ps1 - Hostdeck kurulum sihirbazi gorsellerini uretir (acik + koyu, 3 olcek).
# Kullanim: powershell -ExecutionPolicy Bypass -File tools\Make-InstallerImages.ps1
# Cikti: tools\installer\wizard-{light|dark}-{100|150|200}.png ve small-{100|150|200}.png
# Kaynak: src\RdpManager\Branding\Hostdeck\logo-256.png ve Quicksand fontu.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo   = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $PSScriptRoot 'installer'
New-Item -ItemType Directory -Force $outDir | Out-Null

$logo  = [Drawing.Image]::FromFile((Join-Path $repo 'src\RdpManager\Branding\Hostdeck\logo-256.png'))
$fonts = New-Object Drawing.Text.PrivateFontCollection
$fonts.AddFontFile((Join-Path $repo 'src\RdpManager\Resources\Fonts\Quicksand-Bold.ttf'))
$fonts.AddFontFile((Join-Path $repo 'src\RdpManager\Resources\Fonts\Quicksand-Medium.ttf'))
$family = $fonts.Families[0]

$themes = @{
    light = @{ Back = '#FFFFFF'; Text = '#0A0A0A'; Muted = '#5C5C5C'; Accent = '#E35D5B' }
    dark  = @{ Back = '#0A0A0A'; Text = '#F5F5F5'; Muted = '#A3A3A3'; Accent = '#EC6B69' }
}
function C([string]$hex) { [Drawing.ColorTranslator]::FromHtml($hex) }

# Sondaki boslugu da olcen bicim (iki parcali yazi bosluklu olursa bitisik cikmasin).
$fmt = [Drawing.StringFormat]::GenericTypographic.Clone()
$fmt.FormatFlags = $fmt.FormatFlags -bor [Drawing.StringFormatFlags]::MeasureTrailingSpaces

function New-Canvas([int]$w, [int]$h, [string]$back) {
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.Clear((C $back))
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.InterpolationMode = 'NearestNeighbor'   # logo duz dikdortgenlerden olusuyor: keskin kalsin
    $g.PixelOffsetMode = 'Half'
    return @($bmp, $g)
}

# Sol buyuk gorsel (modern sihirbaz, %100 = 202x386).
foreach ($name in $themes.Keys) {
    $t = $themes[$name]
    foreach ($scale in 100, 150, 200) {
        $k = $scale / 100
        $w = [int](202 * $k); $h = [int](386 * $k)
        $bmp, $g = New-Canvas $w $h $t.Back

        $size = [int](96 * $k)
        $g.DrawImage($logo, [int](($w - $size) / 2), [int](104 * $k), $size, $size)

        $font = New-Object Drawing.Font $family, ([single](22 * $k)), ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
        $part1 = 'Host'; $part2 = 'deck'
        $w1 = $g.MeasureString($part1, $font, [Drawing.PointF]::Empty, $fmt).Width
        $w2 = $g.MeasureString($part2, $font, [Drawing.PointF]::Empty, $fmt).Width
        $x = ($w - $w1 - $w2) / 2; $y = 220 * $k
        $g.DrawString($part1, $font, (New-Object Drawing.SolidBrush (C $t.Text)), $x, $y, $fmt)
        $g.DrawString($part2, $font, (New-Object Drawing.SolidBrush (C $t.Accent)), $x + $w1, $y, $fmt)

        $sub = New-Object Drawing.Font $family, ([single](10.5 * $k)), ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
        $dot = [string][char]0x00B7   # orta nokta: dosya ASCII kalsin (PS 5.1 BOM'suz UTF-8'i bozuk okur)
        $line = "RDP  $dot  VNC  $dot  SSH"
        $sw = $g.MeasureString($line, $sub, [Drawing.PointF]::Empty, $fmt).Width
        $g.DrawString($line, $sub, (New-Object Drawing.SolidBrush (C $t.Muted)), ($w - $sw) / 2, 254 * $k, $fmt)

        # Alt vurgu cizgisi (koseli, ince).
        $g.FillRectangle((New-Object Drawing.SolidBrush (C $t.Accent)), [int](($w - 40 * $k) / 2), [int](330 * $k), [int](40 * $k), [Math]::Max(2, [int](3 * $k)))

        $g.Dispose()
        $bmp.Save((Join-Path $outDir "wizard-$name-$scale.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    }
}

# Sag ust kucuk gorsel (%100 = 58x58): logonun kendisi, iki temada da okunur (beyaz karo + siyah kenar).
foreach ($scale in 100, 150, 200) {
    $s = [int](58 * $scale / 100)
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp); $g.Clear([Drawing.Color]::Transparent)
    $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
    $g.DrawImage($logo, 0, 0, $s, $s); $g.Dispose()
    $bmp.Save((Join-Path $outDir "small-$scale.png"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

$logo.Dispose()
Get-ChildItem $outDir | ForEach-Object { "{0,-22} {1,7} bayt" -f $_.Name, $_.Length }
