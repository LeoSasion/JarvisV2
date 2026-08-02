[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$package = [IO.Path]::GetFullPath($PackagePath)
$packagePrefix = $package.TrimEnd('\') + '\'

function Resolve-AdmittedPackageFile {
    param([Parameter(Mandatory)] [string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath)) {
        return $null
    }
    try {
        $candidate = [IO.Path]::GetFullPath((Join-Path $package $RelativePath))
    }
    catch {
        return $null
    }
    if (-not $candidate.StartsWith(
            $packagePrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    $pathRoot = [IO.Path]::GetPathRoot($candidate)
    $current = $pathRoot
    foreach ($segment in $candidate.Substring($pathRoot.Length).Split(
            @('\', '/'),
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -Force -LiteralPath $current
            if (($item.Attributes -band
                    [IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $null
            }
        }
    }
    return $candidate
}

if (-not (Test-Path -LiteralPath $package -PathType Container)) {
    throw 'PackagePath must identify an existing package directory.'
}
$receiptPath = Join-Path $package 'package-receipt.json'
$expectedCriticalPaths = @(
    'jarvis-control-center.exe',
    'jarvis-control-center.dll',
    'jarvis-control-center.deps.json',
    'jarvis-control-center.runtimeconfig.json',
    'jarvis-pi-agent-desktop-bridge.exe',
    'jarvis-pi-agent-desktop-bridge.dll',
    'jarvis-pi-agent-desktop-bridge.deps.json',
    'jarvis-pi-agent-desktop-bridge.runtimeconfig.json',
    'runtime/node/node.exe',
    'runtime/git/cmd/git.exe',
    'runtime/git/LICENSE.txt',
    'runtime/pi-agent/package.json',
    'runtime/pi-agent/pnpm-lock.yaml',
    'runtime/pi-agent/config/pi-agent-desktop-host-contract.json',
    'runtime/pi-agent/src/host.mjs',
    'README.txt'
)
$requiredFiles = @(
    'jarvis-control-center.exe',
    'jarvis-control-center.dll',
    'jarvis-control-center.deps.json',
    'jarvis-control-center.runtimeconfig.json',
    'jarvis-pi-agent-desktop-bridge.exe',
    'jarvis-pi-agent-desktop-bridge.dll',
    'jarvis-pi-agent-desktop-bridge.deps.json',
    'jarvis-pi-agent-desktop-bridge.runtimeconfig.json',
    'runtime\node\node.exe',
    'runtime\git\cmd\git.exe',
    'runtime\git\LICENSE.txt',
    'runtime\pi-agent\package.json',
    'runtime\pi-agent\pnpm-lock.yaml',
    'runtime\pi-agent\config\pi-agent-desktop-host-contract.json',
    'runtime\pi-agent\src\host.mjs',
    'runtime\pi-agent\node_modules\@earendil-works\pi-ai\package.json',
    'runtime\pi-agent\node_modules\@earendil-works\pi-coding-agent\package.json',
    'README.txt',
    'package-receipt.json'
)
$failures = [Collections.Generic.List[string]]::new()
$expectedPortablePackages = @(
    '@earendil-works/pi-agent-core',
    '@earendil-works/pi-ai',
    '@earendil-works/pi-coding-agent',
    '@earendil-works/pi-tui',
    '@mariozechner/clipboard',
    '@mariozechner/clipboard-win32-x64-msvc',
    'balanced-match',
    'brace-expansion',
    'chalk',
    'cross-spawn',
    'diff',
    'get-east-asian-width',
    'glob',
    'graceful-fs',
    'highlight.js',
    'hosted-git-info',
    'ignore',
    'isexe',
    'jiti',
    'lru-cache',
    'marked',
    'minimatch',
    'partial-json',
    'path-key',
    'proper-lockfile',
    'retry',
    'semver',
    'shebang-command',
    'shebang-regex',
    'signal-exit',
    'typebox',
    'undici',
    'which',
    'yaml'
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (
            Join-Path $package $relativePath) -PathType Leaf)) {
        $failures.Add("Missing package file: $relativePath")
    }
}

$receipt = $null
if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
}
$receiptCriticalPaths = @($receipt.criticalHashes | ForEach-Object path)
$missingExpectedCriticalPaths = @(
    $expectedCriticalPaths |
        Where-Object { $receiptCriticalPaths -notcontains $_ })
$duplicateCriticalPathCount = @(
    $receiptCriticalPaths |
        Group-Object |
        Where-Object Count -ne 1).Count
$actualGitFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $package 'runtime\git') `
        -File -Recurse |
        Sort-Object FullName)
$actualGitPaths = @(
    $actualGitFiles |
        ForEach-Object {
            [IO.Path]::GetRelativePath($package, $_.FullName).Replace('\', '/')
        })
$actualGitBytes = ($actualGitFiles |
    Measure-Object -Property Length -Sum).Sum
$receiptGitPaths = @(
    $receiptCriticalPaths |
        Where-Object { $_.StartsWith('runtime/git/', [StringComparison]::Ordinal) })
if ($null -eq $receipt -or
    $receipt.schemaVersion -ne 1 -or
    $receipt.receiptType -ne 'jarvisv2-portable-control-center-package' -or
    $receipt.result -ne 'passed' -or
    $receipt.runtimeLayout -ne
        'self-contained-wpf-plus-bundled-node-pi-sidecar-and-fixed-git' -or
    $receipt.productionProvider -ne 'openai-responses-opt-in' -or
    $receipt.productionModel -ne 'gpt-5.6-sol' -or
    $receipt.piSidecarNetworkAllowed -or
    $receipt.piSidecarCredentialTransportAllowed -or
    $receipt.activationPermitted -or
    $receipt.liveExplorer -ne 'not-run' -or
    $receipt.systemMutationPerformed -or
    $receipt.portableNodePackageCount -ne
        $expectedPortablePackages.Count -or
    $missingExpectedCriticalPaths.Count -ne 0 -or
    $duplicateCriticalPathCount -ne 0 -or
    $receipt.gitRuntimeFileCount -ne $actualGitFiles.Count -or
    $receipt.gitRuntimeBytes -ne $actualGitBytes -or
    ($receiptGitPaths -join '|') -ne ($actualGitPaths -join '|') -or
    (@($receipt.portableNodePackages | ForEach-Object name) -join '|') -ne
        ($expectedPortablePackages -join '|') -or
    (@($receipt.initialTools) -join '|') -ne
        'read|grep|find|ls|propose_edit' -or
    @($receipt.mutationTools).Count -ne 0 -or
    (@($receipt.desktopOwnerApprovedWorkspaceOperations) -join '|') -ne
        'existing-utf8-exact-replacement' -or
    $receipt.workspaceEditApprovalMode -ne
        'one-shot-exact-before-sha256' -or
    -not $receipt.reviewedSelfIteration -or
    $receipt.reviewedIterationPolicy -ne
        'desktop-owner-fixed-four-edits-six-hours' -or
    $receipt.reviewedIterationValidationProfile -ne
        'git-head-pathset-diffcheck-structured-parse-v1' -or
    $receipt.reviewedIterationGitRuntime -ne
        'bundled-runtime-git-cmd-direct-no-shell' -or
    -not $receipt.automaticReasoningContinuation -or
    $receipt.unattendedApproval -or
    $receipt.unattendedSelfIteration) {
    $failures.Add('The portable package receipt failed its safety contract.')
}

if ($null -ne $receipt) {
    foreach ($entry in @($receipt.criticalHashes)) {
        $fullPath = Resolve-AdmittedPackageFile `
            -RelativePath ([string]$entry.path)
        if ($null -eq $fullPath -or
            -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            $failures.Add(
                "Unadmitted or missing hashed package file: $($entry.path)")
            continue
        }
        $actual = (Get-FileHash `
            -LiteralPath $fullPath `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne [string]$entry.sha256) {
            $failures.Add("Package hash mismatch: $($entry.path)")
        }
    }
    foreach ($packageEntry in @($receipt.portableNodePackages)) {
        $packageManifestPath = Resolve-AdmittedPackageFile `
            -RelativePath ('runtime\pi-agent\node_modules\' +
                ([string]$packageEntry.name).Replace('/', '\') +
                '\package.json')
        if ($null -eq $packageManifestPath -or
            -not (Test-Path -LiteralPath $packageManifestPath -PathType Leaf)) {
            $failures.Add(
                "Missing portable package manifest: $($packageEntry.name)")
            continue
        }
        $actualPackageHash = (Get-FileHash `
            -LiteralPath $packageManifestPath `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualPackageHash -ne [string]$packageEntry.packageJsonSha256) {
            $failures.Add(
                "Portable package hash mismatch: $($packageEntry.name)")
        }
    }
}

$piPackage = Get-Content -LiteralPath (
    Join-Path $package 'runtime\pi-agent\package.json') -Raw |
    ConvertFrom-Json
if ($piPackage.dependencies.'@earendil-works/pi-ai' -ne '0.82.1' -or
    $piPackage.dependencies.'@earendil-works/pi-coding-agent' -ne '0.82.1') {
    $failures.Add('The packaged Pi dependency versions are not exact.')
}

$credentialArtifacts = @(
    Get-ChildItem -LiteralPath $package -File -Recurse |
        Where-Object {
            $_.Name -match '(?i)(?:\.j2secret$|^openai-api-key\.|^\.env(?:\.|$)|^(?:credential|credentials|auth|api-key)\.(?:json|txt)$)'
        }
)
if ($credentialArtifacts.Count -ne 0) {
    $failures.Add('The portable package contains a credential-like artifact.')
}

$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $nodeOutput = @(
        & (Join-Path $package 'runtime\node\node.exe') `
            (Join-Path $package 'runtime\pi-agent\src\host.mjs') `
            inspect 2>&1
    )
    $nodeExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
$nodeReceipt = $null
try {
    $nodeReceipt =
        ($nodeOutput -join [Environment]::NewLine) |
            ConvertFrom-Json
}
catch {
    $nodeReceipt = $null
}
if ($nodeExitCode -ne 0 -or
    $null -eq $nodeReceipt -or
    $nodeReceipt.result -ne 'passed-embedded-dependency' -or
    $nodeReceipt.installedVersion -ne '0.82.1' -or
    -not $nodeReceipt.piOffline -or
    $nodeReceipt.modelNetworkAllowed -or
    $nodeReceipt.credentialTransportAllowed -or
    $nodeReceipt.activationPermitted) {
    $failures.Add(
        'The packaged Pi runtime inspection did not pass offline admission.')
}

$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $runtimeProbeOutput = @(
        & (Join-Path $package 'jarvis-pi-agent-desktop-bridge.exe') `
            runtime-probe `
            --node (Join-Path $package 'runtime\node\node.exe') `
            --sidecar (Join-Path $package 'runtime\pi-agent\src\host.mjs') `
            2>&1
    )
    $runtimeProbeExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
$runtimeProbeReceipt = $null
try {
    $runtimeProbeReceipt =
        ($runtimeProbeOutput -join [Environment]::NewLine) |
            ConvertFrom-Json
}
catch {
    $runtimeProbeReceipt = $null
}
if ($runtimeProbeExitCode -ne 0 -or
    $null -eq $runtimeProbeReceipt -or
    $runtimeProbeReceipt.result -ne 'passed' -or
    -not $runtimeProbeReceipt.runtimeCompositionPassed -or
    -not $runtimeProbeReceipt.multiTurnPassed -or
    -not $runtimeProbeReceipt.toolRoundTripPassed -or
    -not $runtimeProbeReceipt.workspaceEditProposalPassed -or
    -not $runtimeProbeReceipt.workspaceEditApprovalPassed -or
    -not $runtimeProbeReceipt.workspaceEditReplayRejected -or
    -not $runtimeProbeReceipt.workspaceEditDriftRejected -or
    -not $runtimeProbeReceipt.workspaceEditRejectionPassed -or
    -not $runtimeProbeReceipt.workspaceEditShutdownExpirationPassed -or
    -not $runtimeProbeReceipt.workspaceEditFixtureMutationPerformed -or
    -not $runtimeProbeReceipt.checkpointStoreRoundTripPassed -or
    -not $runtimeProbeReceipt.checkpointStoreCiphertextPassed -or
    -not $runtimeProbeReceipt.quiesceClosedSubmission -or
    -not $runtimeProbeReceipt.shutdownCancelledActiveTurn -or
    -not $runtimeProbeReceipt.orderlyShutdownPassed -or
    -not $runtimeProbeReceipt.startupRollbackPassed -or
    -not $runtimeProbeReceipt.credentialEnvironmentClean -or
    $runtimeProbeReceipt.brokerFaultCount -ne 0 -or
    $runtimeProbeReceipt.credentialTransportAllowed -or
    $runtimeProbeReceipt.piSidecarModelNetworkAllowed -or
    $runtimeProbeReceipt.liveModelNetwork -ne 'diagnostic-only' -or
    $runtimeProbeReceipt.liveExplorer -ne 'not-run' -or
    $runtimeProbeReceipt.mutationPerformed) {
    $failures.Add(
        'The packaged desktop runtime probe did not complete its offline lifecycle.')
}

$passed = $failures.Count -eq 0
[ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-portable-control-center-package-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    packagePath = $package
    requiredFileCount = $requiredFiles.Count
    packageFileCount = if (Test-Path -LiteralPath $package) {
        @(Get-ChildItem -LiteralPath $package -File -Recurse).Count
    } else { 0 }
    criticalHashCount = if ($null -ne $receipt) {
        @($receipt.criticalHashes).Count
    } else { 0 }
    embeddedPiInspectionPassed =
        $nodeExitCode -eq 0 -and $null -ne $nodeReceipt
    packagedDesktopRuntimeProbePassed =
        $runtimeProbeExitCode -eq 0 -and $null -ne $runtimeProbeReceipt
    liveModelNetwork = 'not-run'
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    failures = $failures
} | ConvertTo-Json -Depth 8

if (-not $passed) {
    exit 1
}
