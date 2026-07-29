[CmdletBinding()]
param(
    [string]$SourceRef = 'HEAD',
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$allowedOutputRoot = Join-Path $root 'artifacts\handoff'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = $allowedOutputRoot
}

function Get-FullPathWithin {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Parent
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullParent = [IO.Path]::GetFullPath($Parent).TrimEnd('\')
    if (-not ($fullPath.Equals(
                $fullParent,
                [StringComparison]::OrdinalIgnoreCase) -or
            $fullPath.StartsWith(
                $fullParent + '\',
                [StringComparison]::OrdinalIgnoreCase))) {
        throw "Path escaped the handoff artifact root: $fullPath"
    }
    return $fullPath
}

function Remove-SafeTree {
    param([Parameter(Mandatory)] [string]$Path)

    $safePath = Get-FullPathWithin -Path $Path -Parent $allowedOutputRoot
    if (Test-Path -LiteralPath $safePath) {
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}

$status = @(& git -C $root status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw 'git status failed.'
}
if ($status.Count -ne 0) {
    throw 'The handoff package must be created from a clean committed tree.'
}

$commit = (& git -C $root rev-parse "$SourceRef^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or
    -not [regex]::IsMatch($commit, '^[a-f0-9]{40}$')) {
    throw "SourceRef did not resolve to one commit: $SourceRef"
}
$headCommit = (& git -C $root rev-parse 'HEAD^{commit}').Trim()
if ($LASTEXITCODE -ne 0 -or $commit -cne $headCommit) {
    throw 'SourceRef must resolve to the current clean HEAD.'
}

$layoutOutput = @(
    & pwsh -NoLogo -NoProfile -File (
        Join-Path $root 'scripts\Test-PlatformLayout.ps1') 2>&1
)
if ($LASTEXITCODE -ne 0) {
    throw "Platform layout gate failed: $($layoutOutput -join ' ')"
}
$publicationOutput = @(
    & pwsh -NoLogo -NoProfile -File (
        Join-Path $root 'scripts\Test-PublicationBoundary.ps1') 2>&1
)
if ($LASTEXITCODE -ne 0) {
    throw "Publication boundary failed: $($publicationOutput -join ' ')"
}

$outputRoot = Get-FullPathWithin `
    -Path $OutputDirectory `
    -Parent $allowedOutputRoot
$null = New-Item -ItemType Directory -Path $outputRoot -Force
$shortCommit = $commit.Substring(0, 12)
$packageName = "JarvisV2-Windows10-Handoff-$shortCommit"
$finalZip = Join-Path $outputRoot "$packageName.zip"
$workRoot = Join-Path $allowedOutputRoot (
    '.staging-' + [Guid]::NewGuid().ToString('N'))
$seedZip = Join-Path $workRoot 'seed.zip'
$extractRoot = Join-Path $workRoot 'source'
$verifyRoot = Join-Path $workRoot 'verify'

try {
    $null = New-Item -ItemType Directory -Path $workRoot -Force
    & git -C $root archive `
        --format=zip `
        "--prefix=$packageName/" `
        "--output=$seedZip" `
        $commit
    if ($LASTEXITCODE -ne 0) {
        throw 'git archive failed.'
    }

    Expand-Archive -LiteralPath $seedZip -DestinationPath $extractRoot
    $packageRoot = Join-Path $extractRoot $packageName
    if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
        throw 'The archived package root was missing.'
    }

    $files = @(
        Get-ChildItem -LiteralPath $packageRoot -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                [pscustomobject]@{
                    path = [IO.Path]::GetRelativePath(
                        $packageRoot,
                        $_.FullName).Replace('\', '/')
                    size = [int64]$_.Length
                    sha256 = (
                        Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
                    ).Hash
                }
            }
    )
    $matrixPath = Join-Path $packageRoot 'config\platform-matrix.json'
    $manifest = [ordered]@{
        schemaVersion = 1
        packageType = 'jarvisv2-windows10-handoff'
        packageName = $packageName
        sourceCommit = $commit
        sourceRef = $SourceRef
        sourceDirty = $false
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        entryDocument = 'WINDOWS10-HANDOFF.md'
        platformMatrixSha256 = (
            Get-FileHash -LiteralPath $matrixPath -Algorithm SHA256
        ).Hash
        fileCount = $files.Count
        activationPermitted = $false
        liveExplorer = 'not-run'
        files = $files
    }
    $manifestPath = Join-Path $packageRoot 'HANDOFF-MANIFEST.json'
    $manifest |
        ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8

    if (Test-Path -LiteralPath $finalZip) {
        Remove-Item -LiteralPath $finalZip -Force
    }
    Compress-Archive `
        -LiteralPath $packageRoot `
        -DestinationPath $finalZip `
        -CompressionLevel Optimal

    Expand-Archive -LiteralPath $finalZip -DestinationPath $verifyRoot
    $verifiedRoot = Join-Path $verifyRoot $packageName
    $verifiedManifestPath =
        Join-Path $verifiedRoot 'HANDOFF-MANIFEST.json'
    $verifiedManifest =
        Get-Content -LiteralPath $verifiedManifestPath -Raw |
            ConvertFrom-Json -Depth 20
    if ($verifiedManifest.sourceCommit -ne $commit -or
        $verifiedManifest.entryDocument -ne 'WINDOWS10-HANDOFF.md' -or
        $verifiedManifest.activationPermitted -ne $false -or
        $verifiedManifest.liveExplorer -ne 'not-run') {
        throw 'The expanded handoff manifest failed its identity boundary.'
    }
    foreach ($file in @($verifiedManifest.files)) {
        $verifiedPath = Join-Path $verifiedRoot (
            ([string]$file.path).Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $verifiedPath -PathType Leaf)) {
            throw "Package file missing after expansion: $($file.path)"
        }
        $item = Get-Item -LiteralPath $verifiedPath -Force
        $sha256 = (
            Get-FileHash -LiteralPath $verifiedPath -Algorithm SHA256
        ).Hash
        if ([int64]$item.Length -ne [int64]$file.size -or
            $sha256 -cne [string]$file.sha256) {
            throw "Package file identity drifted: $($file.path)"
        }
    }
    $packageVerificationOutput = @(
        & pwsh -NoLogo -NoProfile -File (
            Join-Path $verifiedRoot 'scripts\Test-Windows10HandoffPackage.ps1'
        ) -PackageRoot $verifiedRoot 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw (
            'The expanded package verification gate failed: ' +
            ($packageVerificationOutput -join ' ')
        )
    }
    $packageVerification =
        ($packageVerificationOutput -join [Environment]::NewLine) |
            ConvertFrom-Json -Depth 20
    if ($packageVerification.result -ne 'passed' -or
        $packageVerification.activationPermitted -ne $false -or
        $packageVerification.liveExplorer -ne 'not-run') {
        throw 'The expanded package verification receipt was invalid.'
    }

    $zipItem = Get-Item -LiteralPath $finalZip -Force
    [ordered]@{
        schemaVersion = 1
        result = 'passed'
        packagePath = $zipItem.FullName
        packageSha256 = (
            Get-FileHash -LiteralPath $zipItem.FullName -Algorithm SHA256
        ).Hash
        packageBytes = [int64]$zipItem.Length
        packageName = $packageName
        sourceCommit = $commit
        sourceDirty = $false
        fileCount = $files.Count
        entryDocument = 'WINDOWS10-HANDOFF.md'
        packageVerification = 'passed'
        activationPermitted = $false
        liveExplorer = 'not-run'
    } | ConvertTo-Json -Depth 6
}
finally {
    Remove-SafeTree -Path $workRoot
}
