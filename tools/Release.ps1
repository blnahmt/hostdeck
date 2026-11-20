# Release.ps1 - Hostline RD (+ varsa sirket ici surum) release build + Inno Setup installer + latest.json
# Sirket ici surumun ad/adres/ikon bilgisi git'e girmeyen tools/internal.ps1'de; dosya yoksa yalniz Hostline RD uretilir.
#
# Kullanim (imzasiz):
#   powershell -ExecutionPolicy Bypass -File tools\Release.ps1
#
# Kullanim (kod imzali - Smart App Control / WDAC engellemesin diye):
#   ...Release.ps1 -CertThumbprint <sertifika_parmakizi>
#   ...Release.ps1 -PfxPath C:\yol\cert.pfx -PfxPassword <parola>
#
# Sertifika: bir CA'dan (or. Certum, Sectigo, DigiCert) Authenticode kod imzalama sertifikasi.
# Bulut/akilli kart sertifikalari Windows sertifika deposunda gorunur: -CertThumbprint ile kullanin.
# SmartScreen itibari imzali indirmeler arttikca olusur; ilk surumlerde uyari bir sure surebilir.

param(
    [string]$CertThumbprint,
    [string]$PfxPath,
    [string]$PfxPassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    # Dis surum (Hostline RD) indirme klasoru. Bossa GitHub release'i: .../releases/download/v<surum>
    [string]$PublicUpdateBase = '',
    # Yalniz bir surum: Internal veya Public. Bossa ikisi de.
    [ValidateSet('', 'Internal', 'Public')][string]$Only = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path $PSScriptRoot -Parent
$csproj     = Join-Path $repoRoot 'src\RdpManager\RdpManager.csproj'
$releaseDir = Join-Path $repoRoot 'release'
$issFile    = Join-Path $PSScriptRoot 'installer.iss'
$iconFile   = Join-Path $repoRoot 'src\RdpManager\Resources\app.ico'

# Sirket ici surum tanimi ($InternalEdition, $InternalLeakPatterns) - varsa.
$InternalEdition = $null
$InternalLeakPatterns = @()
$internalConfig = Join-Path $PSScriptRoot 'internal.ps1'

# Inno Setup compiler
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
    $iscc = 'C:\Program Files\Inno Setup 6\ISCC.exe'
}
if (-not (Test-Path $iscc)) {
    Write-Error 'Inno Setup 6 bulunamadi. https://jrsoftware.org/isinfo.php adresinden yukleyin.'
}

# --- Kod imzalama ayari ---
$signingEnabled = ($CertThumbprint) -or ($PfxPath)
$signtool = $null
if ($signingEnabled) {
    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) {
        Write-Error 'signtool.exe bulunamadi. Windows 10/11 SDK yukleyin.'
    }
}

function Invoke-Sign {
    param([string[]]$Files)
    if (-not $signingEnabled) { return }

    $common = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256')
    if ($CertThumbprint) {
        $args = $common + @('/sha1', $CertThumbprint) + $Files
    } else {
        $args = $common + @('/f', $PfxPath, '/p', $PfxPassword) + $Files
    }
    & $signtool @args
    if ($LASTEXITCODE -ne 0) { Write-Error 'Imzalama basarisiz.' }
}

if ($signingEnabled) {
    Write-Host 'Kod imzalama: ETKIN' -ForegroundColor Green
} else {
    Write-Host 'Kod imzalama: kapali (imzasiz surum). Smart App Control/WDAC olan makinelerde engellenebilir.' -ForegroundColor DarkYellow
}

# --- 1. Surum bilgisini csproj dan oku ---
[xml]$proj = Get-Content $csproj -Encoding UTF8
# Birden fazla PropertyGroup var (Edition=Public); StrictMode'da .Version uyesi patlar.
$version = $proj.SelectSingleNode('/Project/PropertyGroup/Version').InnerText

if (-not $version) {
    Write-Error 'csproj icinde <Version> bulunamadi.'
}

Write-Host "Surum: $version" -ForegroundColor Cyan

# --- 2. Surumler: ayni kaynak, farkli Edition ---
if (Test-Path $internalConfig) { . $internalConfig }
$editions = @()
if ($InternalEdition) { $editions += $InternalEdition }
$editions += @(
    [ordered]@{
        Edition   = 'Public'
        Name      = 'Hostline RD'
        SetupBase = 'HostlineRD'   # GitHub dosya adindaki boslugu noktaya ceviriyor; bosluksuz ad
        Publisher = 'Hostline'
        AppGuid   = 'A74335E6-F072-4270-B846-B481BB4A8C87'
        Scheme    = 'hostline'
        OutDir    = (Join-Path $releaseDir 'public')
        BaseUrl   = $(if ($PublicUpdateBase) { $PublicUpdateBase } else { "https://github.com/blnahmt/hostline/releases/download/v$version" })
        GitHub    = $true
        Brand     = $true   # kurulum sihirbazinda Hostline gorselleri
        Icon      = $iconFile
    }
)

$summary = @()

