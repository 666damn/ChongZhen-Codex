$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assertScript = Join-Path $repoRoot 'scripts\assert-public-tree.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("chongzhen-public-tree-test-" + [guid]::NewGuid().ToString('N'))

function Invoke-Scanner {
    param([Parameter(Mandatory)][string]$Root)

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $assertScript -Root $Root *> $null
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

    $safeRoot = Join-Path $fixtureRoot 'safe'
    New-Item -ItemType Directory -Path $safeRoot | Out-Null
    Set-Content -LiteralPath (Join-Path $safeRoot 'README.md') -Value '# safe fixture' -Encoding UTF8
    if ((Invoke-Scanner -Root $safeRoot) -ne 0) {
        throw 'A safe public tree must pass the scanner.'
    }

    $secretRoot = Join-Path $fixtureRoot 'secret'
    New-Item -ItemType Directory -Path $secretRoot | Out-Null
    $token = 'gh' + 'o_' + ('A' * 32)
    Set-Content -LiteralPath (Join-Path $secretRoot 'config.json') -Value ("{`"value`":`"$token`"}") -Encoding UTF8
    if ((Invoke-Scanner -Root $secretRoot) -eq 0) {
        throw 'A tree containing a credential-shaped value must fail the scanner.'
    }

    $binaryRoot = Join-Path $fixtureRoot 'binary'
    New-Item -ItemType Directory -Path $binaryRoot | Out-Null
    Set-Content -LiteralPath (Join-Path $binaryRoot 'game.asar') -Value 'fixture' -Encoding UTF8
    if ((Invoke-Scanner -Root $binaryRoot) -eq 0) {
        throw 'A tree containing a forbidden game payload must fail the scanner.'
    }

    Write-Output 'PASS: public tree scanner rejects secrets and game payloads.'
} finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
