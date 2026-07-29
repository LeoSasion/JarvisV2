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
$headerPath = Join-Path $sourceRoot 'jarvis_explorer_tap_xaml_read_bridge.h'
$policyPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_xaml_read_bridge_policy.cpp'
$windowsPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_xaml_read_bridge_windows.cpp'
$harnessPath = Join-Path (
    $root
) 'tests\native\windows11\jarvis_explorer_tap_xaml_read_bridge_harness.cpp'
$taskPath = Join-Path (
    $root
) 'docs\PHASE-16-EXPLORER-XAML-READ-BRIDGE-REVIEW-TASK.md'
$contractPath = Join-Path (
    $root
) 'config\explorer-xaml-read-bridge-contract.json'
$schemaPath = Join-Path (
    $root
) 'config\explorer-xaml-read-bridge-contract.schema.json'
$tapPath = Join-Path $sourceRoot 'jarvis_explorer_tap_readonly.cpp'
$tapAuditPath = Join-Path $root 'scripts\Test-ExplorerReadOnlyTap.ps1'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ("jarvis2-xaml-read-bridge-" + [Guid]::NewGuid().ToString('N'))

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
    $policyPath,
    $windowsPath,
    $harnessPath,
    $taskPath,
    $contractPath,
    $schemaPath
)
Add-Check `
    -Name 'files.phase16-review-boundary-present' `
    -Passed (
        @(
            $requiredPaths |
                Where-Object {
                    -not (Test-Path -LiteralPath $_ -PathType Leaf)
                }
        ).Count -eq 0
    ) `
    -Detail 'The policy, real-interface review object, harness, task and machine contracts must all be present.'

$header = Get-Content -LiteralPath $headerPath -Raw
$policy = Get-Content -LiteralPath $policyPath -Raw
$windows = Get-Content -LiteralPath $windowsPath -Raw
$harness = Get-Content -LiteralPath $harnessPath -Raw
$task = Get-Content -LiteralPath $taskPath -Raw
$tap = Get-Content -LiteralPath $tapPath -Raw
$tapAudit = Get-Content -LiteralPath $tapAuditPath -Raw
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
$schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json

$contractShape =
    $contract.schemaVersion -eq 1 -and
    $contract.contractId -eq
        'jarvis-explorer-xaml-read-bridge-review-v1' -and
    $contract.lifecycleState -eq 'unlinked-review-object-only' -and
    $schema.additionalProperties -eq $false -and
    $schema.properties.contractId.const -eq
        'jarvis-explorer-xaml-read-bridge-review-v1' -and
    $schema.properties.activationPermitted.const -eq $false
Add-Check `
    -Name 'contract.strict-review-only-schema' `
    -Passed $contractShape `
    -Detail 'The strict schema must describe one unlinked, non-authorizing review object.'

$compileGate =
    $contract.compileGate.macro -eq
        'JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE' -and
    $contract.compileGate.reviewObjectValue -eq 1 -and
    $contract.compileGate.shippingTapValue -eq 0 -and
    -not $contract.compileGate.reviewObjectLinkedIntoTap -and
    $header.Contains(
        '#define JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE 0'
    ) -and
    $windows.Contains(
        '#if JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE == 1'
    )
Add-Check `
    -Name 'compile-gate.review-object-split' `
    -Passed $compileGate `
    -Detail 'Only a separately compiled review object may contain the real read branch; the default remains zero.'

$allowedCalls = @($contract.readBoundary.allowedCalls)
$readBoundary =
    $contract.readBoundary.siteInterface -eq 'IXamlDiagnostics' -and
    $contract.readBoundary.serviceInterface -eq
        'IVisualTreeService2' -and
    $allowedCalls -contains 'GetPropertyValuesChain' -and
    $allowedCalls -contains 'GetProperty' -and
    $allowedCalls -contains 'GetIInspectableFromHandle' -and
    $windows.Contains('service->GetPropertyValuesChain(') -and
    $windows.Contains('service->GetProperty(') -and
    $windows.Contains('diagnostics->GetIInspectableFromHandle(')
Add-Check `
    -Name 'source.real-read-interfaces-only' `
    -Passed $readBoundary `
    -Detail 'The review source must compile the exact diagnostics, property-chain and inspectable read calls.'

