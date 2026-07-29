[CmdletBinding()]
param(
    [string]$ToolCache = (
        Join-Path $env:LOCALAPPDATA 'JARVIS2\tool-cache\windhawk-1.7.3'
    ),
    [switch]$StaticOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerTapReadOnly'
$transportRoot = Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerTransportModel'
$headerPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_surface_discovery.h'
$corePath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_surface_discovery.cpp'
$windowsPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_surface_discovery_windows.cpp'
$harnessPath = Join-Path (
    $root
) 'tests\native\windows11\jarvis_explorer_tap_surface_discovery_harness.cpp'
$contractPath = Join-Path (
    $root
) 'config\explorer-xaml-surface-discovery-contract.json'
$schemaPath = Join-Path (
    $root
) 'config\explorer-xaml-surface-discovery-contract.schema.json'
$candidatePath = Join-Path (
    $root
) 'config\explorer-frame-selector-candidate.json'
$taskPath = Join-Path (
    $root
) 'docs\PHASE-17-EXPLORER-XAML-SURFACE-DISCOVERY-TASK.md'
$packageScriptPath = Join-Path (
    $root
) 'scripts\New-ExplorerXamlReadReviewPackage.ps1'
$tapPath = Join-Path $sourceRoot 'jarvis_explorer_tap_readonly.cpp'
$tapAuditPath = Join-Path $root 'scripts\Test-ExplorerReadOnlyTap.ps1'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ("jarvis2-xaml-surface-discovery-" + [Guid]::NewGuid().ToString('N'))

$checks = [Collections.Generic.List[object]]::new()
function Add-Check {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [bool]$Passed,
        [Parameter(Mandatory)]
        [string]$Detail
    )
    $checks.Add([pscustomobject]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    })
}

$requiredPaths = @(
    $headerPath,
    $corePath,
    $windowsPath,
    $harnessPath,
    $contractPath,
    $schemaPath,
    $candidatePath,
    $taskPath,
    $packageScriptPath
)
Add-Check `
    -Name 'files.phase17-review-boundary-present' `
    -Passed (
        @(
            $requiredPaths |
                Where-Object {
                    -not (Test-Path -LiteralPath $_ -PathType Leaf)
                }
        ).Count -eq 0
    ) `
    -Detail (
        'The discovery core, callback review object, harness, contract, ' +
        'task and read-only host package generator must all be present.'
    )

$header = Get-Content -LiteralPath $headerPath -Raw
$core = Get-Content -LiteralPath $corePath -Raw
$windows = Get-Content -LiteralPath $windowsPath -Raw
$harness = Get-Content -LiteralPath $harnessPath -Raw
$contract = Get-Content -LiteralPath $contractPath -Raw |
    ConvertFrom-Json -Depth 100
$schema = Get-Content -LiteralPath $schemaPath -Raw |
    ConvertFrom-Json -Depth 100
$candidate = Get-Content -LiteralPath $candidatePath -Raw |
    ConvertFrom-Json -Depth 100
$task = Get-Content -LiteralPath $taskPath -Raw
$packageScript = Get-Content -LiteralPath $packageScriptPath -Raw
$tap = Get-Content -LiteralPath $tapPath -Raw
$tapAudit = Get-Content -LiteralPath $tapAuditPath -Raw

$contractShape =
    $contract.schemaVersion -eq 1 -and
    $contract.contractId -eq
        'jarvis-explorer-xaml-surface-discovery-review-v1' -and
    $contract.lifecycleState -eq
        'offline-core-and-unlinked-callback-review-object' -and
    $schema.additionalProperties -eq $false -and
    $schema.properties.contractId.const -eq
        'jarvis-explorer-xaml-surface-discovery-review-v1' -and
    $schema.properties.activationPermitted.const -eq $false
Add-Check `
    -Name 'contract.strict-unlinked-review-schema' `
    -Passed $contractShape `
    -Detail (
        'The strict schema must describe one offline core and one unlinked, ' +
        'non-authorizing callback review object.'
    )

