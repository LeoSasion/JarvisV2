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
$sourceRoot = Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerTransportModel'
$headerPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_transport_contract.h'
$modelPath = Join-Path (
    $sourceRoot
) 'jarvis_explorer_transport_model.cpp'
$harnessPath = Join-Path (
    $root
) 'tests\native\windows11\jarvis_explorer_transport_model_harness.cpp'
$contractPath = Join-Path (
    $root
) 'config\explorer-xaml-transport-contract.json'
$contractSchemaPath = Join-Path (
    $root
) 'config\explorer-xaml-transport-contract.schema.json'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ("jarvis2-explorer-transport-model-" + [Guid]::NewGuid().ToString('N'))

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

$sourceText = @(
    [IO.File]::ReadAllText($headerPath),
    [IO.File]::ReadAllText($modelPath),
    [IO.File]::ReadAllText($harnessPath)
) -join [Environment]::NewLine
$contractText = [IO.File]::ReadAllText($contractPath)
$contractSchemaText = [IO.File]::ReadAllText($contractSchemaPath)
$contract = $contractText | ConvertFrom-Json -Depth 100
$contractSchema = $contractSchemaText | ConvertFrom-Json -Depth 100

Add-Check `
    -Name 'machine-contract.pinned-api-and-gpl-evidence' `
    -Passed (
        $contract.schemaVersion -eq 1 -and
        $contract.contractId -eq 'jarvis-explorer-xaml-transport-v1' -and
        $contract.lifecycleState -eq 'offline-model-only' -and
        $contract.connectionCandidate.api -eq
            'InitializeXamlDiagnosticsEx' -and
        $contract.connectionCandidate.liveConnectionImplemented -eq
            $false -and
        $contract.evidenceBasis.gplUpstreamCommit -eq
            '109589023dde428deaee2fe80e4ce446283a7935' -and
        $contract.evidenceBasis.gplUpstreamSourceSha256 -eq
            'ECD6189A76439518E84938F4CA42FDB7F78AA1CCE3151EE0FE93638918D2DCED'
    ) `
    -Detail (
        'The machine contract must bind the official XAML Diagnostics entry ' +
        'point and exact reviewed GPL upstream while declaring no live link.'
    )

Add-Check `
    -Name 'machine-contract.single-target-failclosed-schema' `
    -Passed (
        $contract.targetIdentity.processEnumerationAllowed -eq $false -and
        $contract.targetIdentity.windowEnumerationAllowed -eq $false -and
        $contract.targetIdentity.identityRecheckBeforeEveryCommand -eq
            $true -and
        $contract.capability.oneShot -eq $true -and
        $contract.capability.selfApprovalAllowed -eq $false -and
        $contract.surfacePolicy.requiredOriginalJournalEntryCount -eq 9 -and
        $contract.recoveryPolicy.automaticExplorerRestartAllowed -eq
            $false -and
        $contract.executionSupported -eq $false -and
        $contract.readyForLiveConnection -eq $false -and
        $contract.readyForExactApproval -eq $false -and
        $contract.activationPermitted -eq $false -and
        $contract.liveExplorer -eq 'not-run' -and
        $contract.mutationPerformed -eq $false -and
        $contractSchema.'$schema' -eq
            'https://json-schema.org/draft/2020-12/schema' -and
        $contractSchema.additionalProperties -eq $false -and
        $contractSchemaText.Contains(
            '"const": "jarvis-explorer-xaml-transport-v1"'
        )
    ) `
    -Detail (
        'The contract/schema must forbid enumeration and self-approval, ' +
        'require the nine-entry journal, and retain all locked-state claims.'
    )

