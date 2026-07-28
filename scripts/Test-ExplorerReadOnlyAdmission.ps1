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
$admissionHeaderPath = Join-Path $sourceRoot 'jarvis_explorer_tap_admission.h'
$admissionPath = Join-Path $sourceRoot 'jarvis_explorer_tap_admission.cpp'
$fingerprintHeaderPath = Join-Path $sourceRoot 'jarvis_explorer_tap_fingerprint.h'
$fingerprintPath = Join-Path $sourceRoot 'jarvis_explorer_tap_fingerprint.cpp'
$controllerPath = Join-Path $sourceRoot 'jarvis_explorer_tap_controller.cpp'
$tapPath = Join-Path $sourceRoot 'jarvis_explorer_tap_readonly.cpp'
$harnessPath = Join-Path (
    $root
) 'tests\native\jarvis_explorer_tap_admission_harness.cpp'
$contractPath = Join-Path (
    $root
) 'config\explorer-readonly-admission-fingerprint-contract.json'
$contractSchemaPath = Join-Path (
    $root
) 'config\explorer-readonly-admission-fingerprint-contract.schema.json'
$taskPath = Join-Path (
    $root
) 'docs\PHASE-13-EXPLORER-READONLY-ADMISSION-AND-FINGERPRINT-TASK.md'
$tapAuditPath = Join-Path $root 'scripts\Test-ExplorerReadOnlyTap.ps1'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ("jarvis2-explorer-readonly-admission-" + [Guid]::NewGuid().ToString('N'))

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

$admissionHeaderText = [IO.File]::ReadAllText($admissionHeaderPath)
$admissionText = [IO.File]::ReadAllText($admissionPath)
$fingerprintHeaderText = [IO.File]::ReadAllText($fingerprintHeaderPath)
$fingerprintText = [IO.File]::ReadAllText($fingerprintPath)
$controllerText = [IO.File]::ReadAllText($controllerPath)
$tapText = [IO.File]::ReadAllText($tapPath)
$harnessText = [IO.File]::ReadAllText($harnessPath)
$tapAuditText = [IO.File]::ReadAllText($tapAuditPath)
$taskText = [IO.File]::ReadAllText($taskPath)
$contractText = [IO.File]::ReadAllText($contractPath)
$contractSchemaText = [IO.File]::ReadAllText($contractSchemaPath)
$contract = $contractText | ConvertFrom-Json -Depth 100
$contractSchema = $contractSchemaText | ConvertFrom-Json -Depth 100
$modelSource = @(
    $admissionHeaderText,
    $admissionText,
    $fingerprintHeaderText,
    $fingerprintText,
    $harnessText
) -join [Environment]::NewLine

Add-Check `
    -Name 'machine-contract.single-endpoint-offline-only' `
    -Passed (
        $contract.schemaVersion -eq 1 -and
        $contract.contractId -eq
            'jarvis-explorer-readonly-admission-fingerprint-v1' -and
        $contract.lifecycleState -eq 'offline-model-only' -and
        $contract.admission.callerSuppliedExactPidRequired -and
        -not $contract.admission.processEnumerationAllowed -and
        -not $contract.admission.windowEnumerationAllowed -and
        $contract.admission.existingDiagnosticsConsumerCountRequired -eq 0 -and
        $contract.admission.endpointCandidateCountRequired -eq 1 -and
        $contract.admission.runtimeEndpointAttemptLimit -eq 0 -and
        $contract.admission.oneShotPlanConsumedOnAdmission -and
        $contract.admission.completeBindByteMatchRequired -and
        $contract.admission.replayPolicy -eq 'latch-blocked' -and
        -not $contract.fingerprint.propertyReadSupported -and
        -not $contract.integration.endpointAttemptedDuringValidation -and
        -not $contract.integration.tapDllLoadedDuringValidation -and
        -not $contract.executionSupported -and
        -not $contract.readyForLiveConnection -and
        -not $contract.readyForExactApproval -and
        -not $contract.activationPermitted -and
        $contract.liveExplorer -eq 'not-run' -and
        -not $contract.mutationPerformed -and
        $contractSchema.'$schema' -eq
            'https://json-schema.org/draft/2020-12/schema' -and
        $contractSchema.additionalProperties -eq $false
    ) `
    -Detail (
        'The machine contract/schema must require an exact caller target, ' +
        'zero consumers, one offline endpoint candidate and no endpoint attempt.'
    )

Add-Check `
    -Name 'admission.fixed-width-binary-and-capability-identity' `
    -Passed (
        $admissionHeaderText.Contains(
            'static_assert(sizeof(jarvis_tap_admission_request) == 792U)'
        ) -and
        $admissionHeaderText.Contains('controller_sha256') -and
        $admissionHeaderText.Contains('tap_dll_sha256') -and
        $admissionHeaderText.Contains('xaml_diagnostics_sha256') -and
        $admissionHeaderText.Contains('endpoint_name_sha256') -and
        $admissionText.Contains(
            'jarvis_tap_encode_initialization_data('
        ) -and
        $admissionText.Contains(
            'request->evaluated_at_monotonic_ms <'
        )
    ) `
    -Detail (
        'Admission must bind the full transport capability and four nonzero ' +
        'binary/endpoint hashes in a fixed-width request.'
    )

