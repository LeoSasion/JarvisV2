[CmdletBinding()]
param(
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path (
    $root
) 'artifacts\explorer-xaml-read-review-packages\runs'
$supervisor = Join-Path (
    $root
) 'src\platforms\windows11\Jarvis.Supervisor\bin\Release\net8.0-windows\jarvis-supervisor.exe'
$killSwitchPath = Join-Path $env:LOCALAPPDATA 'JARVIS2\disabled.flag'
$permitPath = Join-Path $env:LOCALAPPDATA 'JARVIS2\active-module.txt'
$recoveryLeasePath = Join-Path (
    $env:LOCALAPPDATA
) 'JARVIS2\Recovery\m2-recovery-terminal.json'
$contractPath = Join-Path (
    $root
) 'config\explorer-xaml-surface-discovery-contract.json'
$sourceRoot = Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerTapReadOnly'

function Get-RelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return [IO.Path]::GetRelativePath(
        $root,
        [IO.Path]::GetFullPath($Path)
    ).Replace('\', '/')
}

function Get-Identity {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    return [pscustomobject]@{
        relativePath = Get-RelativePath -Path $item.FullName
        size = $item.Length
        sha256 = (
            Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256
        ).Hash
    }
}

function Resolve-SafeOutputPath {
    param(
        [string]$RequestedPath
    )

    if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        $runId =
            [DateTime]::UtcNow.ToString(
                'yyyyMMddTHHmmssfffZ',
                [Globalization.CultureInfo]::InvariantCulture
            ) +
            '-' +
            [Guid]::NewGuid().ToString('N').Substring(0, 8)
        return Join-Path $artifactRoot "$runId.json"
    }

    $candidate = if ([IO.Path]::IsPathRooted($RequestedPath)) {
        [IO.Path]::GetFullPath($RequestedPath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $root $RequestedPath))
    }
    $allowedRoot = [IO.Path]::GetFullPath($artifactRoot)
    if (-not $candidate.StartsWith(
            $allowedRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputPath must remain under $allowedRoot."
    }
    return $candidate
}

$observedAtUtc = [DateTime]::UtcNow
$blockedReasons = [Collections.Generic.List[string]]::new()

$compatibility = $null
$compatibilityError = $null
if (Test-Path -LiteralPath $supervisor -PathType Leaf) {
    try {
        $compatibilityOutput = @(& $supervisor inspect 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Supervisor inspect exited with code $LASTEXITCODE."
        }
        $compatibility = (
            $compatibilityOutput -join [Environment]::NewLine
        ) | ConvertFrom-Json -Depth 100
    }
    catch {
        $compatibilityError = $_.Exception.Message
    }
}
else {
    $compatibilityError = 'release-supervisor-not-built'
}

$compatibilityPassed =
    $null -ne $compatibility -and
    [bool]$compatibility.compatible -and
    @($compatibility.checks).Count -eq 23 -and
    @($compatibility.checks | Where-Object { -not $_.passed }).Count -eq 0
if (-not $compatibilityPassed) {
    $blockedReasons.Add('fresh-compatibility-inspection-failed')
}

$killSwitchArmed = Test-Path -LiteralPath $killSwitchPath -PathType Leaf
$permitPresent = Test-Path -LiteralPath $permitPath -PathType Leaf
if (-not $killSwitchArmed) {
    $blockedReasons.Add('kill-switch-not-armed')
}
if ($permitPresent) {
    $blockedReasons.Add('one-shot-permit-present')
}