$candidateSelectors = @(
    $candidate.surfaces |
        ForEach-Object {
            $bytes = [Text.Encoding]::UTF8.GetBytes([string]$_.selector)
            [pscustomobject]@{
                role = $_.role
                selector = $_.selector
                sha256 = [Convert]::ToHexString(
                    [Security.Cryptography.SHA256]::HashData($bytes)
                )
                requiredMatchCount = [int]$_.expectedMatchCount
            }
        }
)
$contractSelectors = @($contract.selectors)
$selectorContract =
    $contractSelectors.Count -eq 3 -and
    $candidateSelectors.Count -eq 3
if ($selectorContract) {
    for ($index = 0; $index -lt 3; ++$index) {
        $selectorContract =
            $selectorContract -and
            $contractSelectors[$index].surfaceSlot -eq $index -and
            $contractSelectors[$index].role -eq
                $candidateSelectors[$index].role -and
            $contractSelectors[$index].selector -eq
                $candidateSelectors[$index].selector -and
            $contractSelectors[$index].sha256 -eq
                $candidateSelectors[$index].sha256 -and
            $contractSelectors[$index].requiredMatchCount -eq 1
    }
}
Add-Check `
    -Name 'selectors.exact-candidate-order-and-hashes' `
    -Passed $selectorContract `
    -Detail (
        'The three slots must exactly bind the reviewed candidate selectors, ' +
        'their UTF-8 SHA-256 values and one required match each.'
    )

$boundedModel =
    $contract.boundedModel.maximumNodeCount -eq 512 -and
    $contract.boundedModel.maximumEventCount -eq 2048 -and
    $contract.boundedModel.maximumAncestorDepth -eq 64 -and
    $contract.boundedModel.fixedCapacity -and
    -not $contract.boundedModel.heapAllocationRequired -and
    $header.Contains(
        'JARVIS_TAP_DISCOVERY_MAX_NODE_COUNT = 512U') -and
    $header.Contains(
        'JARVIS_TAP_DISCOVERY_MAX_EVENT_COUNT = 2048U') -and
    $header.Contains(
        'JARVIS_TAP_DISCOVERY_MAX_DEPTH = 64U') -and
    $header.Contains(
        'nodes[') -and
    -not [regex]::IsMatch(
        $core,
        '(?i)\b(?:new|malloc|calloc|realloc|vector|unordered_map)\b')
Add-Check `
    -Name 'model.fixed-capacity-no-heap' `
    -Passed $boundedModel `
    -Detail (
        'Discovery must remain a 512-node, 2,048-event, 64-depth fixed ' +
        'capacity model with no dynamic allocation.'
    )

$failClosedModel =
    $core.Contains(
        'JARVIS_TAP_DISCOVERY_RESULT_SEQUENCE_INVALID') -and
    $core.Contains(
        'JARVIS_TAP_DISCOVERY_RESULT_HANDLE_REPLAY') -and
    $core.Contains(
        'JARVIS_TAP_DISCOVERY_RESULT_REMOVE_UNKNOWN') -and
    $core.Contains(
        'JARVIS_TAP_DISCOVERY_RESULT_ORPHAN') -and
    $core.Contains(
        'JARVIS_TAP_DISCOVERY_RESULT_CYCLE') -and
    $core.Contains(
        'JARVIS_TAP_DISCOVERY_RESULT_DEPTH_EXCEEDED') -and
    $core.Contains(
        'JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE') -and
    $core.Contains(
        'JARVIS_TAP_DISCOVERY_RESULT_SURFACE_COLLISION') -and
    $core.Contains(
        'instance->state = JARVIS_TAP_DISCOVERY_STATE_BLOCKED;')
Add-Check `
    -Name 'model.terminal-fail-closed-matrix' `
    -Passed $failClosedModel `
    -Detail (
        'Malformed topology, replay, non-uniqueness and capacity drift must ' +
        'all transition the one-shot discovery instance to Blocked.'
    )

$readSession =
    $contract.readSession.requiredSurfaceCount -eq 3 -and
    $contract.readSession.requiredPropertyCount -eq 3 -and
    $contract.readSession.requestCount -eq 9 -and
    $contract.readSession.order -eq
        'surface-major-property-minor' -and
    $contract.readSession.feedsPhase16ReadRequest -and
    $core.Contains(
        'jarvis_tap_surface_discovery_build_read_request(') -and
    $core.Contains(
        'read_slot / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT') -and
    $core.Contains(
        'read_slot % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT')
Add-Check `
    -Name 'session.exact-nine-phase16-read-requests' `
    -Passed $readSession `
    -Detail (
        'A complete discovery must yield exactly nine Phase 16 requests in ' +
        'surface-major/property-minor order without performing a read.'
    )

