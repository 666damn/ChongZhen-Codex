param(
    [string]$Version = '24.13.1',
    [string]$CacheRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) '.tools\cache\node')
)

$ErrorActionPreference = 'Stop'
$archiveName = "node-v$Version-win-x64.zip"
$baseUri = "https://nodejs.org/dist/v$Version"
$resolvedCache = [IO.Path]::GetFullPath($CacheRoot)
$nodePath = Join-Path $resolvedCache "node-v$Version-win-x64\node.exe"
if (Test-Path -LiteralPath $nodePath -PathType Leaf) {
    $reported = (& $nodePath --version).Trim()
    if ($LASTEXITCODE -eq 0 -and $reported -eq "v$Version") {
        Write-Output $nodePath
        exit 0
    }
}

[IO.Directory]::CreateDirectory($resolvedCache) | Out-Null
$archivePath = Join-Path $resolvedCache $archiveName
$checksumsPath = Join-Path $resolvedCache "SHASUMS256-v$Version.txt"
Invoke-WebRequest -UseBasicParsing -Uri "$baseUri/$archiveName" -OutFile $archivePath
Invoke-WebRequest -UseBasicParsing -Uri "$baseUri/SHASUMS256.txt" -OutFile $checksumsPath
$line = Get-Content -LiteralPath $checksumsPath | Where-Object { $_ -match "^[a-f0-9]{64}\s+$([regex]::Escape($archiveName))$" } | Select-Object -First 1
if (-not $line) { throw "Official Node checksum is missing for $archiveName" }
$expected = ($line -split '\s+')[0].ToUpperInvariant()
$actual = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Node archive SHA-256 mismatch: expected $expected, got $actual" }

$destinationRoot = Split-Path -Parent $nodePath
[IO.Directory]::CreateDirectory($destinationRoot) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entryName = "node-v$Version-win-x64/node.exe"
    $entry = $archive.GetEntry($entryName)
    if ($null -eq $entry) { throw 'Downloaded Node archive is incomplete.' }
    $input = $entry.Open()
    try {
        $output = [IO.File]::Open($nodePath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $input.CopyTo($output); $output.Flush($true) } finally { $output.Dispose() }
    }
    finally { $input.Dispose() }
}
finally { $archive.Dispose() }
$reported = (& $nodePath --version).Trim()
if ($LASTEXITCODE -ne 0 -or $reported -ne "v$Version") { throw "Bundled Node verification failed: $reported" }
Write-Output $nodePath