$forbiddenTokens = @(
    'InitializeXamlDiagnosticsEx(',
    'CreateInstance(',
    'SetProperty(',
    'ClearProperty(',
    'ReplaceResource(',
    'AddChild(',
    'RemoveChild(',
    'ClearChildren('
)
$foundForbidden = @(
    $forbiddenTokens |
        Where-Object { $windows.Contains($_) }
)
Add-Check `
    -Name 'source.no-loader-or-mutation-call' `
    -Passed ($foundForbidden.Count -eq 0) `
    -Detail (
        'Forbidden tokens in the Windows source: ' +
        (($foundForbidden -join ', ') ?? '<none>')
    )

$projection =
    $contract.projection.outputSnapshotBytes -eq 192 -and
    $contract.projection.exactRuntimeClassNameRequired -and
    $contract.projection.maximumOpacityMillionths -eq 1000000 -and
    $contract.projection.feedsPhase14AdapterShape -and
    $contract.readBoundary.maximumPropertySourceCount -eq 128 -and
    $contract.readBoundary.maximumPropertyValueCount -eq 512 -and
    $header.Contains(
        'static_assert(sizeof(jarvis_tap_xaml_read_response) == 264U)'
    ) -and
    $policy.Contains('BaseValueSourceLocal') -eq $false -and
    $policy.Contains('observation->property_value_source != 4U')
Add-Check `
    -Name 'projection.bounded-local-phase14-shape' `
    -Passed $projection `
    -Detail 'Only bounded local null or exact SolidColorBrush observations may become a Phase 14 snapshot.'

$ownership =
    $contract.ownership.querySuccessWithNullOutputRejected -and
    $contract.ownership.queryFailureWithNonNullOutputRetainedAsUncertain -and
    $contract.ownership.propertyChainFreedOnlyAfterSuccessfulBoundedReturn -and
    $contract.ownership.releaseAttemptAndCompletionCountsMustMatch -and
    $windows.Contains('FreeConfirmedPropertyChain(') -and
    $windows.Contains('ReleaseConfirmed(') -and
    $windows.Contains('observation->foreign_outcome_uncertain = 1U;') -and
    $windows.Contains('*result = nullptr;') -and
    $windows.Contains('inspectable = nullptr;') -and
    $policy.Contains(
        'JARVIS_TAP_XAML_READ_RESULT_FOREIGN_OUTCOME_UNCERTAIN'
    ) -and
    $policy.Contains(
        'JARVIS_TAP_XAML_READ_RESULT_RELEASE_INCOMPLETE'
    )
Add-Check `
    -Name 'ownership.fail-closed-foreign-boundary' `
    -Passed $ownership `
    -Detail 'Partial foreign outputs, oversized arrays and incomplete releases must remain blocked.'

$integration =
    $contract.integration.portablePolicyHarnessScenarioCount -eq 56 -and
    $contract.integration.syntheticForeignObservationsOnly -and
    $contract.integration.windowsInterfaceReviewObjectCompiled -and
    -not $contract.integration.windowsInterfaceReviewObjectExecuted -and
    -not $contract.integration.windowsInterfaceReviewObjectLinked -and
    $contract.integration.tapSetSiteResult -eq 'E_ACCESSDENIED' -and
    $tap.Contains('return E_ACCESSDENIED;') -and
    -not $tapAudit.Contains(
        'jarvis_explorer_tap_xaml_read_bridge_windows.cpp'
    )
Add-Check `
    -Name 'integration.unlinked-existing-tap-still-locked' `
    -Passed $integration `
    -Detail 'The existing TAP build list must exclude the review object and SetSite must remain refused.'

$approvalBlocked =
    $contract.approval.status -eq
        'blocked-fresh-host-package-required' -and
    $contract.approval.scope -eq
        'one-exact-visible-c-drive-cabinet-window-read-only' -and
    $contract.approval.durationSeconds -eq 60 -and
    -not $contract.approval.exactCommandGenerated -and
    @($contract.approval.requiredFreshEvidence).Count -eq 8 -and
    @($contract.approval.forbiddenDuringReadValidation).Count -eq 6 -and
    -not $contract.readyForLiveConnection -and
    -not $contract.readyForExactApproval -and
    -not $contract.activationPermitted
Add-Check `
    -Name 'approval.fresh-host-package-still-required' `
    -Passed $approvalBlocked `
    -Detail 'No command may be generated until a fresh exact-window, recovery and compatibility package is reviewed.'

$nonLiveClaims =
    $task.Contains(
        'REAL INTERFACE REVIEW OBJECT COMPLETE — UNLINKED AND NOT RUN'
    ) -and
    -not $contract.propertyReadSupported -and
    -not $contract.propertyWriteSupported -and
    -not $contract.executionSupported -and
    $contract.liveExplorer -eq 'not-run' -and
    -not $contract.mutationPerformed -and
    $harness.Contains('windowsReviewObjectExecuted') -and
    $harness.Contains('liveExplorer') -and
    $harness.Contains('not-run')
Add-Check `
    -Name 'claims.validation-remains-non-live' `
    -Passed $nonLiveClaims `
    -Detail 'The task, contract and executable receipt must distinguish compile review from live Explorer evidence.'

