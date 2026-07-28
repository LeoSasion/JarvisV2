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
$sourceRoot = Join-Path $root 'src\Jarvis.ExplorerTapReadOnly'
$transportRoot = Join-Path $root 'src\Jarvis.ExplorerTransportModel'
$protocolPath = Join-Path $sourceRoot 'jarvis_explorer_tap_protocol.cpp'
$admissionPath = Join-Path $sourceRoot 'jarvis_explorer_tap_admission.cpp'
$fingerprintPath = Join-Path $sourceRoot 'jarvis_explorer_tap_fingerprint.cpp'
$adapterPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_inspectable_adapter.cpp'
$transactionHeaderPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_style_transaction.h'
$transactionPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_style_transaction.cpp'
$controllerPath = Join-Path $sourceRoot 'jarvis_explorer_tap_controller.cpp'
$tapPath = Join-Path $sourceRoot 'jarvis_explorer_tap_readonly.cpp'
$harnessPath = Join-Path (
    $root
) 'tests\native\jarvis_explorer_tap_style_transaction_harness.cpp'
$contractPath = Join-Path (
    $root
) 'config\explorer-style-transaction-contract.json'
$schemaPath = Join-Path (
    $root
) 'config\explorer-style-transaction-contract.schema.json'
$taskPath = Join-Path (
    $root
) 'docs\PHASE-15-EXPLORER-REVERSIBLE-STYLE-TRANSACTION-TASK.md'
$tapAuditPath = Join-Path $root 'scripts\Test-ExplorerReadOnlyTap.ps1'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ("jarvis2-explorer-style-transaction-" + [Guid]::NewGuid().ToString('N'))

$checks = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [bool]$Passed,
        [Parameter(Mandatory)] [string]$Detail
    )
    $checks.Add([pscustomobject]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    })
    if (-not $Passed) {
        $failures.Add("${Name}: ${Detail}")
    }
}

$headerText = [IO.File]::ReadAllText($transactionHeaderPath)
$transactionText = [IO.File]::ReadAllText($transactionPath)
$controllerText = [IO.File]::ReadAllText($controllerPath)
$tapText = [IO.File]::ReadAllText($tapPath)
$harnessText = [IO.File]::ReadAllText($harnessPath)
$tapAuditText = [IO.File]::ReadAllText($tapAuditPath)
$taskText = [IO.File]::ReadAllText($taskPath)
$contractText = [IO.File]::ReadAllText($contractPath)
$schemaText = [IO.File]::ReadAllText($schemaPath)
$contract = $contractText | ConvertFrom-Json -Depth 100
$schema = $schemaText | ConvertFrom-Json -Depth 100
$modelSource = @($headerText, $transactionText, $harnessText) -join "`n"

Add-Check `
    -Name 'machine-contract.offline-reversible-only' `
    -Passed (
        $contract.schemaVersion -eq 1 -and
        $contract.contractId -eq
            'jarvis-explorer-style-transaction-v1' -and
        $contract.lifecycleState -eq
            'offline-reversible-transaction-model-only' -and
        $contract.compileGate.requiredValue -eq 0 -and
        -not $contract.compileGate.livePropertyWriteCompiled -and
        $contract.prepare.originalValueCountRequired -eq 9 -and
        $contract.prepare.styledValueCountRequired -eq 9 -and
        $contract.prepare.previewDurationMilliseconds -eq 60000 -and
        $contract.apply.writeAttemptSetsDirtyBeforeResult -and
        $contract.restore.order -eq 'strict-reverse-last-dirty-first' -and
        $contract.restore.restoredRequiresDirtyMaskZero -and
        -not $contract.propertyReadSupported -and
        -not $contract.propertyWriteSupported -and
        -not $contract.executionSupported -and
        -not $contract.readyForLiveConnection -and
        -not $contract.readyForExactApproval -and
        -not $contract.activationPermitted -and
        $contract.liveExplorer -eq 'not-run' -and
        -not $contract.mutationPerformed -and
        $schema.'$schema' -eq
            'https://json-schema.org/draft/2020-12/schema' -and
        $schema.additionalProperties -eq $false
    ) `
    -Detail 'The machine contract must freeze a non-live reversible model.'

Add-Check `
    -Name 'transaction.fixed-width-bounded-journal' `
    -Passed (
        $headerText.Contains(
            'static_assert(sizeof(jarvis_tap_style_plan_request) == 784U)'
        ) -and
        $headerText.Contains(
            'static_assert(sizeof(jarvis_tap_style_transaction_instance) == 1072U)'
        ) -and
        [regex]::IsMatch(
            $headerText,
            'original_values\s*\[\s*JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT\s*\]'
        ) -and
        [regex]::IsMatch(
            $headerText,
            'styled_values\s*\[\s*JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT\s*\]'
        )
    ) `
    -Detail 'The transaction must contain exactly nine originals and styles.'