$callbackBoundary =
    $contract.callbackReview.macro -eq
        'JARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK' -and
    $contract.callbackReview.reviewObjectValue -eq 1 -and
    $contract.callbackReview.shippingTapValue -eq 0 -and
    $contract.callbackReview.interface -eq
        'IVisualTreeServiceCallback2' -and
    -not $contract.callbackReview.linkedIntoTap -and
    -not $contract.callbackReview.executed -and
    -not $contract.callbackReview.subscriptionAttempted -and
    $contract.callbackReview.tapSetSiteResult -eq 'E_ACCESSDENIED' -and
    $windows.Contains('IVisualTreeServiceCallback2') -and
    $windows.Contains('OnVisualTreeChange(') -and
    $windows.Contains('OnElementStateChanged(') -and
    $windows.Contains('ParentChildRelation') -and
    $windows.Contains('VisualElement')
Add-Check `
    -Name 'callback.real-interface-review-object' `
    -Passed $callbackBoundary `
    -Detail (
        'The separately compiled object must implement the real callback ' +
        'shape while remaining unlinked, unexecuted and unsubscribed.'
    )

$forbiddenCallbackTokens = @(
    'InitializeXamlDiagnosticsEx(',
    'AdviseVisualTreeChange(',
    'UnadviseVisualTreeChange(',
    'GetIInspectableFromHandle(',
    'GetProperty(',
    'GetPropertyValuesChain(',
    'SetProperty(',
    'ClearProperty(',
    'StartService(',
    'TerminateProcess(',
    'RegSetValue'
)
$foundForbiddenCallbackTokens = @(
    $forbiddenCallbackTokens |
        Where-Object { $windows.Contains($_) }
)
Add-Check `
    -Name 'callback.no-connect-read-write-or-system-mutation' `
    -Passed ($foundForbiddenCallbackTokens.Count -eq 0) `
    -Detail (
        'Forbidden callback-review calls: ' +
        (($foundForbiddenCallbackTokens -join ', ') ?? '<none>')
    )

$tapStillLocked =
    $tap.Contains('return E_ACCESSDENIED;') -and
    -not $tapAudit.Contains(
        'jarvis_explorer_tap_surface_discovery.cpp') -and
    -not $tapAudit.Contains(
        'jarvis_explorer_tap_surface_discovery_windows.cpp')
Add-Check `
    -Name 'integration.shipping-tap-still-locked-and-unlinked' `
    -Passed $tapStillLocked `
    -Detail (
        'The existing TAP must still refuse SetSite and its build list must ' +
        'exclude both Phase 17 discovery sources.'
    )

$packageBoundary =
    $contract.hostReviewPackage.script -eq
        'scripts/New-ExplorerXamlReadReviewPackage.ps1' -and
    @($contract.hostReviewPackage.readOnlyChecks).Count -eq 5 -and
    @($contract.hostReviewPackage.terminalBlockers).Count -eq 6 -and
    -not $contract.hostReviewPackage.exactCommandGenerated -and
    $packageScript.Contains(
        "'passed-read-only-blocked-for-connection'") -and
    $packageScript.Contains('exactCommand = $null') -and
    $packageScript.Contains('exactCommandGenerated = $false') -and
    $packageScript.Contains('readyForExactApproval = $false') -and
    $packageScript.Contains('activationPermitted = $false') -and
    $packageScript.Contains(
        "'surface-discovery-callback-unlinked'") -and
    $packageScript.Contains(
        "'controller-remains-describe-only'")
$packageForbiddenPattern =
    '(?i)\b(?:Start-Service|Stop-Service|Set-Service|Restart-Service|' +
    'Start-Process|Stop-Process|Remove-Item|Move-Item|Set-ItemProperty|' +
    'New-ItemProperty|Restart-Computer|Stop-Computer|shutdown\.exe|' +
    'taskkill\.exe|reg\.exe)\b'
Add-Check `
    -Name 'host-package.read-only-and-command-blocked' `
    -Passed (
        $packageBoundary -and
        -not [regex]::IsMatch(
            $packageScript,
            $packageForbiddenPattern)
    ) `
    -Detail (
        'The host package may inspect and write only its repository receipt; ' +
        'it must never mutate services/processes/system state or emit a live command.'
    )

