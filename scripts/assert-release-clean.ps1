param(
    [Parameter(Mandatory)][string]$Installer
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $Installer).Path
$directory = Split-Path -Parent $resolved
$siblings = @(Get-ChildItem -LiteralPath $directory -File)
if ($siblings.Count -ne 1 -or $siblings[0].FullName -ne $resolved) { throw 'Release directory must contain exactly one EXE.' }
if ([IO.Path]::GetExtension($resolved) -ne '.exe') { throw 'Release artifact is not an EXE.' }

$process = Start-Process -FilePath $resolved -ArgumentList '--audit-payload' -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw "Embedded payload self-audit failed with exit code $($process.ExitCode)." }

function Test-BinaryContains {
    param([string]$Path, [Text.Encoding]$Encoding, [string]$Needle)
    $stream = [IO.File]::OpenRead($Path)
    try {
        $needleBytes = $Encoding.GetBytes($Needle)
        $buffer = [byte[]]::new(4MB + ($needleBytes.Length * 2))
        $carry = 0
        while (($read = $stream.Read($buffer, $carry, 4MB)) -gt 0) {
            $length = $carry + $read
            if ($Encoding.GetString($buffer, 0, $length).Contains($Needle)) { return $true }
            $carry = [Math]::Min($needleBytes.Length * 2, $length)
            if ($carry -gt 0) { [Array]::Copy($buffer, $length - $carry, $buffer, 0, $carry) }
        }
        return $false
    }
    finally { $stream.Dispose() }
}

$privateRoot = [IO.Path]::GetFullPath($env:USERPROFILE).TrimEnd('\')
foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
    if (Test-BinaryContains -Path $resolved -Encoding $encoding -Needle $privateRoot) {
        throw 'Release EXE contains the build machine user-profile path.'
    }
}
$hash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash
Write-Output "PASS: embedded payload self-audit and binary privacy scan succeeded."
Write-Output "SHA256=$hash"
