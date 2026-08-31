param(
    [string]$Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'build\release\payload'),
    [string]$NodePath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'build\release'))
$resolvedOutput = [IO.Path]::GetFullPath($Output)
if (-not $resolvedOutput.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Payload staging must stay under $releaseRoot"
}
if (-not $NodePath) { $NodePath = & (Join-Path $PSScriptRoot 'fetch-node.ps1') }
$NodePath = [IO.Path]::GetFullPath(($NodePath | Select-Object -Last 1))
if ((& $NodePath --version).Trim() -ne 'v24.13.1') { throw 'Node 24.13.1 x64 is required.' }

$baseline = Join-Path $projectRoot 'build\baseline.asar'
$patchedAsar = Join-Path $projectRoot 'build\app.asar'
$proxy = Join-Path $projectRoot 'ChongZhenAUProxyPatch\build\version.dll'
foreach ($required in @($baseline, $patchedAsar, $proxy)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Validated build input is missing: $required" }
}
$baselineHash = (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash
if ($baselineHash -ne 'FD24B1C407C2BF18E2B570CC443C4CF50CE98B81C002A5192BE948692014B4A6') {
    throw 'Baseline ASAR is not the supported game version.'
}
$patchedHeader = (& node (Join-Path $projectRoot 'scripts\asar-header-hash.mjs') $patchedAsar).Trim()
if ($LASTEXITCODE -ne 0 -or $patchedHeader -notmatch '^[a-f0-9]{64}$') { throw 'Patched ASAR header verification failed.' }
& node (Join-Path $projectRoot 'scripts\verify-asar.mjs') $patchedAsar | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Patched ASAR content verification failed.' }
& node (Join-Path $projectRoot 'scripts\verify-asar-delta.mjs') $baseline $patchedAsar | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Patched ASAR delta verification failed.' }
& (Join-Path $projectRoot 'ChongZhenAUProxyPatch\test-proxy-exports.ps1') -ProxyPath $proxy | Out-Null
& (Join-Path $projectRoot 'ChongZhenAUProxyPatch\test-proxy-patch.ps1') -ProxyPath $proxy -AsarPath $patchedAsar | Out-Null

if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Recurse -Force }
[IO.Directory]::CreateDirectory((Join-Path $resolvedOutput 'bridge\src')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $resolvedOutput 'bridge\tools')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $resolvedOutput 'global')) | Out-Null
Copy-Item -LiteralPath $NodePath -Destination (Join-Path $resolvedOutput 'bridge\node.exe')
Copy-Item -LiteralPath (Join-Path $projectRoot 'bridge\package.json') -Destination (Join-Path $resolvedOutput 'bridge\package.json')
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'bridge\src') -File -Filter '*.js' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $resolvedOutput "bridge\src\$($_.Name)")
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\configure-game-byok.mjs') -Destination (Join-Path $resolvedOutput 'bridge\tools\configure-game-byok.mjs')
Copy-Item -LiteralPath $patchedAsar -Destination (Join-Path $resolvedOutput 'global\app.asar')
Copy-Item -LiteralPath $proxy -Destination (Join-Path $resolvedOutput 'global\version.dll')

$releaseIdPath = Join-Path $projectRoot 'build\release-token.txt'
if (-not (Test-Path -LiteralPath $releaseIdPath -PathType Leaf)) {
    $bytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $identifier = 'czb_' + ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
    [IO.File]::WriteAllText($releaseIdPath, $identifier, [Text.UTF8Encoding]::new($false))
}
$releaseId = [IO.File]::ReadAllText($releaseIdPath).Trim()
if ($releaseId -notmatch '^czb_[a-f0-9]{64}$') { throw 'Local release identifier is invalid.' }
[IO.File]::WriteAllText((Join-Path $resolvedOutput 'bridge\bridge-id.txt'), $releaseId, [Text.UTF8Encoding]::new($false))

$metadata = [ordered]@{
    version = '1.0.0'
    supportedGameExeSha256 = @('68F05EDBF6E1E48F1CDCDA30E53BD6E37419A3584B684632DBFE223B1B39D785')
    supportedAsarHeaderSha256 = @(
        'dbaaf692e2391d3b13c635f4e63d4159a8b52bce41ed2beb5cc78749949742c1',
        '083b4f00ede7f8bab0f1558c3ab2fec05221a63e915ea9414e51aeb63a863abe'
    )
    supportedExistingVersionSha256 = @(
        (Get-FileHash -LiteralPath $proxy -Algorithm SHA256).Hash,
        'F659DC5CBD92C26FABBEFB2D76CA1921DEA1E91B8F4CC77A3F9B60E64392DD71'
    )
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'payload-metadata.json') -Encoding UTF8
Write-Output $resolvedOutput
