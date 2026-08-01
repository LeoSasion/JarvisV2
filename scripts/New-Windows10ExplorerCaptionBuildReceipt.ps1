[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DotnetPath,

    [string]$OutputPath = (
        'artifacts\win10-explorer-caption-session\build-receipt.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Get-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)] [string]$BasePath,
        [Parameter(Mandatory)] [string]$FullPath
    )

    $baseWithSeparator =
        [IO.Path]::GetFullPath($BasePath).TrimEnd('\') + '\'
    $baseUri = [Uri]$baseWithSeparator
    $pathUri = [Uri][IO.Path]::GetFullPath($FullPath)
    return [Uri]::UnescapeDataString(
        $baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

$artifactsRoot = [IO.Path]::GetFullPath(
    (Join-Path $root 'artifacts'))
$resolvedOutputPath = [IO.Path]::GetFullPath(
    (Join-Path $root $OutputPath))
$allowedOutputPrefix =
    $artifactsRoot.TrimEnd('\') + '\'
if (-not $resolvedOutputPath.StartsWith(
        $allowedOutputPrefix,
        [StringComparison]::OrdinalIgnoreCase))
{
    throw 'The build receipt must remain under the repository artifacts root.'
}

$resolvedDotnetPath = [IO.Path]::GetFullPath($DotnetPath)
if (-not (Test-Path -LiteralPath $resolvedDotnetPath -PathType Leaf))
{
    throw "The fixed dotnet executable is missing: $resolvedDotnetPath"
}

$sourceRoots = @(
    'src\platforms\windows10\Jarvis.Win10.HostAdmission',
    'src\platforms\windows10\Jarvis.Win10.ShellSurfaceProbe',
    'src\platforms\windows10\Jarvis.Win10.ExplorerCaptionPlan',
    'src\platforms\windows10\Jarvis.Win10.ExplorerCaptionSession'
)
$liveObservationRelativePath = (
    'docs\receipts\' +
    'win10-explorer-caption-redraw-live-' +
    '20260731T084924086Z-608182a4\' +
    'observation.json')
$liveObservationPath = Join-Path $root $liveObservationRelativePath
$sourceFiles = @(
    foreach ($relativeRoot in $sourceRoots) {
        Get-ChildItem `
            -LiteralPath (Join-Path $root $relativeRoot) `
            -File `
            -Recurse |
            Where-Object {
                $_.Extension -In @('.cs', '.csproj') -and
                $_.FullName -notmatch '[\\](?:bin|obj)[\\]'
            }
    }
    Get-Item -LiteralPath (
        Join-Path $root 'config\windows10-host-profiles.json')
    Get-Item -LiteralPath (
        Join-Path $root 'config\windows10-host-profiles.schema.json')
    Get-Item -LiteralPath (
        Join-Path $root 'global.json')
    Get-Item -LiteralPath $PSCommandPath
    Get-Item -LiteralPath (
        Join-Path $root 'scripts\Test-Windows10ExplorerCaptionPlan.ps1')
    Get-Item -LiteralPath (
        Join-Path $root 'scripts\Test-Windows10ExplorerCaptionSession.ps1')
    Get-Item -LiteralPath $liveObservationPath
) | Sort-Object FullName -Unique

$sourceEvidence = @(
    foreach ($file in $sourceFiles) {
        $relativePath = (
            Get-RepositoryRelativePath `
                -BasePath $root `
                -FullPath $file.FullName
        ).Replace('\', '/')
        [pscustomobject]@{
            relativePath = $relativePath
            size = [int64]$file.Length
            sha256 = (
                Get-FileHash `
                    -LiteralPath $file.FullName `
                    -Algorithm SHA256
            ).Hash
        }
    }
)
$aggregateMaterial = @(
    foreach ($entry in $sourceEvidence) {
        "$($entry.relativePath)`0$($entry.sha256)`n"
    }
) -join ''
$aggregateHasher = [Security.Cryptography.SHA256]::Create()
try {
    $sourceAggregateSha256 = [BitConverter]::ToString(
        $aggregateHasher.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes($aggregateMaterial))
    ).Replace('-', '')
}
finally {
    $aggregateHasher.Dispose()
}

$projectPath = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ExplorerCaptionSession\' +
    'Jarvis.Win10.ExplorerCaptionSession.csproj')
$buildOutput = @(
    & $resolvedDotnetPath build `
        $projectPath `
        --configuration Release `
        --no-incremental `
        --nologo `
        --warnaserror 2>&1
)
$buildExitCode = $LASTEXITCODE
$buildLogMaterial = @(
    $buildOutput | ForEach-Object { [string]$_ }
) -join "`n"
$buildLogHasher = [Security.Cryptography.SHA256]::Create()
try {
    $buildLogSha256 = [BitConverter]::ToString(
        $buildLogHasher.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes($buildLogMaterial))
    ).Replace('-', '')
}
finally {
    $buildLogHasher.Dispose()
}

$auditOutput = @(
    & powershell `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (
            Join-Path $root (
                'scripts\Test-Windows10ExplorerCaptionSession.ps1')) `
        -DotnetPath $resolvedDotnetPath 2>&1
)
$auditExitCode = $LASTEXITCODE
$auditReceipt = $null
try {
    $auditReceipt =
        ($auditOutput -join [Environment]::NewLine) |
            ConvertFrom-Json
}
catch {
    $auditReceipt = $null
}

$liveObservation = $null
$liveObservationEvidenceValid = $false
try {
    $liveObservation =
        Get-Content -LiteralPath $liveObservationPath -Raw |
            ConvertFrom-Json
    $liveObservationRoot = Split-Path -Parent $liveObservationPath
    $invalidObservationEvidence = @(
        foreach ($entry in @($liveObservation.evidence)) {
            $evidencePath = Join-Path $liveObservationRoot (
                [string]$entry.relativePath)
            if (-not (
                    Test-Path -LiteralPath $evidencePath -PathType Leaf) -or
                (Get-FileHash `
                    -LiteralPath $evidencePath `
                    -Algorithm SHA256).Hash -ne [string]$entry.sha256)
            {
                [string]$entry.relativePath
            }
        }
    )
    $liveObservationEvidenceValid =
        $liveObservation.result -eq
            'api-readback-and-refresh-passed-rollback-verified-' +
            'visual-diff-failed-light-app-theme' -and
        -not [bool]$liveObservation.visual.verificationPassed -and
        [bool]$liveObservation.visual.fullImageHashesIdentical -and
        [bool]$liveObservation.session.rollbackVerified -and
        [int]$liveObservation.theme.appsUseLightTheme -eq 1 -and
        [bool]$liveObservation.recovery.explorerProcessStable -and
        [bool]$liveObservation.recovery.noResidualLiveTarget -and
        $invalidObservationEvidence.Count -eq 0
}
catch {
    $liveObservation = $null
    $liveObservationEvidenceValid = $false
}

$outputRelativePaths = @(
    'src/platforms/windows10/Jarvis.Win10.HostAdmission/bin/Release/net8.0-windows/jarvis-win10-host-admission.dll',
    'src/platforms/windows10/Jarvis.Win10.ShellSurfaceProbe/bin/Release/net8.0-windows/jarvis-win10-shell-surface-probe.dll',
    'src/platforms/windows10/Jarvis.Win10.ExplorerCaptionPlan/bin/Release/net8.0-windows/jarvis-win10-explorer-caption-plan.dll',
    'src/platforms/windows10/Jarvis.Win10.ExplorerCaptionSession/bin/Release/net8.0-windows/jarvis-win10-explorer-caption-session.dll'
)
$outputs = @(
    foreach ($relativePath in $outputRelativePaths) {
        $fullPath = Join-Path $root $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            continue
        }

        $item = Get-Item -LiteralPath $fullPath
        [pscustomobject]@{
            relativePath = $relativePath
            size = [int64]$item.Length
            sha256 = (
                Get-FileHash `
                    -LiteralPath $fullPath `
                    -Algorithm SHA256
            ).Hash
        }
    }
)

$dotnetVersion = @(
    & $resolvedDotnetPath --version 2>&1
) -join [Environment]::NewLine
$passed =
    $buildExitCode -eq 0 -and
    $auditExitCode -eq 0 -and
    $null -ne $auditReceipt -and
    $auditReceipt.result -eq 'passed' -and
    $liveObservationEvidenceValid -and
    $outputs.Count -eq $outputRelativePaths.Count

$receipt = [ordered]@{
    schemaVersion = 1
    receiptType =
        'jarvisv2-win10-explorer-caption-fixed-toolchain-build'
    result = if ($passed) { 'passed' } else { 'failed' }
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    source = [ordered]@{
        aggregateSha256 = $sourceAggregateSha256
        fileCount = $sourceEvidence.Count
        files = $sourceEvidence
    }
    toolchain = [ordered]@{
        dotnetPath = $resolvedDotnetPath
        version = $dotnetVersion
        sha256 = (
            Get-FileHash `
                -LiteralPath $resolvedDotnetPath `
                -Algorithm SHA256
        ).Hash
    }
    build = [ordered]@{
        project = (
            Get-RepositoryRelativePath `
                -BasePath $root `
                -FullPath $projectPath
        ).Replace('\', '/')
        configuration = 'Release'
        noIncremental = $true
        warningsTreatedAsErrors = $true
        exitCode = $buildExitCode
        logLineCount = $buildOutput.Count
        logSha256 = $buildLogSha256
    }
    audit = [ordered]@{
        exitCode = $auditExitCode
        result = $auditReceipt.result
        checkCount = $auditReceipt.checkCount
        passedCount = $auditReceipt.passedCount
        scenarioCount = $auditReceipt.scenarioCount
        scenarioPassedCount = $auditReceipt.scenarioPassedCount
    }
    priorLiveObservation = [ordered]@{
        relativePath = $liveObservationRelativePath.Replace('\', '/')
        sha256 = (
            Get-FileHash `
                -LiteralPath $liveObservationPath `
                -Algorithm SHA256
        ).Hash
        result = $liveObservation.result
        evidenceValid = $liveObservationEvidenceValid
        visualVerificationPassed =
            [bool]$liveObservation.visual.verificationPassed
        rollbackVerified =
            [bool]$liveObservation.session.rollbackVerified
        appsUseLightTheme =
            [int]$liveObservation.theme.appsUseLightTheme
        explorerProcessStable =
            [bool]$liveObservation.recovery.explorerProcessStable
        noResidualLiveTarget =
            [bool]$liveObservation.recovery.noResidualLiveTarget
    }
    outputs = $outputs
    exactHostProfile = 'win10-22h2-19045.6466-x64'
    liveMutationRun = $false
    moduleActivationPermitted = $false
    mutationPerformed = $false
    liveExplorer = 'bounded-nonmodule-observation-recorded'
}

$outputDirectory = Split-Path -Parent $resolvedOutputPath
$null = New-Item `
    -ItemType Directory `
    -Path $outputDirectory `
    -Force
$temporaryPath =
    "$resolvedOutputPath.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    [IO.File]::WriteAllText(
        $temporaryPath,
        ($receipt | ConvertTo-Json -Depth 12),
        [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf) {
        Remove-Item -LiteralPath $resolvedOutputPath -Force
    }
    Move-Item `
        -LiteralPath $temporaryPath `
        -Destination $resolvedOutputPath
}
finally {
    if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

$receipt | ConvertTo-Json -Depth 12
if (-not $passed) {
    exit 1
}