Add-Check `
    -Name 'transaction.live-write-compile-gate-hard-disabled' `
    -Passed (
        $headerText.Contains(
            '#define JARVIS_ENABLE_LIVE_XAML_PROPERTY_WRITE 0'
        ) -and
        $headerText.Contains(
            '#if JARVIS_ENABLE_LIVE_XAML_PROPERTY_WRITE != 0'
        ) -and
        $headerText.Contains(
            '#error Phase 15 must be compiled with live XAML property writes disabled.'
        )
    ) `
    -Detail 'Any nonzero property-write compile gate must fail compilation.'

Add-Check `
    -Name 'prepare.complete-snapshot-hash-and-deadline' `
    -Passed (
        $transactionText.Contains(
            'adapter->canonical_value_count !='
        ) -and
        $transactionText.Contains(
            'std::memcmp('
        ) -and
        $transactionText.Contains(
            'jarvis_tap_fingerprint_compute_canonical('
        ) -and
        $transactionText.Contains(
            'JARVIS_TAP_STYLE_TRANSACTION_RESULT_STYLED_HASH_MISMATCH'
        ) -and
        $transactionText.Contains(
            'deadline > request->bind.expires_at_monotonic_ms'
        ) -and
        $transactionText.Contains(
            'instance->original_values[index] ='
        )
    ) `
    -Detail 'All originals and exact styled hashes must be frozen before apply.'

Add-Check `
    -Name 'apply.attempt-dirties-before-result-and-verification' `
    -Passed (
        $transactionText.Contains(
            'instance->dirty_mask |= 1U << index'
        ) -and
        $transactionText.Contains(
            '++instance->simulated_write_attempt_count'
        ) -and
        $transactionText.Contains(
            'JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_FAILED'
        ) -and
        $transactionText.Contains(
            'JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_MISMATCH'
        ) -and
        $transactionText.Contains(
            'JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED'
        )
    ) `
    -Detail 'Every reported write attempt must become dirty before result trust.'

Add-Check `
    -Name 'restore.strict-reverse-retry-until-clean' `
    -Passed (
        $transactionText.Contains('HighestDirtyIndex(') -and
        $transactionText.Contains(
            'JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORE_ORDER_INVALID'
        ) -and
        $transactionText.Contains(
            'instance->dirty_mask &= ~(1U << index)'
        ) -and
        $transactionText.Contains(
            'if (instance->dirty_mask == 0U)'
        ) -and
        $transactionText.Contains(
            'JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORED'
        )
    ) `
    -Detail 'Only verified reverse restoration may clear dirty bits.'

Add-Check `
    -Name 'deadline.clean-quiesce-dirty-restore-required' `
    -Passed (
        $transactionText.Contains(
            'now_monotonic_ms <'
        ) -and
        $transactionText.Contains(
            'instance->preview_deadline_monotonic_ms'
        ) -and
        $transactionText.Contains(
            'JARVIS_TAP_STYLE_TRANSACTION_STATE_QUIESCED'
        ) -and
        $transactionText.Contains(
            'JARVIS_TAP_STYLE_TRANSACTION_RESULT_TIMEOUT'
        )
    ) `
    -Detail 'Deadline handling must distinguish clean quiesce from dirty restore.'

$forbiddenPattern = (
    '(?i)\b(?:windows\.h|xamlom\.h|roapi\.h|wrl\.h|' +
    'IXamlDiagnostics|IVisualTreeService|DependencyObject|DependencyProperty|' +
    'ReadLocalValue|LoadLibrary|GetProcAddress|OpenProcess|EnumWindows|' +
    'SetValue|ClearValue|SetProperty|InitializeXamlDiagnosticsEx|' +
    'StartService|RegSetValue)\b'
)
Add-Check `
    -Name 'transaction.no-windows-com-xaml-read-or-write-api' `
    -Passed (-not [regex]::IsMatch($modelSource, $forbiddenPattern)) `
    -Detail 'The model may contain no live COM, XAML, loader or mutation API.'

