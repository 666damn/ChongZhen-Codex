param(
    [Parameter(Mandatory)][string]$Root,
    [switch]$Staged
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    Write-Error "Public tree root does not exist: $resolvedRoot"
    exit 2
}

function Get-CandidateFiles {
    if ($Staged) {
        $relativePaths = @(& git -C $resolvedRoot diff --cached --name-only --diff-filter=ACMR)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate staged Git files.' }
        return @($relativePaths | Where-Object { $_ } | ForEach-Object {
            [IO.Path]::GetFullPath((Join-Path $resolvedRoot $_))
        })
    }

    if (Test-Path -LiteralPath (Join-Path $resolvedRoot '.git')) {
        $relativePaths = @(& git -C $resolvedRoot ls-files)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate tracked Git files.' }
        return @($relativePaths | Where-Object { $_ } | ForEach-Object {
            [IO.Path]::GetFullPath((Join-Path $resolvedRoot $_))
        })
    }

    return @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -Force | ForEach-Object FullName)
}

function Get-RelativeCandidatePath {
    param([Parameter(Mandatory)][string]$File)

    $prefix = $resolvedRoot + '\'
    if (-not $File.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Candidate is outside the public tree root: $File"
    }
    return $File.Substring($prefix.Length).Replace('\', '/')
}

$forbiddenPathPattern = '(?i)(^|/)(node_modules|build|work|diagnostics|backups|dist|\.tools|\.worktrees)(/|$)'
$forbiddenExtensionPattern = '(?i)\.(asar|jsc|exe|dll|db|db-shm|db-wal|log|cer|obj|pdb|png|webp|mp4)$'
$credentialPatterns = @(
    [pscustomobject]@{ Name = 'GitHub credential'; Pattern = ('gh' + '[oprsu]_[A-Za-z0-9_]{20,}') },
    [pscustomobject]@{ Name = 'OpenAI-style credential'; Pattern = ('sk' + '-[A-Za-z0-9_-]{20,}') },
    [pscustomobject]@{ Name = 'access token field'; Pattern = ('(?i)"access_' + 'token"\s*:\s*"[^"\r\n]+"') },
    [pscustomobject]@{ Name = 'refresh token field'; Pattern = ('(?i)"refresh_' + 'token"\s*:\s*"[^"\r\n]+"') },
    [pscustomobject]@{ Name = 'API key field'; Pattern = '(?i)"api_key"\s*:\s*"[^"\r\n]+"' },
    [pscustomobject]@{ Name = 'absolute Windows user path'; Pattern = ('(?i)[A-Z]:\\Users\\' + '[^\\\s"'']+') }
)

$violations = [Collections.Generic.List[string]]::new()
$files = @(Get-CandidateFiles)
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
    $relative = Get-RelativeCandidatePath -File $file
    if ($relative -match $forbiddenPathPattern -or $relative -match $forbiddenExtensionPattern) {
        $violations.Add("forbidden path: $relative")
        continue
    }

    try {
        $content = [IO.File]::ReadAllText($file)
    } catch {
        $violations.Add("unreadable file: $relative")
        continue
    }

    foreach ($entry in $credentialPatterns) {
        if ([regex]::IsMatch($content, $entry.Pattern)) {
            $violations.Add("$($entry.Name): $relative")
        }
    }

    $emails = [regex]::Matches($content, '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b')
    foreach ($email in $emails) {
        $value = $email.Value.ToLowerInvariant()
        if ($value.EndsWith('@example.test') -or $value.EndsWith('@example.com') -or $value.EndsWith('@users.noreply.github.com')) {
            continue
        }
        $violations.Add("email address: $relative")
    }
}

if ($violations.Count -gt 0) {
    $violations | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "PASS: scanned $($files.Count) public files; no private data or game payloads found."
exit 0
