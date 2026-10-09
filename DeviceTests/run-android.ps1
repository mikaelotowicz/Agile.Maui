# Roda os device tests no emulador Android: builda, instala, inicia a activity e
# faz poll do logcat (tag DEVICETEST) até o [DEVICETEST-SUMMARY] ou timeout.
# Exit code 0 = suíte verde (fail=0); 1 = falhas ou timeout.
#
# Uso: .\run-android.ps1 [-Configuration Debug] [-Avd pixel_7_-_api_36_0] [-DeviceSerial emulator-5554]
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [string]$Avd = 'pixel_7_-_api_36_0',
    [string]$DeviceSerial = '',
    [int]$BootTimeoutSeconds = 300,
    [int]$RunTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'

$sdk = 'C:\Program Files (x86)\Android\android-sdk'
$adb = Join-Path $sdk 'platform-tools\adb.exe'
$emulator = Join-Path $sdk 'emulator\emulator.exe'
if (-not (Test-Path $adb)) { throw "adb não encontrado em $adb (ajuste `$sdk no script)" }

$appId = 'com.agile.maui.devicetests'
$activity = "$appId/$appId.MainActivity"
$proj = Join-Path $PSScriptRoot 'DeviceTests.csproj'

function Get-EmulatorSerial {
    $out = & $adb devices 2>$null
    foreach ($line in $out) {
        if ($line -match '^(emulator-\d+)\s+device$') { return $Matches[1] }
    }
    return $null
}

# ── 1. Garante o emulador rodando e bootado ─────────────────────────────────────
$serial = $DeviceSerial
if (-not $serial) { $serial = Get-EmulatorSerial }
if (-not $serial) {
    Write-Host "Nenhum emulador online — iniciando AVD '$Avd' (boot pode levar minutos)…"
    Start-Process -FilePath $emulator -ArgumentList @('-avd', $Avd, '-no-snapshot-save', '-no-boot-anim') | Out-Null
    $deadline = (Get-Date).AddSeconds($BootTimeoutSeconds)
    while (-not $serial) {
        if ((Get-Date) -gt $deadline) { throw "timeout esperando o emulador aparecer no adb" }
        Start-Sleep -Seconds 3
        $serial = Get-EmulatorSerial
    }
}

Write-Host "Emulador: $serial — esperando sys.boot_completed=1…"
$deadline = (Get-Date).AddSeconds($BootTimeoutSeconds)
while ($true) {
    $boot = (& $adb -s $serial shell getprop sys.boot_completed 2>$null | Out-String).Trim()
    if ($boot -eq '1') { break }
    if ((Get-Date) -gt $deadline) { throw "timeout esperando boot do emulador $serial" }
    Start-Sleep -Seconds 3
}
Write-Host "Boot concluído."

# ── 2. Build + install (retry para locks transitórios de NuGet/MSBuild) ────────
$installed = $false
for ($attempt = 1; $attempt -le 2 -and -not $installed; $attempt++) {
    Write-Host "== Build + Install (net10.0-android, $Configuration) — tentativa $attempt =="
    & dotnet build $proj -f net10.0-android -c $Configuration -t:Install -p:AdbTarget="-s $serial"
    if ($LASTEXITCODE -eq 0) { $installed = $true }
    elseif ($attempt -lt 2) { Write-Warning "build/install falhou; retry em 10s…"; Start-Sleep -Seconds 10 }
}
if (-not $installed) { Write-Error "build/install falhou"; exit 1 }

# ── 3. Limpa logcat e inicia a activity ─────────────────────────────────────────
& $adb -s $serial shell am force-stop $appId | Out-Null
& $adb -s $serial logcat -c
Write-Host "Iniciando $activity…"
& $adb -s $serial shell am start -n $activity | Out-Null

# ── 4. Poll do logcat até o SUMMARY ou timeout ──────────────────────────────────
$summary = $null
$deadline = (Get-Date).AddSeconds($RunTimeoutSeconds)
while (-not $summary) {
    if ((Get-Date) -gt $deadline) { break }
    Start-Sleep -Seconds 3
    $log = & $adb -s $serial logcat -d -s DEVICETEST:I 2>$null | Out-String
    if ($log -match '\[DEVICETEST-SUMMARY\]') { $summary = $log }
}

if (-not $summary) {
    Write-Error "timeout ($RunTimeoutSeconds s) sem [DEVICETEST-SUMMARY] no logcat — o app pode ter crashado. Últimas linhas:"
    & $adb -s $serial logcat -d -t 80
    exit 1
}

Write-Host ''
Write-Host '===== Resultado (logcat, tag DEVICETEST) ====='
$lines = $summary -split "`r?`n" | Where-Object { $_ -match '\[DEVICETEST' } |
    ForEach-Object { ($_ -replace '^.*?(\[DEVICETEST)', '$1') }
$lines | ForEach-Object { Write-Host $_ }

$summaryLine = $lines | Where-Object { $_ -match '^\[DEVICETEST-SUMMARY\]' } | Select-Object -Last 1
if ($summaryLine -match 'fail=(\d+)') {
    $fail = [int]$Matches[1]
    if ($fail -eq 0) { Write-Host "`nSuíte VERDE no emulador Android." -ForegroundColor Green; exit 0 }
    Write-Host "`n$fail teste(s) FALHARAM no emulador Android." -ForegroundColor Red
    exit 1
}
Write-Error 'sumário ilegível'
exit 1
