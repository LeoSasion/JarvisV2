[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$NodePath,

    [Parameter(Mandatory)]
    [string]$GitPath,

    [string]$DotnetPath = 'dotnet',

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $artifactsRoot (
        'cc-portable-' +
        [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
}
$target = [IO.Path]::GetFullPath($OutputPath)
$admittedPrefix = $artifactsRoot.TrimEnd('\') + '\'
if (-not $target.StartsWith(
        $admittedPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The portable package target must remain under artifacts.'
}
if (Test-Path -LiteralPath $target) {
    throw 'The portable package target already exists; choose a new path.'
}

$node = [IO.Path]::GetFullPath($NodePath)
if (-not (Test-Path -LiteralPath $node -PathType Leaf) -or
    [IO.Path]::GetFileName($node) -ne 'node.exe') {
    throw 'NodePath must identify an existing absolute node.exe.'
}
$git = [IO.Path]::GetFullPath($GitPath)
if (-not (Test-Path -LiteralPath $git -PathType Leaf) -or
    [IO.Path]::GetFileName($git) -ne 'git.exe' -or
    (Split-Path -Leaf (Split-Path -Parent $git)) -ne 'cmd') {
    throw 'GitPath must identify an existing absolute Git for Windows cmd\\git.exe.'
}
$gitRoot = Split-Path -Parent (Split-Path -Parent $git)
foreach ($gitPrerequisite in @(
        (Join-Path $gitRoot 'cmd\git.exe'),
        (Join-Path $gitRoot 'mingw64\bin\git.exe'),
        (Join-Path $gitRoot 'LICENSE.txt'))) {
    if (-not (Test-Path -LiteralPath $gitPrerequisite -PathType Leaf)) {
        throw "The portable Git prerequisite is missing: $gitPrerequisite"
    }
}

$controlCenterProject = Join-Path $root (
    'src\common\Jarvis.ControlCenter\Jarvis.ControlCenter.csproj')
$piRoot = Join-Path $root 'src\common\Jarvis.PiAgentHost'
$piBridgeProject = Join-Path $piRoot 'Jarvis.PiAgentHost.csproj'
$piSource = Join-Path $piRoot 'src'
$piModules = Join-Path $piRoot 'node_modules'
$desktopHostContract = Join-Path $root (
    'config\pi-agent-desktop-host-contract.json')
foreach ($required in @(
        $controlCenterProject,
        $piBridgeProject,
        (Join-Path $piRoot 'package.json'),
        (Join-Path $piRoot 'pnpm-lock.yaml'),
        $desktopHostContract,
        (Join-Path $piSource 'host.mjs'),
        (Join-Path $piModules '@earendil-works\pi-ai\package.json'),
        (Join-Path $piModules '@earendil-works\pi-coding-agent\package.json'))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "The package prerequisite is missing: $required"
    }
}

$publishOutput = @(
    & $DotnetPath publish `
        $controlCenterProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $target `
        --nologo `
        --warnaserror 2>&1
)
if ($LASTEXITCODE -ne 0) {
    throw (
        'The self-contained Control Center publish failed: ' +
        (($publishOutput | Select-Object -Last 16) -join ' '))
}

$bridgePublishOutput = @(
    & $DotnetPath publish `
        $piBridgeProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $target `
        --nologo `
        --warnaserror 2>&1
)
if ($LASTEXITCODE -ne 0) {
    throw (
        'The self-contained Pi bridge publish failed: ' +
        (($bridgePublishOutput | Select-Object -Last 16) -join ' '))
}

$runtimeNodeRoot = Join-Path $target 'runtime\node'
$runtimeGitRoot = Join-Path $target 'runtime\git'
$runtimePiRoot = Join-Path $target 'runtime\pi-agent'
$runtimePiConfigRoot = Join-Path $runtimePiRoot 'config'
New-Item -ItemType Directory -Path $runtimeNodeRoot | Out-Null
New-Item -ItemType Directory -Path $runtimePiRoot | Out-Null
New-Item -ItemType Directory -Path $runtimePiConfigRoot | Out-Null
Copy-Item -LiteralPath $node -Destination (
    Join-Path $runtimeNodeRoot 'node.exe')
$gitCopyOutput = @(
    & robocopy.exe `
        $gitRoot `
        $runtimeGitRoot `
        /E `
        /COPY:DAT `
        /DCOPY:DAT `
        /R:1 `
        /W:1 `
        /NFL `
        /NDL `
        /NJH `
        /NJS `
        /NP 2>&1
)
$gitCopyExitCode = $LASTEXITCODE
if ($gitCopyExitCode -gt 7) {
    throw (
        "The portable Git copy failed with robocopy exit $gitCopyExitCode`: " +
        (($gitCopyOutput | Select-Object -Last 12) -join ' '))
}
$gitRuntimeEntries = @(
    Get-ChildItem -LiteralPath $runtimeGitRoot -Force -Recurse)
if (@($gitRuntimeEntries | Where-Object {
            ($_.Attributes -band
                [IO.FileAttributes]::ReparsePoint) -ne 0
        }).Count -ne 0) {
    throw 'The copied portable Git runtime contains a reparse point.'
}
$gitRuntimeFiles = @(
    $gitRuntimeEntries |
        Where-Object { -not $_.PSIsContainer } |
        Sort-Object FullName)
$gitRuntimeBytes = ($gitRuntimeFiles |
    Measure-Object -Property Length -Sum).Sum
if ($gitRuntimeFiles.Count -lt 3 -or
    $gitRuntimeFiles.Count -gt 2048 -or
    $gitRuntimeBytes -le 0 -or
    $gitRuntimeBytes -gt 536870912) {
    throw 'The copied portable Git runtime exceeded its file or byte boundary.'
}
Copy-Item -LiteralPath $piSource -Destination $runtimePiRoot -Recurse
Copy-Item -LiteralPath (Join-Path $piRoot 'package.json') `
    -Destination $runtimePiRoot
Copy-Item -LiteralPath (Join-Path $piRoot 'pnpm-lock.yaml') `
    -Destination $runtimePiRoot
Copy-Item -LiteralPath $desktopHostContract `
    -Destination $runtimePiConfigRoot
$runtimePiModules = Join-Path $runtimePiRoot 'node_modules'
$portablePackageNames = @(
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
$pnpmHoistedRoot = Join-Path $piModules '.pnpm\node_modules'
$portablePackageReceipts = @(
    foreach ($packageName in $portablePackageNames) {
        $relativePackagePath = $packageName.Replace('/', '\')
        $linkPath = Join-Path $pnpmHoistedRoot $relativePackagePath
        if (-not (Test-Path -LiteralPath $linkPath -PathType Container)) {
            $linkPath = Join-Path $piModules $relativePackagePath
        }
        if (-not (Test-Path -LiteralPath $linkPath -PathType Container)) {
            throw "The portable Pi package link is missing: $packageName"
        }
        $link = Get-Item -Force -LiteralPath $linkPath
        $linkTarget = [string](@($link.Target)[0])
        if ([string]::IsNullOrWhiteSpace($linkTarget)) {
            throw "The portable Pi package is not a pinned pnpm link: $packageName"
        }
        $sourcePackageRoot = [IO.Path]::GetFullPath((Join-Path `
            (Split-Path -Parent $link.FullName) `
            $linkTarget))
        $sourcePackageJson = Join-Path $sourcePackageRoot 'package.json'
        if (-not (Test-Path -LiteralPath $sourcePackageJson -PathType Leaf)) {
            throw "The portable Pi package manifest is missing: $packageName"
        }
        $sourceManifest = Get-Content -LiteralPath $sourcePackageJson -Raw |
            ConvertFrom-Json
        if ($sourceManifest.name -ne $packageName -or
            [string]::IsNullOrWhiteSpace([string]$sourceManifest.version)) {
            throw "The portable Pi package identity drifted: $packageName"
        }

        $destinationPackageRoot = Join-Path `
            $runtimePiModules `
            $relativePackagePath
        $moduleCopyOutput = @(
            & robocopy.exe `
                $sourcePackageRoot `
                $destinationPackageRoot `
                /E `
                /XD node_modules `
                /COPY:DAT `
                /DCOPY:DAT `
                /R:1 `
                /W:1 `
                /NFL `
                /NDL `
                /NJH `
                /NJS `
                /NP 2>&1
        )
        $moduleCopyExitCode = $LASTEXITCODE
        if ($moduleCopyExitCode -gt 7) {
            throw (
                "The portable package copy failed for $packageName with " +
                "robocopy exit $moduleCopyExitCode`: " +
                (($moduleCopyOutput | Select-Object -Last 12) -join ' '))
        }
        $destinationPackageJson = Join-Path `
            $destinationPackageRoot `
            'package.json'
        [ordered]@{
            name = $packageName
            version = [string]$sourceManifest.version
            packageJsonSha256 = (Get-FileHash `
                -LiteralPath $destinationPackageJson `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
)

$readme = @'
JARVIS2 PORTABLE CONTROL CENTER

This folder is a self-contained Windows x64 desktop build. It does not install,
activate, inject, restart Explorer, or modify the registry.
The fixed repository gate uses the bundled runtime\git\cmd\git.exe directly;
it does not invoke cmd, PowerShell, or a workspace-authored script.

Native session launcher (recommended):
  1. Open jarvis-control-center.exe.
  2. Choose START PI SESSION and select one local workspace.
  3. Keep LOCAL DIAGNOSTIC for the fastest deterministic first turn.

OpenAI Responses conversation:
  1. Open jarvis-control-center.exe and choose CONFIGURE OPENAI.
  2. Choose START PI SESSION and select OPENAI RESPONSES.

Automation equivalent:
  jarvis-control-center.exe --conversation --workspace C:\absolute\workspace --provider local

Resume latest admitted work:
  jarvis-control-center.exe --resume-latest

Windows presence:
  Use WINDOWS PRESENCE in the runtime inspector to enable current-user sign-in
  startup. Jarvis registers one exact REG_SZ command and starts minimized. A
  second launch activates the existing Control Center instead of starting a
  second Pi runtime. The setting is explicit, current-user only and reversible.

The API key is protected under the current Windows user with DPAPI. It is not
stored in this package and is never sent to the offline Pi sidecar. Pi tools are
limited to read, grep, find, ls, and the non-mutating propose_edit,
propose_patch, propose_create_file and propose_change_set tools. Pi may stage
one exact replacement, one 2-8 hunk non-overlapping patch in a single existing
UTF-8 file, one missing UTF-8 file (16 KiB maximum) whose parent directory
already exists, or one ordered 2-4 file change set capped at 32 KiB of review
text. Only the desktop owner can approve it once. Single-file existing targets
use atomic replacement and new files use exclusive creation. A change set uses
a strict durable journal: failure or restart converges to every before state or
every committed after state; simultaneous multi-path visibility is not claimed.
Shell, directory creation, delete, rename, direct-write, VCS metadata mutation,
and unattended approval remain unavailable. The desktop can arm a clean-HEAD
reviewed iteration for at most four owner-approved writes and six hours. Each
approved write must pass the fixed Git, strict UTF-8, tracked/untracked diff and
structured-text gate, then pause for a separate RUN PINNED TESTS ONCE owner
approval. That command is loaded from the clean Git HEAD, launches bundled Node
directly without a shell, has bounded time/output, and is surrounded by pre/post
repository revalidation. Only a pass may start another reasoning turn. Pi cannot
invoke the runner or its approval. Restart restores neither proposal nor process
authority and requires explicit re-arm.
'@
[IO.File]::WriteAllText(
    (Join-Path $target 'README.txt'),
    $readme,
    [Text.UTF8Encoding]::new($false))

$criticalRelativePaths = @(
    'jarvis-control-center.exe',
    'jarvis-control-center.dll',
    'jarvis-desktop-presence.dll',
    'jarvis-control-center.deps.json',
    'jarvis-control-center.runtimeconfig.json',
    'jarvis-pi-agent-desktop-bridge.exe',
    'jarvis-pi-agent-desktop-bridge.dll',
    'jarvis-pi-agent-desktop-bridge.deps.json',
    'jarvis-pi-agent-desktop-bridge.runtimeconfig.json',
    'runtime\node\node.exe',
    'runtime\pi-agent\package.json',
    'runtime\pi-agent\pnpm-lock.yaml',
    'runtime\pi-agent\config\pi-agent-desktop-host-contract.json',
    'runtime\pi-agent\src\host.mjs',
    'README.txt'
) + @(
    $gitRuntimeFiles |
        ForEach-Object {
            [IO.Path]::GetRelativePath($target, $_.FullName)
        }
)
$hashes = @(
    foreach ($relativePath in $criticalRelativePaths) {
        $fullPath = Join-Path $target $relativePath
        [ordered]@{
            path = $relativePath.Replace('\', '/')
            sha256 = (Get-FileHash `
                -LiteralPath $fullPath `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
)
$receipt = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-portable-control-center-package'
    result = 'passed'
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    outputPath = $target
    runtimeLayout =
        'self-contained-wpf-plus-bundled-node-pi-sidecar-and-fixed-git'
    providerDefault = 'local-diagnostic'
    productionProvider = 'openai-responses-opt-in'
    productionModel = 'gpt-5.6-sol'
    desktopPresence =
        'current-user-explicit-sign-in-startup-exact-reg-sz'
    desktopPresenceCommand =
        'jarvis-control-center.exe --resume-latest --minimized'
    desktopSingleInstance =
        'current-user-session-named-auto-reset-activation'
    desktopStartupAvailableToPi = $false
    credentialStore =
        'desktop-current-user-dpapi-atomic-no-sidecar-transport'
    piSidecarNetworkAllowed = $false
    piSidecarCredentialTransportAllowed = $false
    initialTools = @(
        'read',
        'grep',
        'find',
        'ls',
        'propose_edit',
        'propose_patch',
        'propose_create_file',
        'propose_change_set')
    mutationTools = @()
    desktopOwnerApprovedWorkspaceOperations = @(
        'existing-utf8-exact-replacement',
        'existing-utf8-2-to-8-hunk-atomic-patch',
        'missing-utf8-exclusive-create-existing-parent',
        'ordered-2-to-4-file-durable-change-set')
    workspaceEditApprovalMode =
        'one-shot-explicit-operation-before-state-sha256'
    workspaceCreateMaximumBytes = 16384
    workspacePatchMaximumHunks = 8
    workspacePatchMaximumPreviewBytes = 16384
    workspacePatchCommitMode =
        'single-file-atomic-replace-and-post-verify'
    workspaceChangeSetMinimumFiles = 2
    workspaceChangeSetMaximumFiles = 4
    workspaceChangeSetMaximumReviewBytes = 32768
    workspaceChangeSetCommitMode =
        'durable-before-or-after-convergence-no-simultaneous-visibility-claim'
    workspaceChangeSetRecovery =
        'strict-journal-before-tools-rollback-or-complete'
    workspaceChangeSetRecoveryAvailableToPi = $false
    simultaneousMultiPathVisibilityClaimed = $false
    sidecarTransportMaximumFrameBytes = 131072
    workspaceVersionControlMetadataMutation = $false
    reviewedSelfIteration = $true
    reviewedIterationPolicy =
        'desktop-owner-fixed-four-edits-six-hours'
    reviewedIterationValidationProfile =
        'git-head-pathset-text-hash-diffcheck-structured-parse-v2'
    reviewedIterationGitRuntime =
        'bundled-runtime-git-cmd-direct-no-shell'
    reviewedIterationTrustedValidation =
        'owner-approved-pinned-head-node-test-direct-no-shell-pre-post-gate'
    ownerTrustedValidationApprovalRequired = $true
    piTrustedValidationProcessAvailable = $false
    gitRuntimeFileCount = $gitRuntimeFiles.Count
    gitRuntimeBytes = $gitRuntimeBytes
    automaticReasoningContinuation = $true
    unattendedApproval = $false
    unattendedSelfIteration = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    systemMutationPerformed = $false
    portableNodePackageCount = $portablePackageReceipts.Count
    portableNodePackages = $portablePackageReceipts
    packageFileCount = @(
        Get-ChildItem -LiteralPath $target -File -Recurse).Count
    criticalHashes = $hashes
}
$receiptPath = Join-Path $target 'package-receipt.json'
[IO.File]::WriteAllText(
    $receiptPath,
    ($receipt | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

$receipt | ConvertTo-Json -Depth 8
