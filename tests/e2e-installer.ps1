param(
    [Parameter(Mandatory)][string]$Installer,
    [string]$GameRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedInstaller = (Resolve-Path -LiteralPath $Installer).Path
& (Join-Path $projectRoot 'scripts\assert-release-clean.ps1') -Installer $resolvedInstaller
& (Join-Path $projectRoot 'tests\release-payload.test.ps1')

$process = Start-Process -FilePath $resolvedInstaller -PassThru
try {
    for ($index = 0; $index -lt 50 -and $process.MainWindowHandle -eq 0 -and -not $process.HasExited; $index++) {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    }
    if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw 'Installer UI did not open.' }
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}

$baseline = Join-Path $projectRoot 'build\baseline.asar'
$patched = Join-Path $projectRoot 'build\app.asar'
$proxy = Join-Path $projectRoot 'ChongZhenAUProxyPatch\build\version.dll'
& node (Join-Path $projectRoot 'scripts\verify-asar.mjs') $patched | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Patched ASAR verification failed.' }
& node (Join-Path $projectRoot 'scripts\verify-asar-delta.mjs') $baseline $patched | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Patched ASAR delta verification failed.' }
& (Join-Path $projectRoot 'ChongZhenAUProxyPatch\test-proxy-exports.ps1') -ProxyPath $proxy | Out-Null
& (Join-Path $projectRoot 'ChongZhenAUProxyPatch\test-proxy-patch.ps1') -ProxyPath $proxy -AsarPath $patched | Out-Null

if ($GameRoot) {
    $resolvedGame = (Resolve-Path -LiteralPath $GameRoot).Path
    $gameExe = Join-Path $resolvedGame 'ChongZhenSimulator.exe'
    if ((Get-FileHash -LiteralPath $gameExe -Algorithm SHA256).Hash -ne '68F05EDBF6E1E48F1CDCDA30E53BD6E37419A3584B684632DBFE223B1B39D785') {
        throw 'Installed game EXE differs from the supported untouched baseline.'
    }
}
Write-Output 'PASS: final EXE, embedded payload, UI startup, ASAR delta, proxy exports, and optional game EXE baseline verified.'
