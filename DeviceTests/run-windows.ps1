# Roda os device tests no Windows local (app WinUI unpackaged): builda, executa o exe,
# espera %TEMP%\agile-devicetests-result.txt e imprime o resultado.
# Exit code 0 = suíte verde (fail=0); 1 = falhas ou timeout.
#
# Uso: .\run-windows.ps1 [-Configuration Debug]
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [int]$RunTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'

$tfm = 'net10.0-windows10.0.19041.0'
$proj = Join-Path $PSScriptRoot 'DeviceTests.csproj'
$resultFile = Join-Path $env:TEMP 'agile-devicetests-result.txt'

# ── 1. Build (retry para locks transitórios de NuGet/MSBuild) ───────────────────
$built = $false
for ($attempt = 1; $attempt -le 2 -and -not $built; $attempt++) {
    Write-Host "== Build ($tfm, $Configuration) — tentativa $attempt =="
    & dotnet build $proj -f $tfm -c $Configuration
    if ($LASTEXITCODE -eq 0) { $built = $true }
    elseif ($attempt -lt 2) { Write-Warning "build falhou; retry em 10s…"; Start-Sleep -Seconds 10 }
}
if (-not $built) { Write-Error 'build falhou'; exit 1 }

$exe = Join-Path $PSScriptRoot "bin\$Configuration\$tfm\win-x64\Agile.Maui.DeviceTests.exe"
if (-not (Test-Path $exe)) { Write-Error "exe não encontrado: $exe"; exit 1 }

# ── 2. Executa e espera o arquivo de resultado ──────────────────────────────────
Remove-Item $resultFile -ErrorAction SilentlyContinue
Write-Host "Iniciando $exe…"
$proc = Start-Process -FilePath $exe -PassThru

$deadline = (Get-Date).AddSeconds($RunTimeoutSeconds)
while (-not (Test-Path $resultFile)) {
    if ((Get-Date) -gt $deadline) {
        if (-not $proc.HasExited) { $proc | Stop-Process -Force }
        Write-Error "timeout ($RunTimeoutSeconds s) sem arquivo de resultado — o app pode ter crashado."
        exit 1
    }
    if ($proc.HasExited -and -not (Test-Path $resultFile)) {
        Start-Sleep -Seconds 2   # margem para flush
        if (-not (Test-Path $resultFile)) {
            Write-Error "o app encerrou (exit $($proc.ExitCode)) sem gravar o arquivo de resultado."
            exit 1
        }
    }
    Start-Sleep -Seconds 1
}

Start-Sleep -Seconds 1   # margem para o app terminar de escrever/fechar

Write-Host ''
Write-Host "===== Resultado ($resultFile) ====="
$lines = Get-Content $resultFile
$lines | ForEach-Object { Write-Host $_ }

if (-not $proc.HasExited) {
    # O app se encerra sozinho (AGILE_DEVICETESTS_KEEP_OPEN=1 mantém a janela aberta).
    $null = $proc.WaitForExit(10000)
    if (-not $proc.HasExited) { $proc | Stop-Process -Force }
}

$summaryLine = $lines | Where-Object { $_ -match '^\[DEVICETEST-SUMMARY\]' } | Select-Object -Last 1
if ($summaryLine -match 'fail=(\d+)') {
    $fail = [int]$Matches[1]
    if ($fail -eq 0) { Write-Host "`nSuíte VERDE no Windows." -ForegroundColor Green; exit 0 }
    Write-Host "`n$fail teste(s) FALHARAM no Windows." -ForegroundColor Red
    exit 1
}
Write-Error 'sumário ilegível'
exit 1
