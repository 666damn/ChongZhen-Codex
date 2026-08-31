param(
    [string]$Configuration = 'Release',
    [string]$Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = [IO.Path]::GetFullPath($Output)
$nodePath = & (Join-Path $PSScriptRoot 'fetch-node.ps1') | Select-Object -Last 1
$staging = & (Join-Path $PSScriptRoot 'stage-current-payload.ps1') -NodePath $nodePath | Select-Object -Last 1
$payload = & (Join-Path $PSScriptRoot 'build-payload.ps1') -Staging $staging | Select-Object -Last 1
& (Join-Path $projectRoot '.tools\dotnet\dotnet.exe') publish `
    (Join-Path $projectRoot 'installer\src\ChongZhenCodexInstaller\ChongZhenCodexInstaller.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
    -p:PayloadZip=$payload -o $resolvedOutput
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
$exe = Join-Path $resolvedOutput 'ChongZhenCodexInstaller.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Single-file installer was not produced.' }
$unexpected = @(Get-ChildItem -LiteralPath $resolvedOutput -File | Where-Object { $_.FullName -ne $exe })
if ($unexpected.Count) { throw "Release directory contains unexpected files: $($unexpected.Name -join ', ')" }
Write-Output $exe
