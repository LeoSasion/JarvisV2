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
$protocolPath = Join-Path $sourceRoot 'jarvis_explorer_tap_protocol.cpp'
$admissionPath = Join-Path $sourceRoot 'jarvis_explorer_tap_admission.cpp'
$fingerprintPath = Join-Path $sourceRoot 'jarvis_explorer_tap_fingerprint.cpp'
$adapterHeaderPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_inspectable_adapter.h'
$adapterPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_tap_inspectable_adapter.cpp'
$controllerPath = Join-Path $sourceRoot 'jarvis_explorer_tap_controller.cpp'
$tapPath = Join-Path $sourceRoot 'jarvis_explorer_tap_readonly.cpp'
$harnessPath = Join-Path (
    $root
) 'tests\native\windows11\jarvis_explorer_tap_inspectable_adapter_harness.cpp'
$contractPath = Join-Path (
    $root
) 'config\explorer-inspectable-adapter-contract.json'
$schemaPath = Join-Path (
    $root
) 'config\explorer-inspectable-adapter-contract.schema.json'
$taskPath = Join-Path (
    $root
) 'docs\PHASE-14-EXPLORER-INSPECTABLE-ADAPTER-TASK.md'
$tapAuditPath = Join-Path $root 'scripts\Test-ExplorerReadOnlyTap.ps1'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ("jarvis2-explorer-inspectable-adapter-" + [Guid]::NewGuid().ToString('N'))

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

$headerText = [IO.File]::ReadAllText($adapterHeaderPath)
$adapterText = [IO.File]::ReadAllText($adapterPath)
$controllerText = [IO.File]::ReadAllText($controllerPath)
$tapText = [IO.File]::ReadAllText($tapPath)
$harnessText = [IO.File]::ReadAllText($harnessPath)
$tapAuditText = [IO.File]::ReadAllText($tapAuditPath)
$taskText = [IO.File]::ReadAllText($taskPath)
$contractText = [IO.File]::ReadAllText($contractPath)
$schemaText = [IO.File]::ReadAllText($schemaPath)
$contract = $contractText | ConvertFrom-Json -Depth 100
$schema = $schemaText | ConvertFrom-Json -Depth 100
$modelSource = @($headerText, $adapterText, $harnessText) -join "`n"