Add-Check `
    -Name 'integration.linked-unreachable-unexported' `
    -Passed (
        $controllerText.Contains(
            '\"offlineStyleTransactionModelSupported\":true'
        ) -and
        $controllerText.Contains('\"propertyWriteSupported\":false') -and
        $tapText.Contains('return E_ACCESSDENIED;') -and
        $tapAuditText.Contains('$transactionPath') -and
        -not $controllerText.Contains(
            'jarvis_tap_style_transaction_prepare('
        ) -and
        -not $tapText.Contains(
            'jarvis_tap_style_transaction_prepare('
        )
    ) `
    -Detail 'Disk binaries may contain the model but no entry point may reach it.'

Add-Check `
    -Name 'docs.no-platform-write-or-live-claim' `
    -Passed (
        $taskText.Contains(
            'OFFLINE TRANSACTION MODEL COMPLETE — NO PLATFORM WRITE'
        ) -and
        $taskText.Contains(
            'The model never interprets a failed API result as proof that no mutation'
        ) -and
        $taskText.Contains(
            'Phase 15 grants no permission to connect, read or write Explorer'
        )
    ) `
    -Detail 'Documentation must separate simulated attempts from platform writes.'

$scenarioCount = 0
$scenarioPassedCount = 0
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
            $executablePath = Join-Path (
                $temporaryRoot
            ) 'style-transaction-harness.exe'
            $buildOutput = @(
                & $compiler `
                    -std=c++20 `
                    -O2 `
                    -Wall `
                    -Wextra `
                    -Wpedantic `
                    -Werror `
                    -Wconversion `
                    -Wsign-conversion `
                    -Wshadow `
                    -fno-color-diagnostics `
                    -static `
                    -target x86_64-w64-mingw32 `
                    -I $sourceRoot `
                    -I $transportRoot `
                    $protocolPath `
                    $admissionPath `
                    $fingerprintPath `
                    $adapterPath `
                    $transactionPath `
                    $harnessPath `
                    -o $executablePath 2>&1
            )
            $buildExitCode = $LASTEXITCODE
            Add-Check `
                -Name 'build.portable-style-transaction-harness' `
                -Passed (
                    $buildExitCode -eq 0 -and
                    (Test-Path -LiteralPath $executablePath -PathType Leaf)
                ) `
                -Detail (
                    "Compiler exit $buildExitCode. " +
                    (($buildOutput | Select-Object -Last 16) -join ' ')
                )
            if ($buildExitCode -eq 0) {
                $harnessOutput = @(& $executablePath 2>&1)
                $harnessExitCode = $LASTEXITCODE
                try {
                    $receipt = (
                        $harnessOutput -join [Environment]::NewLine
                    ) | ConvertFrom-Json
                }
                catch {
                    $receipt = $null
                }
                if ($null -ne $receipt) {
                    $scenarioCount = [int]$receipt.scenarioCount
                    $scenarioPassedCount = [int]$receipt.passedCount
                }
                Add-Check `
                    -Name 'harness.reversible-transaction-fault-matrix' `
                    -Passed (
                        $harnessExitCode -eq 0 -and
                        $null -ne $receipt -and
                        $receipt.result -eq 'passed' -and
                        $scenarioCount -eq 65 -and
                        $scenarioPassedCount -eq 65 -and
                        $receipt.simulatedWriteAttempts -and
                        -not $receipt.platformWriteAttempted -and
                        -not $receipt.propertyWriteSupported -and
                        -not $receipt.propertyReadSupported -and
                        -not $receipt.endpointAttempted -and
                        -not $receipt.tapDllLoaded -and
                        -not $receipt.executionSupported -and
                        -not $receipt.activationPermitted -and
                        $receipt.liveExplorer -eq 'not-run' -and
                        -not $receipt.mutationPerformed
                    ) `
                    -Detail (
                        "Harness exit $harnessExitCode; passed " +
                        "$scenarioPassedCount/$scenarioCount."
                    )
            }
        }
        finally {
            $resolvedRoot = [IO.Path]::GetFullPath($temporaryRoot)
            $resolvedTemp = [IO.Path]::GetFullPath(
                [IO.Path]::GetTempPath()
            ).TrimEnd('\') + '\'
            if (
                $resolvedRoot.StartsWith(
                    $resolvedTemp,
                    [StringComparison]::OrdinalIgnoreCase
                ) -and
                [IO.Path]::GetFileName($resolvedRoot).StartsWith(
                    'jarvis2-explorer-style-transaction-',
                    [StringComparison]::Ordinal
                )
            ) {
                Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
            }
            else {
                throw "Refusing to remove unexpected temp path: $temporaryRoot"
            }
        }
    }
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-style-transaction-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scenarioCount = $scenarioCount
    scenarioPassedCount = $scenarioPassedCount
    simulatedWriteAttempts = $true
    platformWriteAttempted = $false
    propertyWriteSupported = $false
    propertyReadSupported = $false
    endpointAttempted = $false
    tapDllLoaded = $false
    executionSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 8

if (-not $passed) {
    exit 1
}
