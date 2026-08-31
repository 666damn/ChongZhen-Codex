param(
    [Parameter(Mandatory = $true)][string]$ProxyPath,
    [Parameter(Mandatory = $true)][string]$AsarPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$expectedHash = (& node (Join-Path $projectRoot 'scripts\asar-header-hash.mjs') $AsarPath).Trim()
if ($LASTEXITCODE -ne 0 -or $expectedHash -notmatch '^[a-f0-9]{64}$') {
    throw 'Unable to calculate the patched ASAR header hash.'
}
if (-not (Test-Path -LiteralPath $ProxyPath)) { throw "Proxy DLL is missing: $ProxyPath" }

$binaryText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($ProxyPath))
if (-not $binaryText.Contains($expectedHash)) {
    throw 'Proxy DLL does not contain the generated ASAR header hash.'
}
if ($binaryText.Contains('ee85b1ae1fecc337e575015993e76e603f8ae291043ca9e9e3ab3a73c4a06ef4')) {
    throw 'Proxy DLL still contains the previous development ASAR header hash.'
}

Write-Output 'PASS: proxy contains only the generated release ASAR integrity hash.'
