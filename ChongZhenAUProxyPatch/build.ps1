param(
    [string]$AsarPath,
    [switch]$TestOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $PSScriptRoot 'build'
if (-not (Test-Path -LiteralPath $output)) {
    New-Item -ItemType Directory -Path $output | Out-Null
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe is missing.' }
$visualStudio = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudio)) { throw 'Visual Studio C++ tools were not found.' }
$vcvars = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvars64.bat'

$testCommand = '"' + $vcvars + '" && cl /nologo /std:c++17 /EHsc /O2 /MT /I"' +
    $PSScriptRoot + '" "' + (Join-Path $PSScriptRoot 'loader_test.cpp') + '" "' +
    (Join-Path $PSScriptRoot 'loader_core.cpp') + '" /link /OUT:"' +
    (Join-Path $output 'loader_test.exe') + '" bcrypt.lib shell32.lib'
cmd.exe /d /s /c $testCommand
if ($LASTEXITCODE -ne 0) { throw "Loader test compiler exited with code $LASTEXITCODE." }
& (Join-Path $output 'loader_test.exe')
if ($LASTEXITCODE -ne 0) { throw "Loader tests exited with code $LASTEXITCODE." }
if ($TestOnly) { Write-Output 'PASS: native loader tests.'; return }

$originalImportLibrary = Join-Path $output 'version_original.lib'
$command = '"' + $vcvars + '" && lib /nologo /machine:x64 /def:"' +
    (Join-Path $PSScriptRoot 'version_original.def') + '" /out:"' +
    $originalImportLibrary + '" && cl /nologo /std:c++17 /EHsc /O2 /MT /LD /I"' +
    $PSScriptRoot + '" "' + (Join-Path $PSScriptRoot 'version_proxy.cpp') + '" "' +
    (Join-Path $PSScriptRoot 'loader_core.cpp') + '" /link /DEF:"' +
    (Join-Path $PSScriptRoot 'version.def') + '" /OUT:"' +
    (Join-Path $output 'version.dll') + '" /IMPLIB:"' +
    (Join-Path $output 'version.lib') + '" "' + $originalImportLibrary + '" bcrypt.lib shell32.lib'

cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw "Compiler exited with code $LASTEXITCODE." }
Copy-Item -LiteralPath (Join-Path $env:WINDIR 'System32\version.dll') `
    -Destination (Join-Path $output 'version_original.dll') -Force

Write-Output 'Built fail-open sidecar proxy DLLs.'
