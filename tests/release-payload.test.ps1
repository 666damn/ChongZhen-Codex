param(
    [string]$Payload = (Join-Path (Split-Path -Parent $PSScriptRoot) 'build\release\ChongZhenCodexPayload.zip')
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
        'bridge/tools/configure-game-byok.mjs', 'global/version.dll'
    ) + $expectedBridgeSources
    $actual = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('\', '/') })
    $difference = @(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object))
    if ($difference.Count) { throw "Payload allowlist mismatch: $($difference | Out-String)" }
    if ($actual | Where-Object { $_ -match '(?i)(^|/)[^/]*\.asar$' }) {
        throw 'Payload must not contain a game ASAR.'
    }
    if ($actual -contains 'bridge/bridge-id.txt') {
        throw 'Payload must not contain a build-time bridge identifier.'
    }

    $manifestEntry = $archive.GetEntry('manifest.json')
    $reader = [IO.StreamReader]::new($manifestEntry.Open(), [Text.Encoding]::UTF8)
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($manifest.version -ne '1.0.0') { throw 'Unexpected payload version.' }
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

    $forbiddenNames = '(?i)(^|/)(\.codex|node_modules|backups?|logs?|saves?|sessions?)(/|$)|state\.db|auth\.json|config\.toml|cookies?'
    if ($actual -match $forbiddenNames) { throw 'Payload contains private/runtime paths.' }

    [IO.Directory]::CreateDirectory($temporary) | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($resolvedPayload, $temporary)
    $textFiles = Get-ChildItem -LiteralPath (Join-Path $temporary 'bridge') -Recurse -File |
        Where-Object { $_.Extension -in '.js', '.mjs', '.json', '.txt' }
    foreach ($file in $textFiles) {
        $content = [IO.File]::ReadAllText($file.FullName)
        if ($content -match '(?i)[A-Z]:\\Users\\[^\\\s"'']+' -or
            $content -match '(?i)"(access_token|refresh_token|api_key)"\s*:\s*"[^"\r\n]+"' -or
            $content -match '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b') {
            throw "Private data pattern in payload text: $($file.Name)"
        }
    }

    $nodeVersion = (& (Join-Path $temporary 'bridge\node.exe') --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $nodeVersion -ne 'v24.13.1') { throw "Unexpected bundled Node version: $nodeVersion" }
    if (@($manifest.supportedGameExeSha256).Count -lt 1 -or
        @($manifest.supportedAsarHeaderSha256).Count -lt 1 -or
        @($manifest.supportedExistingVersionSha256).Count -lt 1) {
        throw 'Supported-version allowlists are incomplete.'
    }
    if (@($manifest.supportedExistingVersionSha256) -notcontains 'E2415FD1F0F4C6A58544D92376FEFF3CBC9004EE1A9E26B36D3209925C605082') {
        throw 'The validated original game version.dll hash is missing.'
    }
    Write-Output "PASS: audited $($actual.Count) allowlisted payload files; no local Codex/ChatGPT data is packaged."
}
finally {
    $archive.Dispose()
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
