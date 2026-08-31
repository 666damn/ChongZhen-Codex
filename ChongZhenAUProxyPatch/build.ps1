param([Parameter(Mandatory = $true)][string]$AsarPath)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedAsar = (Resolve-Path -LiteralPath $AsarPath).Path
$output = Join-Path $PSScriptRoot 'build'
if (-not (Test-Path -LiteralPath $output)) {
    New-Item -ItemType Directory -Path $output | Out-Null
}

$asarHeaderHash = (& node (Join-Path $projectRoot 'scripts\asar-header-hash.mjs') $resolvedAsar).Trim()
if ($LASTEXITCODE -ne 0 -or $asarHeaderHash -notmatch '^[a-f0-9]{64}$') {
    throw 'Unable to calculate the patched ASAR header hash.'
}
$asarSize = (Get-Item -LiteralPath $resolvedAsar).Length
$generatedHeader = Join-Path $output 'generated_asar.h'
@"
#pragma once
#define CHONGZHEN_CODEX_ASAR_HEADER_HASH "$asarHeaderHash"
#define CHONGZHEN_CODEX_ASAR_SIZE $($asarSize)ULL
"@ | Set-Content -LiteralPath $generatedHeader -Encoding Ascii

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe is missing.' }
$visualStudio = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudio)) { throw 'Visual Studio C++ tools were not found.' }
$vcvars = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvars64.bat'

$originalImportLibrary = Join-Path $output 'version_original.lib'
$command = '"' + $vcvars + '" && lib /nologo /machine:x64 /def:"' +
    (Join-Path $PSScriptRoot 'version_original.def') + '" /out:"' +
    $originalImportLibrary + '" && cl /nologo /std:c++17 /O2 /MT /LD /I"' +
    $output + '" "' + (Join-Path $PSScriptRoot 'version_proxy.cpp') + '" /link /DEF:"' +
    (Join-Path $PSScriptRoot 'version.def') + '" /OUT:"' +
    (Join-Path $output 'version.dll') + '" /IMPLIB:"' +
    (Join-Path $output 'version.lib') + '" "' + $originalImportLibrary + '"'

cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw "Compiler exited with code $LASTEXITCODE." }
Copy-Item -LiteralPath (Join-Path $env:WINDIR 'System32\version.dll') `
    -Destination (Join-Path $output 'version_original.dll') -Force

Write-Output "Built proxy DLLs for ASAR $asarHeaderHash"
