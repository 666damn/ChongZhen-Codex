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

$proxy = Join-Path $projectRoot 'ChongZhenAUProxyPatch\build\version.dll'
foreach ($required in @($proxy)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Validated build input is missing: $required" }
}
& (Join-Path $projectRoot 'ChongZhenAUProxyPatch\test-proxy-exports.ps1') -ProxyPath $proxy | Out-Null

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
Copy-Item -LiteralPath $proxy -Destination (Join-Path $resolvedOutput 'global\version.dll')

$metadata = [ordered]@{
    version = '1.0.0'
    supportedGameExeSha256 = @('68F05EDBF6E1E48F1CDCDA30E53BD6E37419A3584B684632DBFE223B1B39D785')
    supportedAsarHeaderSha256 = @(
        'dbaaf692e2391d3b13c635f4e63d4159a8b52bce41ed2beb5cc78749949742c1',
        '083b4f00ede7f8bab0f1558c3ab2fec05221a63e915ea9414e51aeb63a863abe'
    )
    supportedExistingVersionSha256 = @(
        'E2415FD1F0F4C6A58544D92376FEFF3CBC9004EE1A9E26B36D3209925C605082',
        (Get-FileHash -LiteralPath $proxy -Algorithm SHA256).Hash,
        'F659DC5CBD92C26FABBEFB2D76CA1921DEA1E91B8F4CC77A3F9B60E64392DD71'
    )
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'payload-metadata.json') -Encoding UTF8
Write-Output $resolvedOutput
