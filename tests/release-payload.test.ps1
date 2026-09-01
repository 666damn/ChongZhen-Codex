param(
    [string]$Payload = (Join-Path (Split-Path -Parent $PSScriptRoot) 'build\release\ChongZhenCodexPayload.zip'),
    [string]$Installer = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\ChongZhenCodexInstaller.exe')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedPayload = [IO.Path]::GetFullPath($Payload)
if (-not (Test-Path -LiteralPath $resolvedPayload -PathType Leaf)) {
    throw "Release payload is missing: $resolvedPayload"
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedPayload)
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("cz-release-audit-" + [Guid]::NewGuid().ToString('N'))
try {
    $expectedBridgeSources = @(
        'bridge-service.js', 'codex-client.js', 'codex-discovery.js', 'hash.js',
        'http-server.js', 'jsonl-rpc.js', 'main.js', 'openai-format.js',
        'prompt-builder.js', 'ready-status.js', 'state-store.js', 'watcher-main.js', 'watcher.js'
    ) | ForEach-Object { "bridge/src/$_" }
    $expected = @(
        'manifest.json', 'bridge/node.exe', 'bridge/package.json',
        'bridge/tools/configure-game-byok.mjs',
        'bridge/patcher/package.json', 'bridge/patcher/package-lock.json',
        'bridge/patcher/src/build-sidecar.mjs', 'bridge/patcher/src/verify-sidecar.mjs',
        'bridge/scripts/asar-header-hash.mjs', 'bridge/scripts/patch-region.mjs',
        'bridge/scripts/patch-renderer.mjs', 'bridge/scripts/verify-asar.mjs',
        'bridge/scripts/verify-asar-delta.mjs', 'global/version.dll'
    ) + $expectedBridgeSources + @(
        Get-ChildItem -LiteralPath (Join-Path $projectRoot 'patcher\node_modules') -Recurse -File |
            Where-Object { $_.FullName -notmatch '\\node_modules\\\.bin\\' } |
            ForEach-Object {
                'bridge/patcher/node_modules/' + $_.FullName.Substring(
                    (Join-Path $projectRoot 'patcher\node_modules').Length + 1).Replace('\', '/')
            }
    )
    $actual = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('\', '/') })
    $difference = @(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object))
    if ($difference.Count) { throw "Payload allowlist mismatch: $($difference | Out-String)" }
    if ($actual | Where-Object { $_ -match '(?i)(^|/)[^/]*\.asar$' }) {
        throw 'Payload must not contain a game ASAR.'
    }
    if ($actual | Where-Object { $_ -match '(?i)(^|/)(bridge-id|release-token)\.txt$' }) {
        throw 'Payload must not contain a build-time token or bridge identifier.'
    }

    $manifestEntry = $archive.GetEntry('manifest.json')
    $reader = [IO.StreamReader]::new($manifestEntry.Open(), [Text.Encoding]::UTF8)
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($manifest.version -ne '1.1.0') { throw 'Unexpected payload version.' }
    if (@($manifest.files.PSObject.Properties).Count -ne ($expected.Count - 1)) { throw 'Manifest file count is wrong.' }
    foreach ($entry in $archive.Entries | Where-Object { $_.Name -and $_.FullName -ne 'manifest.json' }) {
        $relative = $entry.FullName.Replace('\', '/')
        $record = $manifest.files.PSObject.Properties[$relative].Value
        if ($null -eq $record) { throw "Unmanifested entry: $relative" }
        $stream = $entry.Open()
        try {
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') } finally { $sha.Dispose() }
        } finally { $stream.Dispose() }
        if ($entry.Length -ne [long]$record.length -or $hash -ne [string]$record.sha256) {
            throw "Manifest mismatch: $relative"
        }
    }

    $unexpectedNodeModules = @($actual | Where-Object {
        $_ -match '(?i)(^|/)node_modules(/|$)' -and
        -not $_.StartsWith('bridge/patcher/node_modules/', [StringComparison]::Ordinal)
    })
    if ($unexpectedNodeModules.Count) { throw 'Payload contains an unexpected node_modules tree.' }
    $forbiddenNames = '(?i)(^|/)(\.codex|backups?|logs?|saves?|sessions?)(/|$)|state\.db|auth\.json|config\.toml|cookies?'
    if ($actual -match $forbiddenNames) { throw 'Payload contains private/runtime paths.' }

    [IO.Directory]::CreateDirectory($temporary) | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($resolvedPayload, $temporary)
    $textFiles = Get-ChildItem -LiteralPath (Join-Path $temporary 'bridge') -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\patcher\\node_modules\\' } |
        Where-Object { $_.Extension -in '.js', '.mjs', '.json', '.txt' }
    foreach ($file in $textFiles) {
        $content = [IO.File]::ReadAllText($file.FullName)
        if ($content -match '(?i)[A-Z]:\\Users\\[^\\\s"'']+' -or
            $content -match '(?i)"(access_token|refresh_token|api_key)"\s*:\s*"[^"\r\n]+"' -or
            $content -match '(?i)"(session|session_id|account|model)"\s*:\s*"[^"\r\n]+"' -or
            $content -match '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b') {
            throw "Private data pattern in payload text: $($file.Name)"
        }
    }

    $nodeVersion = (& (Join-Path $temporary 'bridge\node.exe') --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $nodeVersion -ne 'v24.13.1') { throw "Unexpected bundled Node version: $nodeVersion" }
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $patcherProbe = & (Join-Path $temporary 'bridge\node.exe') (Join-Path $temporary 'bridge\patcher\src\build-sidecar.mjs') 2>&1 | Out-String
        $patcherExitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if ($patcherExitCode -eq 0 -or $patcherProbe -notmatch 'Usage:') {
        throw 'Packaged sidecar patcher or one of its pinned dependencies cannot be loaded.'
    }
    if (@($manifest.supportedGameExeSha256).Count -lt 1 -or
        @($manifest.supportedAsarHeaderSha256).Count -lt 1 -or
        @($manifest.supportedExistingVersionSha256).Count -lt 1) {
        throw 'Supported-version allowlists are incomplete.'
    }
    if (@($manifest.supportedExistingVersionSha256) -notcontains 'E2415FD1F0F4C6A58544D92376FEFF3CBC9004EE1A9E26B36D3209925C605082') {
        throw 'The validated original game version.dll hash is missing.'
    }
    $profilePath = [IO.Path]::GetFullPath($env:USERPROFILE)
    foreach ($file in Get-ChildItem -LiteralPath $temporary -Recurse -File) {
        if ($file.Length -gt 25MB) { continue }
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
            if ($encoding.GetString($bytes).IndexOf($profilePath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Build user-profile path found in payload: $($file.Name)"
            }
        }
    }
    $totalLength = ($archive.Entries | Measure-Object -Property Length -Sum).Sum
    $totalCompressed = ($archive.Entries | Measure-Object -Property CompressedLength -Sum).Sum
    if ($totalCompressed -ge ($totalLength * 0.8)) { throw 'Payload entries are not using effective compression.' }
    if (-not (Test-Path -LiteralPath $Installer -PathType Leaf)) { throw 'Final installer EXE is missing.' }
    $installerLength = (Get-Item -LiteralPath $Installer).Length
    if ($installerLength -ge 300MB) { throw "Installer exceeds 300 MiB: $installerLength bytes" }
    if ($installerLength -ge ([long](723246671 * 0.5))) { throw 'Installer is not at least 50 percent smaller than v1.0.0.' }
    Write-Output "PASS: audited $($actual.Count) allowlisted payload files; no local Codex/ChatGPT data is packaged."
}
finally {
    $archive.Dispose()
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
