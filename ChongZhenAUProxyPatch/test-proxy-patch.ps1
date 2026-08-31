param([Parameter(Mandatory = $true)][string]$ProxyPath)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ProxyPath)) { throw "Proxy DLL is missing: $ProxyPath" }

$binaryBytes = [IO.File]::ReadAllBytes($ProxyPath)
$binaryText = [Text.Encoding]::ASCII.GetString($binaryBytes)
$binaryWideText = [Text.Encoding]::Unicode.GetString($binaryBytes)
if ($binaryText -match '(?i)[a-f0-9]{64}') { throw 'Proxy DLL contains a compile-time ASAR hash.' }
if ($binaryText -match '(?i)[A-Z]:\\Users\\') { throw 'Proxy DLL contains a build-machine user path.' }
if (-not $binaryText.Contains('CZSCAR1') -or -not $binaryWideText.Contains('loader-metadata.bin')) {
    throw 'Proxy DLL is missing dynamic sidecar metadata support.'
}
$loaderTest = Join-Path (Split-Path -Parent $ProxyPath) 'loader_test.exe'
if (-not (Test-Path -LiteralPath $loaderTest)) { throw 'Native loader test executable is missing.' }
& $loaderTest
if ($LASTEXITCODE -ne 0) { throw "Native loader test failed with exit code $LASTEXITCODE." }

Write-Output 'PASS: proxy uses validated dynamic sidecar metadata and contains no fixed ASAR hash or build path.'