foreach ($e in $editions) {
    if ($Only -and $Only -ne $e.Edition) { continue }

    Write-Host ''
    Write-Host "=== $($e.Edition): $($e.Name) ===" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $e.OutDir | Out-Null

    # Iki surum ayni obj klasorunu paylasir; WPF'in uretilen dosyalarinda assembly adi
    # gomulu oldugu icin onceki surumden kalinti olmasin diye Release ara ciktisini sil.
    Get-ChildItem (Join-Path $repoRoot 'src\RdpManager\obj') -Directory -Recurse -Filter Release -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force

    $publishDir = Join-Path $releaseDir 'publish'
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host 'Release build aliniyor...' -ForegroundColor Yellow
    # self-contained: .NET calisma ortami kurulumla gelir; kullanici bir sey kurmaz, yonetici izni gerekmez.
    dotnet publish $csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:Edition=$($e.Edition) `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) { Write-Error "Build basarisiz ($($e.Edition))." }

    # Dis surumde sirket ici bilgi kalmamali (desenler tools/internal.ps1'den).
    if ($e.Edition -eq 'Public' -and $InternalLeakPatterns.Count -gt 0) {
        $leak = Select-String -Path (Join-Path $publishDir '*.dll'), (Join-Path $publishDir '*.exe') `
            -Pattern $InternalLeakPatterns -SimpleMatch -Encoding Unicode -List -ErrorAction SilentlyContinue
        if ($leak) { Write-Error "Dis surumde sirket ici bilgi bulundu: $($leak.Path -join ', ')" }
    }

    # Yayin ikili dosyalarini imzala (installer'a girmeden ONCE).
    # Kendi exe/dll'lerimizin yani sira ucuncu taraf native e_sqlite3.dll'i de
    # imzaliyoruz; boylece WDAC dosya bazli engellemez.
    if ($signingEnabled) {
        Write-Host 'Yayin dosyalari imzalaniyor...' -ForegroundColor Yellow
        $toSign = Get-ChildItem $publishDir -Recurse -Include *.exe, *.dll |
            Select-Object -ExpandProperty FullName
        if ($toSign) { Invoke-Sign -Files $toSign }
    }

    # --- 3. Inno Setup ile installer olustur ---
    Write-Host 'Installer olusturuluyor...' -ForegroundColor Yellow

    $setupBase = if ($e.Contains('SetupBase')) { $e.SetupBase } else { $e.Name }
    $setupName = "${setupBase}_${version}_x64-setup"

    & $iscc $issFile `
        "/DAppVersion=$version" `
        "/DAppName=$($e.Name)" `
        "/DAppPublisher=$($e.Publisher)" `
        "/DAppGuid=$($e.AppGuid)" `
        "/DUriScheme=$($e.Scheme)" `
        "/DPublishDir=$publishDir" `
        "/DOutputDir=$($e.OutDir)" `
        "/DOutputFilename=$setupName" `
        "/DIconFile=$($e.Icon)" `
        "/D$(if ($e.Contains('Brand')) { 'HostlineBrand' } else { 'NoBrand' })=1"

    if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup basarisiz ($($e.Edition))." }

    $setupExe = Join-Path $e.OutDir "$setupName.exe"

    # Installer'in kendisini de imzala.
    if ($signingEnabled) {
        Write-Host 'Installer imzalaniyor...' -ForegroundColor Yellow
        Invoke-Sign -Files @($setupExe)
    }

    Write-Host "Installer: $setupExe" -ForegroundColor Green

    # --- 4. latest.json olustur (guncelleme sunucusu tanimliysa) ---
    $latestPath = $null
    if ($e.BaseUrl) {
        $encodedName = [Uri]::EscapeDataString("$setupName.exe")

        $latest = [ordered]@{
            version   = $version
            notes     = 'New version.'
            pub_date  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            platforms = [ordered]@{
                'windows-x86_64' = [ordered]@{
                    url = "$($e.BaseUrl)/$encodedName"
                }
            }
        }

        $latestPath = Join-Path $e.OutDir 'latest.json'
        $latest | ConvertTo-Json -Depth 4 | Out-File $latestPath -Encoding UTF8
        Write-Host "latest.json: $latestPath" -ForegroundColor Green
    } else {
        Write-Host 'Guncelleme sunucusu tanimli degil, latest.json uretilmedi.' -ForegroundColor DarkYellow
    }

    $summary += [pscustomobject]@{ Edition = $e.Edition; Setup = $setupExe; Latest = $latestPath; BaseUrl = $e.BaseUrl; Name = "$setupName.exe"; GitHub = $e.Contains('GitHub') }

    # --- 5. Temizlik ---
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}

# --- 6. Ozet ---
Write-Host ''
Write-Host '=== Release tamamlandi ===' -ForegroundColor Cyan
Write-Host "  Surum: $version"
foreach ($s in $summary) {
    Write-Host ''
    Write-Host "  [$($s.Edition)] $($s.Setup)"
    if ($s.Latest) {
        if ($s.GitHub) {
            # Yayinlamak disa donuk bir adim: komut yalniz yazdirilir, calistirilmaz.
            Write-Host "    GitHub'da yayinlamak icin:" -ForegroundColor Yellow
            Write-Host "    gh release create v$version `"$($s.Setup)`" `"$($s.Latest)`" --repo blnahmt/hostline --title `"Hostline RD $version`" --generate-notes"
        } else {
            Write-Host "    Sunucuya yukle: $($s.Name) ve latest.json  ->  $($s.BaseUrl)/" -ForegroundColor Yellow
        }
    }
}