Add-Check `
    -Name 'machine-contract.offline-projection-only' `
    -Passed (
        $contract.schemaVersion -eq 1 -and
        $contract.contractId -eq
            'jarvis-explorer-inspectable-adapter-v1' -and
        $contract.lifecycleState -eq 'offline-projection-model-only' -and
        $contract.compileGate.requiredValue -eq 0 -and
        -not $contract.compileGate.livePropertyReadCompiled -and
        $contract.projection.snapshotBytes -eq 192 -and
        $contract.projection.acceptedValueOrigin -eq 'local' -and
        $contract.projection.exactRuntimeClassNameMatchRequiredForObject -and
        $contract.projection.unsupportedValuePolicy -eq
            'latch-adapter-and-fingerprint-blocked' -and
        -not $contract.propertyReadSupported -and
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
    -Detail 'The machine contract must freeze a non-live projection-only seam.'

Add-Check `
    -Name 'adapter.fixed-width-owned-fingerprint' `
    -Passed (
        $headerText.Contains(
            'static_assert(sizeof(jarvis_tap_runtime_property_snapshot) == 192U)'
        ) -and
        $headerText.Contains(
            'static_assert(sizeof(jarvis_tap_inspectable_adapter_instance) == 680U)'
        ) -and
        $headerText.Contains('jarvis_tap_fingerprint_instance fingerprint;') -and
        [regex]::IsMatch(
            $headerText,
            'canonical_values\s*\[\s*JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT\s*\]'
        )
    ) `
    -Detail 'The adapter must own one bounded fingerprint and nine originals.'

Add-Check `
    -Name 'adapter.live-read-compile-gate-hard-disabled' `
    -Passed (
        $headerText.Contains(
            '#define JARVIS_ENABLE_LIVE_XAML_PROPERTY_READ 0'
        ) -and
        $headerText.Contains(
            '#if JARVIS_ENABLE_LIVE_XAML_PROPERTY_READ != 0'
        ) -and
        $headerText.Contains(
            '#error Phase 14 must be compiled with live IInspectable property reads disabled.'
        )
    ) `
    -Detail 'Any nonzero live property-read compile gate must fail compilation.'

Add-Check `
    -Name 'adapter.local-null-or-exact-solid-color-only' `
    -Passed (
        $adapterText.Contains('snapshot->value_origin !=') -and
        $adapterText.Contains(
            'JARVIS_TAP_PROPERTY_VALUE_ORIGIN_LOCAL'
        ) -and
        $adapterText.Contains('snapshot->runtime_value_kind ==') -and
        $adapterText.Contains('JARVIS_TAP_RUNTIME_VALUE_NULL') -and
        $adapterText.Contains(
            'JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH'
        ) -and
        $adapterText.Contains(
            'snapshot->exact_runtime_class_name_matched != 1U'
        ) -and
        $adapterText.Contains(
            'JARVIS_TAP_OPACITY_MILLIONTHS_MAX'
        )
    ) `
    -Detail 'Only exact local null or verified solid color may be canonicalized.'

Add-Check `
    -Name 'adapter.fail-closed-before-fingerprint-forward' `
    -Passed (
        $adapterText.Contains(
            'instance->state = JARVIS_TAP_ADAPTER_STATE_BLOCKED'
        ) -and
        $adapterText.Contains(
            'instance->fingerprint.state ='
        ) -and
        $adapterText.Contains(
            'jarvis_tap_fingerprint_observe('
        ) -and
        $adapterText.Contains(
            'instance->canonical_values[index] = canonical'
        )
    ) `
    -Detail 'Unsupported projections must latch both layers before forwarding.'

$forbiddenPattern = (
    '(?i)\b(?:windows\.h|xamlom\.h|roapi\.h|wrl\.h|' +
    'IXamlDiagnostics|IVisualTreeService|DependencyObject|DependencyProperty|' +
    'ReadLocalValue|GetRuntimeClassName|LoadLibrary|GetProcAddress|' +
    'OpenProcess|EnumWindows|SetValue|ClearValue|SetProperty|' +
    'InitializeXamlDiagnosticsEx|StartService|RegSetValue)\b'
)
Add-Check `
    -Name 'adapter.no-windows-com-xaml-read-or-write-api' `
    -Passed (-not [regex]::IsMatch($modelSource, $forbiddenPattern)) `
    -Detail 'The portable model may not contain live COM, XAML, loader or mutation APIs.'

Add-Check `
    -Name 'integration.linked-unreachable-unexported' `
    -Passed (
        $controllerText.Contains(
            '\"offlineInspectableAdapterModelSupported\":true'
        ) -and
        $controllerText.Contains('\"propertyReadSupported\":false') -and
        $tapText.Contains('return E_ACCESSDENIED;') -and
        $tapAuditText.Contains('$adapterPath') -and
        -not $controllerText.Contains(
            'jarvis_tap_inspectable_adapter_observe('
        ) -and
        -not $tapText.Contains(
            'jarvis_tap_inspectable_adapter_observe('
        )
    ) `
    -Detail 'The model may be linked but no disk-binary entry point may reach it.'

Add-Check `
    -Name 'docs.no-live-property-read-claim' `
    -Passed (
        $taskText.Contains(
            'OFFLINE PROJECTION MODEL COMPLETE — NO IINSPECTABLE READ'
        ) -and
        $taskText.Contains(
            'They are not proof that Explorer was read'
        ) -and
        $taskText.Contains(
            'Phase 14 grants no permission to connect to Explorer'
        )
    ) `
    -Detail 'Documentation must distinguish projection fixtures from Explorer evidence.'

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
            $executablePath = Join-Path $temporaryRoot 'adapter-harness.exe'
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
                    $harnessPath `
                    -o $executablePath 2>&1
            )
            $buildExitCode = $LASTEXITCODE
            Add-Check `
                -Name 'build.portable-inspectable-adapter-harness' `
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
                    -Name 'harness.projection-fault-matrix' `
                    -Passed (
                        $harnessExitCode -eq 0 -and
                        $null -ne $receipt -and
                        $receipt.result -eq 'passed' -and
                        $scenarioCount -eq 29 -and
                        $scenarioPassedCount -eq 29 -and
                        -not $receipt.iInspectableReadAttempted -and
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
                    'jarvis2-explorer-inspectable-adapter-',
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
    receiptType = 'jarvisv2-explorer-inspectable-adapter-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scenarioCount = $scenarioCount
    scenarioPassedCount = $scenarioPassedCount
    iInspectableReadAttempted = $false
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
