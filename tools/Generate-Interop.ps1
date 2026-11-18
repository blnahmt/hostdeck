<#
.SYNOPSIS
    mstscax.dll icin AxMSTSCLib.dll ve MSTSCLib.dll interop assembly'lerini uretir.

.DESCRIPTION
    RDP ActiveX kontrolu (Microsoft Terminal Services Client Control) icin resmi bir
    NuGet paketi YOKTUR. Interop DLL'leri makinede bir kez uretilip repoya commit edilir.
    1Remote ve mRemoteNG de aynisini yapiyor.

    Gereksinim: AxImp.exe (.NET Framework SDK araci).
    Yoksa: Visual Studio Installer -> Tekil bilesenler -> ".NET Framework 4.8 SDK"

    NOT: Bu dosya bilerek saf ASCII tutuluyor. Windows PowerShell 5.1 BOM'suz
    UTF-8 dosyalari ANSI olarak okur ve Turkce karakterler parse hatasi yapar.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Generate-Interop.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot param blogunda her zaman dolu gelmiyor; burada cozuyoruz.
if (-not $OutputDirectory) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
    $OutputDirectory = Join-Path $scriptDir '..\lib'
}

function Find-AxImp {
    $roots = @(
        'C:\Program Files (x86)\Microsoft SDKs\Windows',
        'C:\Program Files (x86)\Windows Kits\10\bin',
        'C:\Program Files\Microsoft Visual Studio',
        'C:\Program Files (x86)\Microsoft Visual Studio'
    ) | Where-Object { Test-Path $_ }

    foreach ($root in $roots) {
        $hit = Get-ChildItem -Path $root -Filter 'AxImp.exe' -Recurse -ErrorAction SilentlyContinue |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

$source = Join-Path $env:SystemRoot 'System32\mstscax.dll'
if (-not (Test-Path $source)) {
    throw "mstscax.dll bulunamadi: $source"
}

$axImp = Find-AxImp
if (-not $axImp) {
    Write-Host ''
    Write-Host 'AxImp.exe bulunamadi.' -ForegroundColor Red
    Write-Host 'Kurulum: Visual Studio Installer -> Degistir -> Tekil bilesenler ->' -ForegroundColor Yellow
    Write-Host '         ".NET Framework 4.8 SDK" isaretleyin.' -ForegroundColor Yellow
    Write-Host ''
    throw 'AxImp.exe gerekli.'
}

Write-Host "AxImp   : $axImp"
Write-Host "Kaynak  : $source"

$OutputDirectory = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
Write-Host "Cikti   : $OutputDirectory"
Write-Host ''

# AxImp, tip kutuphanesi interop'unu (MSTSCLib.dll) CALISMA DIZININE yazar,
# /out ise sadece ActiveX sarmalayicisini (AxMSTSCLib.dll) adlandirir.
Push-Location $OutputDirectory
try {
    & $axImp $source /out:AxMSTSCLib.dll
    if ($LASTEXITCODE -ne 0) { throw "AxImp basarisiz oldu (exit $LASTEXITCODE)." }
}
finally {
    Pop-Location
}

Write-Host ''
$missing = $false
foreach ($name in 'AxMSTSCLib.dll', 'MSTSCLib.dll') {
    $path = Join-Path $OutputDirectory $name
    if (Test-Path $path) {
        $size = (Get-Item $path).Length
        Write-Host "  OK     $name ($size bayt)" -ForegroundColor Green
    } else {
        Write-Host "  EKSIK  $name" -ForegroundColor Red
        $missing = $true
    }
}

if ($missing) { throw 'Interop uretimi eksik tamamlandi.' }

Write-Host ''
Write-Host 'Tamam. Simdi: dotnet build RdpManager.sln -c Debug' -ForegroundColor Green
