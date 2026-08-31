param(
    [string]$Staging = (Join-Path (Split-Path -Parent $PSScriptRoot) 'build\release\payload'),
    [string]$Output = (Join-Path (Split-Path -Parent $PSScriptRoot) 'build\release\ChongZhenCodexPayload.zip')
)

$ErrorActionPreference = 'Stop'
$resolvedStaging = [IO.Path]::GetFullPath($Staging)
$resolvedOutput = [IO.Path]::GetFullPath($Output)
$metadataPath = Join-Path $resolvedStaging 'payload-metadata.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) { throw 'Staged payload metadata is missing.' }
$metadata = [IO.File]::ReadAllText($metadataPath) | ConvertFrom-Json
$manifestPath = Join-Path $resolvedStaging 'manifest.json'
if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
$files = [ordered]@{}
Get-ChildItem -LiteralPath $resolvedStaging -Recurse -File |
    Where-Object { $_.FullName -ne $metadataPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($resolvedStaging.Length + 1).Replace('\', '/')
        $files[$relative] = [ordered]@{
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            length = $_.Length
        }
    }
$manifest = [ordered]@{
    version = [string]$metadata.version
    files = $files
    supportedGameExeSha256 = @($metadata.supportedGameExeSha256)
    supportedAsarHeaderSha256 = @($metadata.supportedAsarHeaderSha256)
    supportedExistingVersionSha256 = @($metadata.supportedExistingVersionSha256)
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Force }
[IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedOutput)) | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($resolvedOutput, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $resolvedStaging -Recurse -File | Where-Object { $_.Name -ne 'payload-metadata.json' } | Sort-Object FullName) {
        $relative = $file.FullName.Substring($resolvedStaging.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    }
}
finally { $archive.Dispose() }
Write-Output $resolvedOutput
