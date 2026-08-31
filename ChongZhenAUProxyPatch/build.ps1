$ErrorActionPreference = 'Stop'

$vs = 'C:\Program Files\Microsoft Visual Studio\2022\Community'
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$output = Join-Path $PSScriptRoot 'build'

if (-not (Test-Path -LiteralPath $vcvars)) {
    throw 'Visual Studio x64 build environment was not found.'
}
if (-not (Test-Path -LiteralPath $output)) {
    New-Item -ItemType Directory -Path $output | Out-Null
}

$originalImportLibrary = Join-Path $output 'version_original.lib'
$command = '"' + $vcvars + '" && lib /nologo /machine:x64 /def:"' +
    (Join-Path $PSScriptRoot 'version_original.def') + '" /out:"' +
    $originalImportLibrary + '" && cl /nologo /std:c++17 /O2 /MT /LD "' +
    (Join-Path $PSScriptRoot 'version_proxy.cpp') + '" /link /DEF:"' +
    (Join-Path $PSScriptRoot 'version.def') + '" /OUT:"' +
    (Join-Path $output 'version.dll') + '" /IMPLIB:"' +
    (Join-Path $output 'version.lib') + '" "' + $originalImportLibrary + '"'

cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) {
    throw "Compiler exited with code $LASTEXITCODE."
}

Copy-Item -LiteralPath 'C:\Windows\System32\version.dll' `
    -Destination (Join-Path $output 'version_original.dll') -Force

Write-Output "Built proxy DLLs in $output"