Add-Check `
    -Name 'admission.zero-consumer-one-endpoint-one-shot' `
    -Passed (
        $admissionText.Contains(
            'request->observed_consumer_count != 0U'
        ) -and
        $admissionText.Contains(
            'request->endpoint_candidate_count != 1U'
        ) -and
        $admissionText.Contains('request->tap_export_count != 2U') -and
        $admissionText.Contains('instance->plan_consumed = 1U') -and
        $admissionText.Contains('JARVIS_TAP_ADMISSION_RESULT_REPLAY') -and
        $admissionText.Contains(
            'instance->state = JARVIS_TAP_ADMISSION_STATE_BLOCKED'
        )
    ) `
    -Detail (
        'Any existing consumer, non-single endpoint, export drift or replay ' +
        'must fail closed, and the first passing plan must be consumed once.'
    )

Add-Check `
    -Name 'fingerprint.fixed-three-by-three-sequence-and-identity' `
    -Passed (
        $fingerprintHeaderText.Contains(
            'static_assert(sizeof(jarvis_tap_fingerprint_request) == 176U)'
        ) -and
        $fingerprintText.Contains(
            'expected_index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT'
        ) -and
        $fingerprintText.Contains(
            'expected_index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT'
        ) -and
        $fingerprintText.Contains(
            'request->sequence != instance->next_sequence'
        ) -and
        $fingerprintText.Contains(
            'request->selector_sha256'
        ) -and
        $fingerprintText.Contains(
            'request.target.visual_tree_generation_sha256'
        ) -and
        $fingerprintText.Contains(
            'instance->observed_property_count =='
        )
    ) `
    -Detail (
        'Exactly nine ordered observations must bind target generation, ' +
        'selector, instance, surface and property slots.'
    )

Add-Check `
    -Name 'fingerprint.canonical-null-solid-color-and-domain-sha256' `
    -Passed (
        $fingerprintHeaderText.Contains(
            'JARVIS_TAP_PROPERTY_VALUE_NULL = 0U'
        ) -and
        $fingerprintHeaderText.Contains(
            'JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR = 1U'
        ) -and
        $fingerprintHeaderText.Contains(
            'JARVIS_TAP_OPACITY_MILLIONTHS_MAX = 1000000U'
        ) -and
        $fingerprintText.Contains(
            "'X', 'A', 'M', 'L', '-', 'P', 'R', 'O', 'P', '-', 'V', '1'"
        ) -and
        $fingerprintText.Contains('kSha256RoundConstants') -and
        $fingerprintText.Contains(
            'JARVIS_TAP_FINGERPRINT_RESULT_VALUE_UNSUPPORTED'
        ) -and
        $fingerprintText.Contains(
            'JARVIS_TAP_FINGERPRINT_RESULT_VALUE_NONCANONICAL'
        )
    ) `
    -Detail (
        'Only null or bounded solid color values may enter the domain-separated ' +
        'allocation-free SHA-256 fingerprint.'
    )

$forbiddenModelPattern = (
    '(?i)\b(?:windows\.h|xamlom\.h|ocidl\.h|InitializeXamlDiagnosticsEx|' +
    'IXamlDiagnostics|IVisualTreeService|LoadLibrary|GetProcAddress|' +
    'OpenProcess|EnumWindows|EnumChildWindows|VirtualAllocEx|' +
    'WriteProcessMemory|CreateRemoteThread|SetWindowsHookEx|' +
    'SendMessage|PostMessage|SetProperty|ClearProperty|AddChild|' +
    'RemoveChild|ReplaceResource|TerminateProcess|RegSetValue|' +
    'StartService)\b'
)
Add-Check `
    -Name 'models.no-windows-xaml-loader-read-or-mutation-api' `
    -Passed (-not [regex]::IsMatch($modelSource, $forbiddenModelPattern)) `
    -Detail (
        'The admission/fingerprint models must remain portable value models ' +
        'with no Windows, XAML, loader, process, hook, property or system API.'
    )