$forbiddenRuntimePattern = (
    '(?i)\b(?:windows\.h|xamlom\.h|DllMain|' +
    '__declspec\s*\(\s*dllexport|InitializeXamlDiagnosticsEx|' +
    'IXamlDiagnostics|IVisualTreeService|SetProperty|ClearProperty|' +
    'LoadLibrary|GetProcAddress|OpenProcess|CreateRemoteThread|' +
    'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|' +
    'UnhookWindowsHookEx|SendMessage|PostMessage|EnumWindows|' +
    'CreateToolhelp32Snapshot|TerminateProcess|StartService|' +
    'ServiceController|RegOpenKey)\b'
)
Add-Check `
    -Name 'source.no-live-xaml-loader-hook-process-or-window-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenRuntimePattern)) `
    -Detail (
        'The Phase 11 core must contain no XAML Diagnostics entry point, TAP ' +
        'export, loader, hook, process, window, service or registry API.'
    )

Add-Check `
    -Name 'contract.fixed-width-versioned-abi' `
    -Passed (
        $sourceText.Contains(
            'JARVIS_EXPLORER_TRANSPORT_ABI_VERSION = 1U'
        ) -and
        $sourceText.Contains(
            'static_assert(sizeof(jarvis_transport_bind_request) == 616U)'
        ) -and
        $sourceText.Contains(
            'static_assert(sizeof(jarvis_transport_surface_request) == 160U)'
        ) -and
        $sourceText.Contains(
            'static_assert(sizeof(jarvis_transport_property_request) == 168U)'
        ) -and
        $sourceText.Contains(
            'static_assert(sizeof(jarvis_transport_response) == 64U)'
        )
    ) `
    -Detail (
        'The transport boundary must pin ABI v1 and exact request/response ' +
        'sizes across the future controller and TAP boundary.'
    )

Add-Check `
    -Name 'contract.exact-target-identity-bound' `
    -Passed (
        $sourceText.Contains('std::uint32_t explorer_process_id') -and
        $sourceText.Contains('std::uint32_t desktop_shell_process_id') -and
        $sourceText.Contains('std::uint32_t window_thread_id') -and
        $sourceText.Contains('std::uint64_t window_handle') -and
        $sourceText.Contains('process_start_time_utc_ticks') -and
        $sourceText.Contains('visual_tree_generation_sha256') -and
        $sourceText.Contains('exact_window_title_sha256') -and
        $sourceText.Contains(
            'target.explorer_process_id != target.desktop_shell_process_id'
        )
    ) `
    -Detail (
        'Every command must stay bound to one non-desktop Explorer PID, TID, ' +
        'HWND, start time, exact title hash and visual-tree generation.'
    )

Add-Check `
    -Name 'contract.one-shot-capability-and-sequence' `
    -Passed (
        $sourceText.Contains('session_nonce') -and
        $sourceText.Contains('selector_profile_sha256') -and
        $sourceText.Contains('preview_plan_sha256') -and
        $sourceText.Contains('expected_selector_sha256') -and
        $sourceText.Contains('expected_styled_value_sha256') -and
        $sourceText.Contains('JARVIS_TRANSPORT_MAX_CAPABILITY_AGE_MS') -and
        $sourceText.Contains('capability_consumed = 1U') -and
        $sourceText.Contains(
            'request->sequence != instance->next_sequence'
        ) -and
        $sourceText.Contains('JARVIS_TRANSPORT_RESULT_BIND_REPLAY')
    ) `
    -Detail (
        'The model must consume one plan/profile-bound capability once and ' +
        'reject bind replay or a command sequence gap.'
    )

Add-Check `
    -Name 'contract.exact-three-by-three-journal' `
    -Passed (
        $sourceText.Contains(
            'JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT = 3U'
        ) -and
        $sourceText.Contains(
            'JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT = 3U'
        ) -and
        $sourceText.Contains(
            'JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT = 9U'
        ) -and
        $sourceText.Contains('JARVIS_TRANSPORT_RESULT_SURFACE_NOT_UNIQUE') -and
        $sourceText.Contains(
            'instance->journaled_property_count =='
        )
    ) `
    -Detail (
        'Exactly three distinct surfaces and nine original property hashes ' +
        'must be journaled before a simulated apply is admitted.'
    )

