param([Parameter(Mandatory = $true)][string]$ProxyPath)

$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe is missing.' }
$visualStudio = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$dumpbin = Get-ChildItem -LiteralPath (Join-Path $visualStudio 'VC\Tools\MSVC') -Recurse -Filter dumpbin.exe |
    Where-Object { $_.FullName -match '\\Hostx64\\x64\\dumpbin\.exe$' } |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $dumpbin) { throw '64-bit dumpbin.exe was not found.' }

$expected = @(
    'GetFileVersionInfoA', 'GetFileVersionInfoByHandle', 'GetFileVersionInfoExA', 'GetFileVersionInfoExW',
    'GetFileVersionInfoSizeA', 'GetFileVersionInfoSizeExA', 'GetFileVersionInfoSizeExW', 'GetFileVersionInfoSizeW',
    'GetFileVersionInfoW', 'VerFindFileA', 'VerFindFileW', 'VerInstallFileA', 'VerInstallFileW',
    'VerLanguageNameA', 'VerLanguageNameW', 'VerQueryValueA', 'VerQueryValueW'
)
$dumpText = (& $dumpbin /nologo /exports $ProxyPath) -join "`n"
$missing = @($expected | Where-Object { $dumpText -notmatch "(?m)\b$([regex]::Escape($_))\b" })
if ($missing.Count) { throw "Proxy exports are missing: $($missing -join ', ')" }
Write-Output "PASS: proxy exports all $($expected.Count) Version APIs."