$service = @(
    Get-CimInstance `
        -ClassName Win32_Service `
        -Filter "Name='Windhawk'" `
        -ErrorAction SilentlyContinue
)
$serviceLocked =
    $service.Count -eq 1 -and
    $service[0].State -eq 'Stopped' -and
    $service[0].StartMode -eq 'Manual' -and
    [uint64]$service[0].ProcessId -eq 0
if (-not $serviceLocked) {
    $blockedReasons.Add('windhawk-service-not-stopped-manual')
}

$explorerMappings = [Collections.Generic.List[object]]::new()
$moduleInspectionErrors = [Collections.Generic.List[string]]::new()
foreach ($process in @(Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
    try {
        foreach ($module in @($process.Modules)) {
            if (
                $module.ModuleName -match '(?i)(?:windhawk|jarvis)' -or
                $module.FileName -match '(?i)(?:windhawk|jarvis)'
            ) {
                $explorerMappings.Add([pscustomobject]@{
                    processId = $process.Id
                    module = $module.ModuleName
                    path = $module.FileName
                })
            }
        }
    }
    catch {
        $moduleInspectionErrors.Add(
            "explorer-$($process.Id):$($_.Exception.GetType().Name)"
        )
    }
}
if (
    $explorerMappings.Count -ne 0 -or
    $moduleInspectionErrors.Count -ne 0
) {
    $blockedReasons.Add('explorer-module-baseline-not-cleanly-observed')
}

$recoveryLeaseExists =
    Test-Path -LiteralPath $recoveryLeasePath -PathType Leaf
$recoveryLeaseIdentity = $null
if ($recoveryLeaseExists) {
    try {
        $recoveryLeaseIdentity = [pscustomobject]@{
            path = $recoveryLeasePath
            size = (Get-Item -LiteralPath $recoveryLeasePath).Length
            sha256 = (
                Get-FileHash `
                    -LiteralPath $recoveryLeasePath `
                    -Algorithm SHA256
            ).Hash
        }
    }
    catch {
        $recoveryLeaseIdentity = [pscustomobject]@{
            path = $recoveryLeasePath
            error = $_.Exception.Message
        }
    }
}

# These are intentionally terminal blockers in Phase 17. The review callback
# is compiled only as an unlinked object, and there is no controller endpoint
# that can connect it to Explorer.
$blockedReasons.Add('exact-c-drive-window-identity-not-bound')
$blockedReasons.Add('visual-tree-generation-not-observed')
$blockedReasons.Add('existing-xaml-diagnostics-consumer-not-inspected')
$blockedReasons.Add('recovery-terminal-not-plan-bound-and-confirmed')
$blockedReasons.Add('surface-discovery-callback-unlinked')
$blockedReasons.Add('controller-remains-describe-only')

$sourceIdentity = [ordered]@{
    contract = Get-Identity -Path $contractPath
    discoveryHeader = Get-Identity -Path (
        Join-Path $sourceRoot 'jarvis_explorer_tap_surface_discovery.h'
    )
    discoveryCore = Get-Identity -Path (
        Join-Path $sourceRoot 'jarvis_explorer_tap_surface_discovery.cpp'
    )
    discoveryWindowsReview = Get-Identity -Path (
        Join-Path (
            $sourceRoot
        ) 'jarvis_explorer_tap_surface_discovery_windows.cpp'
    )
}

$receipt = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-xaml-read-review-package'
    result = 'passed-read-only-blocked-for-connection'
    observedAtUtc = $observedAtUtc.ToString(
        'O',
        [Globalization.CultureInfo]::InvariantCulture
    )
    freshHost = [ordered]@{
        compatibilityPassed = $compatibilityPassed
        compatibilityError = $compatibilityError
        profileId = if ($null -ne $compatibility) {
            $compatibility.profileId
        } else {
            $null
        }
        compatibilityCheckCount = if ($null -ne $compatibility) {
            @($compatibility.checks).Count
        } else {
            0
        }
        explorerProcessIds = if ($null -ne $compatibility) {
            @($compatibility.host.explorerProcessIds)
        } else {
            @()
        }
        killSwitchPath = $killSwitchPath
        killSwitchArmed = $killSwitchArmed
        activeModulePermitPath = $permitPath
        activeModulePermitPresent = $permitPresent
        windhawkService = if ($service.Count -eq 1) {
            [ordered]@{
                state = $service[0].State
                startMode = $service[0].StartMode
                processId = [uint64]$service[0].ProcessId
            }
        } else {
            $null
        }
        explorerMappings = $explorerMappings
        moduleInspectionErrors = $moduleInspectionErrors
    }
    recovery = [ordered]@{
        leaseExists = $recoveryLeaseExists
        leaseIdentity = $recoveryLeaseIdentity
        ready = $false
        planBound = $false
    }
    sourceIdentity = $sourceIdentity
    reviewBoundary = [ordered]@{
        callbackReviewObjectCompileEvidenceBound = $false
        callbackReviewObjectLinked = $false
        callbackReviewObjectExecuted = $false
        controllerEndpointAvailable = $false
        exactWindowBound = $false
        existingConsumerInspectionCompleted = $false
        visualTreeGenerationObserved = $false
    }
    blockedReasons = @($blockedReasons)
    exactCommand = $null
    exactCommandGenerated = $false
    readyForLiveConnection = $false
    readyForExactApproval = $false
    propertyReadAttempted = $false
    propertyWriteSupported = $false
    executionSupported = $false
    activationPermitted = $false
    liveExplorer = 'read-only-host-inspection'
    mutationPerformed = $false
    windhawkStarted = $false
    explorerRestartRequested = $false
    processTerminationRequested = $false
    registryWriteRequested = $false
    systemFileWriteRequested = $false
}

$resolvedOutput = Resolve-SafeOutputPath -RequestedPath $OutputPath
[IO.Directory]::CreateDirectory(
    (Split-Path -Parent $resolvedOutput)
) | Out-Null
$json = $receipt | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText(
    $resolvedOutput,
    $json + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false)
)
$receipt.outputPath = Get-RelativePath -Path $resolvedOutput
$receipt.outputSha256 = (
    Get-FileHash -LiteralPath $resolvedOutput -Algorithm SHA256
).Hash
$receipt | ConvertTo-Json -Depth 20