Add-Check `
    -Name 'integration.models-linked-but-unreachable-and-unexported' `
    -Passed (
        $controllerText.Contains(
            '\"offlineAdmissionModelSupported\":true'
        ) -and
        $controllerText.Contains(
            '\"offlineEndpointCandidateLimit\":1'
        ) -and
        $controllerText.Contains(
            '\"offlineFingerprintModelSupported\":true'
        ) -and
        $controllerText.Contains('\"propertyReadSupported\":false') -and
        $tapText.Contains(
            'static_assert(JARVIS_ENABLE_LIVE_XAML_READONLY == 0)'
        ) -and
        $tapText.Contains('return E_ACCESSDENIED;') -and
        $tapAuditText.Contains('$admissionPath') -and
        $tapAuditText.Contains('$fingerprintPath') -and
        -not $tapText.Contains('jarvis_tap_fingerprint_observe(') -and
        -not $controllerText.Contains('jarvis_tap_admission_evaluate(')
    ) `
    -Detail (
        'The disk binaries may contain the models, but their only runtime ' +
        'entry remains describe-only/SetSite-denied and model functions stay unexported.'
    )

Add-Check `
    -Name 'docs.no-endpoint-attempt-property-read-or-live-claim' `
    -Passed (
        $taskText.Contains(
            'OFFLINE MODELS COMPLETE — NO ENDPOINT ATTEMPT OR PROPERTY READ'
        ) -and
        $taskText.Contains(
            'These hashes are evidence identifiers only.'
        ) -and
        $taskText.Contains(
            'Phase 13 grants no permission to connect, inject, read Explorer properties or'
        ) -and
        $taskText.Contains('modify the desktop.')
    ) `
    -Detail (
        'Phase 13 documentation must distinguish canonical offline values from ' +
        'a real IInspectable read or live Explorer evidence.'
    )

$compiler = $null
$scenarioCount = 0
$scenarioPassedCount = 0
$firstFingerprintSha256 = $null
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
            ) 'admission-fingerprint-harness.exe'
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
                    $harnessPath `
                    -o $executablePath 2>&1
            )
            $buildExitCode = $LASTEXITCODE
            Add-Check `
                -Name 'build.portable-admission-fingerprint-harness' `
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
                    $firstFingerprintSha256 =
                        [string]$receipt.firstFingerprintSha256
                }
                Add-Check `
                    -Name 'harness.admission-and-fingerprint-fault-matrix' `
                    -Passed (
                        $harnessExitCode -eq 0 -and
                        $null -ne $receipt -and
                        $receipt.result -eq 'passed' -and
                        $scenarioCount -eq 50 -and
                        $scenarioPassedCount -eq 50 -and
                        $firstFingerprintSha256 -eq
                            '00542DB9887A4CE9FA17AD0B42EC164D5E38FDD3BFE410D9517B2814CC264560' -and
                        -not $receipt.endpointAttempted -and
                        -not $receipt.tapDllLoaded -and
                        -not $receipt.propertyReadSupported -and
                        -not $receipt.liveConnectionCompiled -and
                        -not $receipt.executionSupported -and
                        -not $receipt.activationPermitted -and
                        $receipt.liveExplorer -eq 'not-run' -and
                        -not $receipt.mutationPerformed
                    ) `
                    -Detail (
                        "Harness exit $harnessExitCode; passed " +
                        "$scenarioPassedCount/$scenarioCount; vector " +
                        "$firstFingerprintSha256."
                    )
            }
        }
        finally {
            $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
            $resolvedTemp = [IO.Path]::GetFullPath(
                [IO.Path]::GetTempPath()
            ).TrimEnd('\') + '\'
            if (
                $resolvedTemporaryRoot.StartsWith(
                    $resolvedTemp,
                    [StringComparison]::OrdinalIgnoreCase
                ) -and
                [IO.Path]::GetFileName($resolvedTemporaryRoot).StartsWith(
                    'jarvis2-explorer-readonly-admission-',
                    [StringComparison]::Ordinal
                )
            ) {
                Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
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
    receiptType =
        'jarvisv2-explorer-readonly-admission-fingerprint-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scenarioCount = $scenarioCount
    scenarioPassedCount = $scenarioPassedCount
    firstFingerprintSha256 = $firstFingerprintSha256
    endpointAttempted = $false
    tapDllLoaded = $false
    propertyReadSupported = $false
    liveConnectionCompiled = $false
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