$scenarioCount = 0
$scenarioPassedCount = 0
$policyHarnessBuilt = $false
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
            ) 'xaml-read-policy-harness.exe'
            $harnessBuildOutput = @(
                & $compiler `
                    @commonArguments `
                    -static `
                    -DJARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE=1 `
                    $policyPath `
                    $harnessPath `
                    -o $harnessExecutable 2>&1
            )
            $harnessBuildExitCode = $LASTEXITCODE
            $policyHarnessBuilt =
                $harnessBuildExitCode -eq 0 -and
                (Test-Path `
                    -LiteralPath $harnessExecutable `
                    -PathType Leaf)
            Add-Check `
                -Name 'build.portable-policy-harness-warning-free' `
                -Passed $policyHarnessBuilt `
                -Detail (
                    "Compiler exit $harnessBuildExitCode. " +
                    (($harnessBuildOutput | Select-Object -Last 12) -join ' ')
                )

            $harnessReceipt = $null
            $harnessExitCode = -1
            if ($policyHarnessBuilt) {
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
                -Name 'harness.synthetic-foreign-fault-matrix' `
                -Passed (
                    $harnessExitCode -eq 0 -and
                    $null -ne $harnessReceipt -and
                    $harnessReceipt.result -eq 'passed' -and
                    $scenarioCount -eq 56 -and
                    $scenarioPassedCount -eq 56 -and
                    $harnessReceipt.syntheticForeignObservations -and
                    -not $harnessReceipt.windowsReviewObjectExecuted -and
                    -not $harnessReceipt.propertyReadSupported -and
                    -not $harnessReceipt.endpointAttempted -and
                    -not $harnessReceipt.tapDllLoaded -and
                    -not $harnessReceipt.executionSupported -and
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
            ) 'windows-read-bridge-review.o'
            $reviewBuildOutput = @(
                & $compiler `
                    @commonArguments `
                    -DJARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE=1 `
                    -c $windowsPath `
                    -o $windowsObject 2>&1
            )
            $reviewBuildExitCode = $LASTEXITCODE
            $windowsReviewObjectBuilt =
                $reviewBuildExitCode -eq 0 -and
                (Test-Path -LiteralPath $windowsObject -PathType Leaf)
            Add-Check `
                -Name 'build.windows-interface-review-object-warning-free' `
                -Passed $windowsReviewObjectBuilt `
                -Detail (
                    "Compiler exit $reviewBuildExitCode. " +
                    (($reviewBuildOutput | Select-Object -Last 12) -join ' ')
                )

            $disabledObject = Join-Path (
                $temporaryRoot
            ) 'windows-read-bridge-disabled.o'
            $disabledBuildOutput = @(
                & $compiler `
                    @commonArguments `
                    -DJARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE=0 `
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
            -Name 'build.portable-policy-harness-warning-free' `
            -Passed $false `
            -Detail 'The pinned portable compiler is unavailable.'
        Add-Check `
            -Name 'harness.synthetic-foreign-fault-matrix' `
            -Passed $false `
            -Detail 'The policy harness could not be built.'
        Add-Check `
            -Name 'build.windows-interface-review-object-warning-free' `
            -Passed $false `
            -Detail 'The real-interface review object could not be built.'
        Add-Check `
            -Name 'build.default-disabled-object-warning-free' `
            -Passed $false `
            -Detail 'The default-disabled object could not be built.'
    }
}

$passed = @($checks | Where-Object passed).Count
$result = if ($passed -eq $checks.Count) { 'passed' } else { 'failed' }
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-xaml-read-bridge-review-audit'
    result = $result
    checkCount = $checks.Count
    passedCount = $passed
    scenarioCount = $scenarioCount
    scenarioPassedCount = $scenarioPassedCount
    policyHarnessBuilt = $policyHarnessBuilt
    windowsReviewObjectBuilt = $windowsReviewObjectBuilt
    windowsReviewObjectExecuted = $false
    disabledObjectBuilt = $disabledObjectBuilt
    endpointAttempted = $false
    tapDllLoaded = $false
    propertyReadSupported = $false
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