$claimsLocked =
    $task.Contains(
        'BOUNDED DISCOVERY CORE COMPLETE — CALLBACK UNLINKED AND NOT RUN') -and
    $contract.surfaceDiscoveryModelSupported -and
    $contract.callbackReviewObjectCompiled -and
    -not $contract.callbackReviewObjectLinked -and
    -not $contract.callbackReviewObjectExecuted -and
    -not $contract.readyForLiveConnection -and
    -not $contract.readyForExactApproval -and
    -not $contract.executionSupported -and
    -not $contract.activationPermitted -and
    $contract.liveExplorer -eq 'not-run' -and
    -not $contract.mutationPerformed
Add-Check `
    -Name 'claims.offline-review-is-not-live-evidence' `
    -Passed $claimsLocked `
    -Detail (
        'Task and contract claims must keep callback compilation distinct ' +
        'from a live Explorer connection or exact approval.'
    )

$scenarioCount = 0
$scenarioPassedCount = 0
$harnessBuilt = $false
$windowsReviewObjectBuilt = $false
$disabledObjectBuilt = $false

if (-not $StaticOnly) {
    $compiler = Join-Path (
        Join-Path $ToolCache 'portable'
    ) 'Compiler\bin\clang++.exe'
    Add-Check `
        -Name 'toolchain.pinned-portable-compiler-available' `
        -Passed (Test-Path -LiteralPath $compiler -PathType Leaf) `
        -Detail "Required compiler: $compiler"

    if (Test-Path -LiteralPath $compiler -PathType Leaf) {
        [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
        try {
            $commonArguments = @(
                '-std=c++20',
                '-O2',
                '-Wall',
                '-Wextra',
                '-Wpedantic',
                '-Werror',
                '-Wconversion',
                '-Wsign-conversion',
                '-Wshadow',
                '-fno-color-diagnostics',
                '-target',
                'x86_64-w64-mingw32',
                '-I',
                $sourceRoot,
                '-I',
                $transportRoot
            )
            $harnessExecutable = Join-Path (
                $temporaryRoot
            ) 'surface-discovery-harness.exe'
            $harnessBuildOutput = @(
                & $compiler `
                    @commonArguments `
                    -static `
                    -DJARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK=1 `
                    $corePath `
                    $harnessPath `
                    -o $harnessExecutable 2>&1
            )
            $harnessBuildExitCode = $LASTEXITCODE
            $harnessBuilt =
                $harnessBuildExitCode -eq 0 -and
                (Test-Path `
                    -LiteralPath $harnessExecutable `
                    -PathType Leaf)
            Add-Check `
                -Name 'build.portable-discovery-harness-warning-free' `
                -Passed $harnessBuilt `
                -Detail (
                    "Compiler exit $harnessBuildExitCode. " +
                    (($harnessBuildOutput | Select-Object -Last 12) -join ' ')
                )

            $harnessReceipt = $null
            $harnessExitCode = -1
            if ($harnessBuilt) {
                $harnessOutput = @(& $harnessExecutable 2>&1)
                $harnessExitCode = $LASTEXITCODE
                try {
                    $harnessReceipt = (
                        $harnessOutput -join [Environment]::NewLine
                    ) | ConvertFrom-Json
                }
                catch {
                    $harnessReceipt = $null
                }
                if ($null -ne $harnessReceipt) {
                    $scenarioCount = [int]$harnessReceipt.scenarioCount
                    $scenarioPassedCount = [int]$harnessReceipt.passedCount
                }
            }
            Add-Check `
                -Name 'harness.bounded-discovery-fault-matrix' `
                -Passed (
                    $harnessExitCode -eq 0 -and
                    $null -ne $harnessReceipt -and
                    $harnessReceipt.result -eq 'passed' -and
                    $scenarioCount -eq 58 -and
                    $scenarioPassedCount -eq 58 -and
                    $harnessReceipt.syntheticVisualTreeEvents -and
                    -not $harnessReceipt.windowsCallbackExecuted -and
                    -not $harnessReceipt.callbackSubscriptionAttempted -and
                    -not $harnessReceipt.propertyReadAttempted -and
                    -not $harnessReceipt.propertyWriteSupported -and
                    -not $harnessReceipt.executionSupported -and
                    -not $harnessReceipt.readyForLiveConnection -and
                    -not $harnessReceipt.readyForExactApproval -and
                    -not $harnessReceipt.activationPermitted -and
                    $harnessReceipt.liveExplorer -eq 'not-run' -and
                    -not $harnessReceipt.mutationPerformed
                ) `
                -Detail (
                    "Harness exit $harnessExitCode; passed " +
                    "$scenarioPassedCount/$scenarioCount."
                )

            $windowsObject = Join-Path (
                $temporaryRoot
            ) 'surface-discovery-windows-review.o'
            $reviewBuildOutput = @(
                & $compiler `
                    @commonArguments `
                    -DJARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK=1 `
                    -c $windowsPath `
                    -o $windowsObject 2>&1
            )
            $reviewBuildExitCode = $LASTEXITCODE
            $windowsReviewObjectBuilt =
                $reviewBuildExitCode -eq 0 -and
                (Test-Path -LiteralPath $windowsObject -PathType Leaf)
            Add-Check `
                -Name 'build.windows-callback-review-object-warning-free' `
                -Passed $windowsReviewObjectBuilt `
                -Detail (
                    "Compiler exit $reviewBuildExitCode. " +
                    (($reviewBuildOutput | Select-Object -Last 12) -join ' ')
                )

            $disabledObject = Join-Path (
                $temporaryRoot
            ) 'surface-discovery-windows-disabled.o'
            $disabledBuildOutput = @(
                & $compiler `
                    @commonArguments `
                    -DJARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK=0 `
                    -c $windowsPath `
                    -o $disabledObject 2>&1
            )
            $disabledBuildExitCode = $LASTEXITCODE
            $disabledObjectBuilt =
                $disabledBuildExitCode -eq 0 -and
                (Test-Path -LiteralPath $disabledObject -PathType Leaf)
            Add-Check `
                -Name 'build.default-disabled-object-warning-free' `
                -Passed $disabledObjectBuilt `
                -Detail (
                    "Compiler exit $disabledBuildExitCode. " +
                    (($disabledBuildOutput | Select-Object -Last 12) -join ' ')
                )
        }
        finally {
            if (Test-Path -LiteralPath $temporaryRoot) {
                Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
            }
        }
    }
    else {
        Add-Check `
            -Name 'build.portable-discovery-harness-warning-free' `
            -Passed $false `
            -Detail 'The pinned portable compiler is unavailable.'
        Add-Check `
            -Name 'harness.bounded-discovery-fault-matrix' `
            -Passed $false `
            -Detail 'The portable discovery harness could not be built.'
        Add-Check `
            -Name 'build.windows-callback-review-object-warning-free' `
            -Passed $false `
            -Detail 'The real callback review object could not be built.'
        Add-Check `
            -Name 'build.default-disabled-object-warning-free' `
            -Passed $false `
            -Detail 'The default-disabled callback object could not be built.'
    }
}

$passed = @($checks | Where-Object passed).Count
$result = if ($passed -eq $checks.Count) { 'passed' } else { 'failed' }
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-xaml-surface-discovery-review-audit'
    result = $result
    checkCount = $checks.Count
    passedCount = $passed
    scenarioCount = $scenarioCount
    scenarioPassedCount = $scenarioPassedCount
    harnessBuilt = $harnessBuilt
    windowsReviewObjectBuilt = $windowsReviewObjectBuilt
    windowsCallbackExecuted = $false
    disabledObjectBuilt = $disabledObjectBuilt
    hostReviewPackageExecuted = $false
    callbackSubscriptionAttempted = $false
    propertyReadAttempted = $false
    propertyWriteSupported = $false
    executionSupported = $false
    readyForLiveConnection = $false
    readyForExactApproval = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
} | ConvertTo-Json -Depth 20

if ($result -ne 'passed') {
    exit 1
}
