$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$installRoot = Join-Path $projectRoot '.tools\dotnet'
$dotnet = Join-Path $installRoot 'dotnet.exe'
if (Test-Path -LiteralPath $dotnet) {
    $sdks = & $dotnet --list-sdks
    if ($LASTEXITCODE -eq 0 -and ($sdks -match '(?m)^8\.0\.')) {
        Write-Output "Using existing project .NET 8 SDK: $dotnet"
        exit 0
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('chongzhen-dotnet-' + [Guid]::NewGuid().ToString('N'))
$installer = Join-Path $temporaryRoot 'dotnet-install.ps1'
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer `
        -Channel '8.0' -Quality 'GA' -Architecture 'x64' -InstallDir $installRoot
    if ($LASTEXITCODE -ne 0) { throw "dotnet-install exited with code $LASTEXITCODE." }
    & $dotnet --info
    if ($LASTEXITCODE -ne 0) { throw 'The project-local .NET SDK failed validation.' }
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($temporaryRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemp.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTemp).StartsWith('chongzhen-dotnet-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
