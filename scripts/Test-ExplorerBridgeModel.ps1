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
$sourceRoot = Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerBridgeModel'
$headerPath =
    Join-Path $sourceRoot 'jarvis_explorer_bridge_contract.h'
$modelPath =
    Join-Path $sourceRoot 'jarvis_explorer_bridge_model.cpp'
$harnessPath =
    Join-Path $root 'tests\native\windows11\jarvis_explorer_bridge_model_harness.cpp'
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ("jarvis2-explorer-bridge-model-" + [Guid]::NewGuid().ToString('N'))

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

$forbiddenRuntimePattern = (
    '(?i)\b(?:windows\.h|DllMain|__declspec\s*\(\s*dllexport|' +
    'LoadLibrary|GetProcAddress|OpenProcess|CreateRemoteThread|' +
    'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|UnhookWindowsHookEx|' +
    'NtQueueApcThread|StartService|ServiceController|RegOpenKey|' +
    'CreateToolhelp32Snapshot|EnumProcesses|TerminateProcess)\b'
)
Add-Check `
    -Name 'source.no-loader-injection-hook-or-process-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenRuntimePattern)) `
    -Detail (
        'The bridge model must contain no Windows loader, export, process, ' +
        'service, registry, hook-installation or injection API.'
    )

Add-Check `
    -Name 'contract.fixed-width-versioned-boundary' `
    -Passed (
        $sourceText.Contains('JARVIS_EXPLORER_BRIDGE_ABI_VERSION = 1U') -and
        $sourceText.Contains('std::uint32_t explorer_process_id') -and
        $sourceText.Contains('std::uint32_t shell_thread_id') -and
        $sourceText.Contains('std::uint64_t session_nonce') -and
        $sourceText.Contains(
            'static_assert(sizeof(jarvis_bridge_init_request) == 24U)'
        ) -and
        $sourceText.Contains(
            'static_assert(sizeof(jarvis_bridge_response) == 32U)'
        )
    ) `
    -Detail (
        'The review boundary must pin ABI v1, exact-width identity fields ' +
        'and compile-time structure sizes.'
    )

Add-Check `
    -Name 'contract.always-denies-live-execution' `
    -Passed (
        $sourceText.Contains('.activation_permitted = 0U') -and
        $sourceText.Contains('.mutation_performed = 0U') -and
        $sourceText.Contains('.live_explorer_touched = 0U') -and
        $sourceText.Contains('JARVIS_BRIDGE_RESULT_EXECUTION_UNSUPPORTED')
    ) `
    -Detail (
        'Every response must deny activation, system mutation and live ' +
        'Explorer contact.'
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

if ($null -ne $compiler) {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    try {
        $executablePath = Join-Path $temporaryRoot 'bridge-model-harness.exe'
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
                (($compileOutput | Select-Object -Last 12) -join ' ')
            )

        if ($compileExitCode -eq 0) {
            $harnessOutput = @(& $executablePath 2>&1)
            $harnessExitCode = $LASTEXITCODE
            $payload = ($harnessOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
            Add-Check `
                -Name 'harness.fault-matrix' `
                -Passed (
                    $harnessExitCode -eq 0 -and
                    $payload.result -eq 'passed' -and
                    $payload.scenarioCount -eq 16 -and
                    $payload.passedCount -eq 16 -and
                    -not $payload.executionSupported -and
                    -not $payload.activationPermitted -and
                    -not $payload.mutationPerformed -and
                    $payload.liveExplorer -eq 'not-run'
                ) `
                -Detail (
                    "Harness exit $harnessExitCode; " +
                    "passed $($payload.passedCount)/$($payload.scenarioCount)."
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
                'jarvis2-explorer-bridge-model-',
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
    receiptType = 'jarvisv2-explorer-bridge-model-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
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