Add-Check `
    -Name 'contract.deadline-and-strict-reverse-restore' `
    -Passed (
        $sourceText.Contains(
            'JARVIS_TRANSPORT_PREVIEW_DURATION_MS = 60000U'
        ) -and
        $sourceText.Contains(
            'instance->preview_deadline_monotonic_ms'
        ) -and
        $sourceText.Contains(
            'const auto expected_index = instance->applied_property_count - 1U'
        ) -and
        $sourceText.Contains(
            'instance->state = JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED'
        ) -and
        $sourceText.Contains(
            'instance->restore_required = 1U'
        )
    ) `
    -Detail (
        'The 60-second deadline must latch restore-required, and restoration ' +
        'must consume only the exact original journal in reverse order.'
    )

Add-Check `
    -Name 'contract.hard-nonlive-receipt' `
    -Passed (
        $sourceText.Contains('.execution_supported = 0U') -and
        $sourceText.Contains('.activation_permitted = 0U') -and
        $sourceText.Contains('.mutation_performed = 0U') -and
        $sourceText.Contains('.live_explorer_touched = 0U')
    ) `
    -Detail (
        'The portable model may simulate outcomes but every public response ' +
        'must deny execution, activation, mutation and live Explorer contact.'
    )

$compiler = $null
if (-not $StaticOnly) {
    $portableCompiler = Join-Path (
        Join-Path $ToolCache 'portable'
    ) 'Compiler\bin\clang++.exe'
    $compiler = if (
        Test-Path -LiteralPath $portableCompiler -PathType Leaf
    ) {
        $portableCompiler
    }
    else {
        $clangCommand = Get-Command clang++.exe -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $clangCommand) {
            $null
        }
        else {
            $clangCommand.Source
        }
    }
    $compilerDetail = if ($null -eq $compiler) {
        'No portable or PATH clang++ compiler is available.'
    }
    else {
        "Using compiler: $compiler"
    }
    Add-Check `
        -Name 'toolchain.compiler-available' `
        -Passed ($null -ne $compiler) `
        -Detail $compilerDetail
}

$scenarioCount = 0
$passedCount = 0
if ($null -ne $compiler) {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    try {
        $executablePath = Join-Path (
            $temporaryRoot
        ) 'explorer-transport-model-harness.exe'
        $compileOutput = @(
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
                -x c++ `
                $modelPath `
                $harnessPath `
                -o $executablePath 2>&1
        )
        $compileExitCode = $LASTEXITCODE
        Add-Check `
            -Name 'build.portable-harness' `
            -Passed (
                $compileExitCode -eq 0 -and
                (Test-Path -LiteralPath $executablePath -PathType Leaf)
            ) `
            -Detail (
                "Compiler exit $compileExitCode. " +
                (($compileOutput | Select-Object -Last 16) -join ' ')
            )

        if ($compileExitCode -eq 0) {
            $harnessOutput = @(& $executablePath 2>&1)
            $harnessExitCode = $LASTEXITCODE
            $payload = ($harnessOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
            $scenarioCount = [int]$payload.scenarioCount
            $passedCount = [int]$payload.passedCount
            Add-Check `
                -Name 'harness.transport-fault-matrix' `
                -Passed (
                    $harnessExitCode -eq 0 -and
                    $payload.result -eq 'passed' -and
                    $scenarioCount -eq 85 -and
                    $passedCount -eq $scenarioCount -and
                    -not $payload.executionSupported -and
                    -not $payload.activationPermitted -and
                    -not $payload.mutationPerformed -and
                    $payload.liveExplorer -eq 'not-run'
                ) `
                -Detail (
                    "Harness exit $harnessExitCode; " +
                    "passed $passedCount/$scenarioCount."
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
                'jarvis2-explorer-transport-model-',
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

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-transport-model-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scenarioCount = $scenarioCount
    scenarioPassedCount = $passedCount
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
