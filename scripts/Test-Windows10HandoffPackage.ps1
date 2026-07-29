[CmdletBinding()]
param(
    [string]$PackageRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    $PackageRoot = Split-Path -Parent $PSScriptRoot
}
$root = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\')
$manifestPath = Join-Path $root 'HANDOFF-MANIFEST.json'
$errors = [System.Collections.Generic.List[string]]::new()

function Add-Error {
    param([Parameter(Mandatory)] [string]$Message)
    $errors.Add($Message)
}

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Add-Error 'handoff-manifest-missing'
}

$manifest = $null
if ($errors.Count -eq 0) {
    try {
        $manifest =
            Get-Content -LiteralPath $manifestPath -Raw |
                ConvertFrom-Json -Depth 20
    }
    catch {
        Add-Error 'handoff-manifest-invalid-json'
    }
}

$verifiedFileCount = 0
if ($null -ne $manifest) {
    if ($manifest.schemaVersion -ne 1 -or
        $manifest.packageType -ne 'jarvisv2-windows10-handoff' -or
        -not [regex]::IsMatch(
            [string]$manifest.sourceCommit,
            '^[a-f0-9]{40}$') -or
        $manifest.sourceDirty -ne $false -or
        $manifest.entryDocument -ne 'WINDOWS10-HANDOFF.md' -or
        $manifest.activationPermitted -ne $false -or
        $manifest.liveExplorer -ne 'not-run') {
        Add-Error 'handoff-manifest-identity-invalid'
    }

    $expectedPaths =
        [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
    foreach ($file in @($manifest.files)) {
        $relativePath = [string]$file.path
        if ([string]::IsNullOrWhiteSpace($relativePath) -or
            $relativePath.Contains('\') -or
            $relativePath.StartsWith('/') -or
            $relativePath -match '(^|/)\.\.(/|$)' -or
            -not $expectedPaths.Add($relativePath)) {
            Add-Error "handoff-file-path-invalid:$relativePath"
            continue
        }

        $candidate = [IO.Path]::GetFullPath(
            (Join-Path $root $relativePath.Replace('/', '\')))
        if (-not $candidate.StartsWith(
                $root + '\',
                [StringComparison]::OrdinalIgnoreCase)) {
            Add-Error "handoff-file-path-escaped:$relativePath"
            continue
        }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            Add-Error "handoff-file-missing:$relativePath"
            continue
        }

        $item = Get-Item -LiteralPath $candidate -Force
        $sha256 = (
            Get-FileHash -LiteralPath $candidate -Algorithm SHA256
        ).Hash
        if ([int64]$item.Length -ne [int64]$file.size -or
            $sha256 -cne [string]$file.sha256) {
            Add-Error "handoff-file-identity-drifted:$relativePath"
            continue
        }
        $verifiedFileCount++
    }

    if ([int]$manifest.fileCount -ne $expectedPaths.Count -or
        $verifiedFileCount -ne $expectedPaths.Count) {
        Add-Error 'handoff-file-count-mismatch'
    }

    $actualPaths = @(
        Get-ChildItem -LiteralPath $root -File -Recurse |
            ForEach-Object {
                [IO.Path]::GetRelativePath(
                    $root,
                    $_.FullName).Replace('\', '/')
            } |
            Where-Object { $_ -cne 'HANDOFF-MANIFEST.json' }
    )
    $unexpected = @($actualPaths | Where-Object {
        -not $expectedPaths.Contains($_)
    })
    if ($actualPaths.Count -ne $expectedPaths.Count -or
        $unexpected.Count -ne 0) {
        Add-Error 'handoff-unexpected-or-unlisted-file'
    }

    $matrixPath = Join-Path $root 'config\platform-matrix.json'
    if (-not (Test-Path -LiteralPath $matrixPath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $matrixPath -Algorithm SHA256).Hash -cne
            [string]$manifest.platformMatrixSha256) {
        Add-Error 'handoff-platform-matrix-identity-drifted'
    }
}

[ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-windows10-handoff-verification'
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    result = if ($errors.Count -eq 0) { 'passed' } else { 'failed' }
    packageRoot = $root
    sourceCommit = if ($null -ne $manifest) {
        [string]$manifest.sourceCommit
    }
    else {
        $null
    }
    verifiedFileCount = $verifiedFileCount
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    errors = @($errors)
} | ConvertTo-Json -Depth 8

if ($errors.Count -ne 0) {
    exit 1
}
