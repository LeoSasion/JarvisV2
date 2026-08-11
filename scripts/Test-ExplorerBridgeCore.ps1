[CmdletBinding()]
param(
    [switch]$StaticOnly,

    [ValidateSet('windows10', 'windows11')]
    [string]$Platform = 'windows11'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$isWindows10 = $Platform -ceq 'windows10'
$platformPrefix = if ($Platform -ceq 'windows10') { 'Jarvis.Win10.' } else { 'Jarvis.' }
$expectedAbiVersion = if ($isWindows10) { 3 } else { 2 }
$expectedResponseBytes = if ($isWindows10) { 68 } else { 64 }
$expectedCoreExportCount = if ($isWindows10) { 5 } else { 4 }
$sourceRoot = Join-Path $root (
    "src\platforms\$Platform\${platformPrefix}ExplorerBridgeCore")
$headerPath = Join-Path $sourceRoot 'jarvis_explorer_bridge_core.h'
$internalPath = Join-Path $sourceRoot (
    'jarvis_explorer_bridge_core_internal.h')
$corePath = Join-Path $sourceRoot 'jarvis_explorer_bridge_core.cpp'
$harnessPath = Join-Path $root (
    $(if ($Platform -ceq 'windows10') {
        'tests\native\windows10\jarvis_win10_explorer_bridge_core_harness.cpp'
    } else {
        'tests\native\windows11\jarvis_explorer_bridge_core_harness.cpp'
    }))
$temporaryRoot = Join-Path (
    [IO.Path]::GetTempPath()
) ('jarvis2-explorer-bridge-core-' + [Guid]::NewGuid().ToString('N'))
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

function Import-MsvcEnvironment {
    param([Parameter(Mandatory)] [string]$TemporaryDirectory)

    $programFilesX86 = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ProgramFilesX86)
    $vswherePath = Join-Path $programFilesX86 (
        'Microsoft Visual Studio\Installer\vswhere.exe')
    if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
        return $false
    }

    $installationPath = @(
        & $vswherePath `
            -latest `
            -products '*' `
            -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -property installationPath 2>$null
    ) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($installationPath)) {
        return $false
    }

    $devCommand = Join-Path $installationPath 'Common7\Tools\VsDevCmd.bat'
    if (-not (Test-Path -LiteralPath $devCommand -PathType Leaf)) {
        return $false
    }

    $environmentScript = Join-Path $TemporaryDirectory 'msvc-environment.cmd'
    [IO.File]::WriteAllText(
        $environmentScript,
        "@call `"$devCommand`" -no_logo -arch=x64 -host_arch=x64`r`n@set`r`n",
        [Text.Encoding]::ASCII)
    $environmentLines = @(
        & $env:ComSpec /d /c $environmentScript 2>$null
    )
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    foreach ($line in $environmentLines) {
        if ($line -match '^([^=]+)=(.*)$') {
            [Environment]::SetEnvironmentVariable(
                $Matches[1],
                $Matches[2],
                [EnvironmentVariableTarget]::Process)
        }
    }
    return $null -ne (Get-Command cl.exe -ErrorAction SilentlyContinue)
}

$sourceText = @(
    [IO.File]::ReadAllText($headerPath),
    [IO.File]::ReadAllText($internalPath),
    [IO.File]::ReadAllText($corePath),
    [IO.File]::ReadAllText($harnessPath)
) -join [Environment]::NewLine

$forbiddenRuntimePattern = (
    '(?i)\b(?:windows\.h|DllMain|LoadLibrary|GetProcAddress|OpenProcess|' +
    'CreateRemoteThread|VirtualAllocEx|WriteProcessMemory|ReadProcessMemory|' +
    'SetWindowsHookEx|UnhookWindowsHookEx|NtQueueApcThread|StartService|' +
    'ServiceController|RegOpenKey|CreateToolhelp32Snapshot|EnumProcesses|' +
    'TerminateProcess|Process32First|Process32Next)\b'
)
Add-Check `
    -Name 'source.no-loader-hook-process-service-or-registry-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenRuntimePattern)) `
    -Detail (
        'The bridge core may export lifecycle functions but must contain no ' +
        'loader, Hook installation, process enumeration, remote-memory, ' +
        'service, registry or termination API.')

$exportMatches = @(
    [regex]::Matches(
        [IO.File]::ReadAllText($headerPath),
        '(?m)^JarvisBridge_(?:QueryContract|Initialize|Quiesce|QueryState|' +
        'AcquireSharedInstance)\(')
)
Add-Check `
    -Name 'contract.platform-specific-export-abi' `
    -Passed (
        $exportMatches.Count -eq $expectedCoreExportCount -and
        $sourceText.Contains(
            "JARVIS_EXPLORER_BRIDGE_CORE_ABI_VERSION = $($expectedAbiVersion)U") -and
        $sourceText.Contains('__declspec(dllexport)') -and
        -not $sourceText.Contains('JarvisBridge_InstallHook') -and
        -not $sourceText.Contains('JarvisBridge_Load')) `
    -Detail (
        "The $Platform PE boundary must expose exactly " +
        "$expectedCoreExportCount ABI v$expectedAbiVersion exports; loader " +
        'and Hook installation remain absent.')

Add-Check `
    -Name 'contract.fixed-layout-and-exact-identity' `
    -Passed (
        $sourceText.Contains(
            'static_assert(sizeof(jarvis_bridge_core_init_request) == 80U)') -and
        $sourceText.Contains(
            "static_assert(sizeof(jarvis_bridge_core_response) == " +
            "$($expectedResponseBytes)U)") -and
        $sourceText.Contains('std::uint32_t explorer_process_id') -and
        $sourceText.Contains('std::uint32_t shell_thread_id') -and
        $sourceText.Contains('std::uint64_t session_nonce') -and
        $sourceText.Contains('std::uint8_t settings_sha256[32]')) `
    -Detail (
        "ABI v$expectedAbiVersion must pin the $expectedResponseBytes-byte " +
        'response, exact PID/TID, session nonce and settings SHA-256.')

Add-Check `
    -Name 'admission.kill-switch-permit-and-thread-scope-required' `
    -Passed (
        $sourceText.Contains('host_admission_passed != 1U') -and
        $sourceText.Contains('kill_switch_armed != 1U') -and
        $sourceText.Contains('one_shot_permit_valid != 1U') -and
        $sourceText.Contains(
            'JARVIS_EXPLORER_BRIDGE_TRANSPORT_SCOPE_EXACT_THREAD') -and
        $sourceText.Contains('JARVIS_BRIDGE_CORE_RESULT_ADMISSION_DENIED')) `
    -Detail (
        'Preparation must reject absent host admission, armed-state proof, ' +
        'one-shot permit or exact nonzero-thread scope.')

$quiesceText = [IO.File]::ReadAllText($corePath)
$passThroughIndex = $quiesceText.IndexOf(
    'instance->pass_through.store(1U, std::memory_order_release);',
    $quiesceText.IndexOf('jarvis_bridge_core_begin_quiesce'))
$drainingIndex = $quiesceText.IndexOf(
    'JARVIS_BRIDGE_CORE_STATE_DRAINING',
    $quiesceText.IndexOf('jarvis_bridge_core_begin_quiesce'))
Add-Check `
    -Name 'lifecycle.pass-through-before-drain' `
    -Passed (
        $passThroughIndex -ge 0 -and
        $drainingIndex -gt $passThroughIndex -and
        $sourceText.Contains('active_callback_count.fetch_add') -and
        $sourceText.Contains('active_callback_count.fetch_sub') -and
        ($sourceText.Contains('accepted_callback_count.fetch_add') -eq
            $isWindows10) -and
        $sourceText.Contains('PromoteDrainedInstance(instance)')) `
    -Detail (
        'Quiesce must publish pass-through before entering the draining ' +
        'state, then wait for exact callback ownership to reach zero; only ' +
        'the Win10 v3 fork records the cumulative accepted count.')

Add-Check `
    -Name 'lifecycle.external-entry-permanent-pin' `
    -Passed (
        $sourceText.Contains('external_entry_published.store(1U') -and
        $sourceText.Contains(
            '.module_pin_required = external_entry_published == 0U ? 0U : 1U') -and
        $sourceText.Contains('external_entry_published == 0U;') -and
        $sourceText.Contains('unload_permitted = unload_permitted ? 1U : 0U')) `
    -Detail (
        'After any external callback is published, the response must retain ' +
        'the module pin for the Explorer lifetime even after successful drain.')

Add-Check `
    -Name 'lifecycle.allocation-free-atomic-hot-path' `
    -Passed (
        $sourceText.Contains('std::atomic<std::uint32_t>') -and
        -not [regex]::IsMatch(
            [IO.File]::ReadAllText($corePath),
            '(?i)\b(?:new|delete|malloc|calloc|realloc|free|mutex|' +
            'condition_variable|sleep_for|yield)\b')) `
    -Detail (
        'The callback path must use fixed storage and atomics only, with no ' +
        'allocation, lock, wait or scheduler call.')

Add-Check `
    -Name 'receipt.always-denies-live-activation-and-mutation' `
    -Passed (
        $sourceText.Contains('.activation_permitted = 0U') -and
        $sourceText.Contains('.mutation_performed = 0U') -and
        $sourceText.Contains(
            'std::atomic<std::uint32_t> live_explorer_touched') -and
        $sourceText.Contains('live_explorer_touched > 1U') -and
        $sourceText.Contains('response.live_explorer_touched == 1U') -and
        $sourceText.Contains('\"transportIncluded\":false') -and
        $sourceText.Contains('\"hookInstallerIncluded\":false')) `
    -Detail (
        'The bridge-core evidence must never imply transport, activation, ' +
        'Explorer contact or visual mutation.')

Add-Check `
    -Name 'harness.identity-drain-pin-and-concurrency-matrix' `
    -Passed (
        $sourceText.Contains('std::vector<std::thread> workers') -and
        $sourceText.Contains('entered.load') -and
        $sourceText.Contains('leave_failures.load') -and
        $sourceText.Contains('JARVIS_BRIDGE_CORE_RESULT_QUIESCE_PENDING') -and
        $sourceText.Contains('response.module_pin_required == 1U') -and
        $sourceText.Contains('publication_quiesce_race_closed') -and
        $sourceText.Contains('response.unload_permitted == 0U')) `
    -Detail (
        'The executable harness must cover malformed admission, identity ' +
        'drift, duplicate lifecycle calls, callback drain and concurrent ' +
        'quiesce races.')

$compiler = $null
$dumpbin = $null
$bridgeBuilt = $false
$harnessReceipt = $null
$dllSha256 = $null
$dllBytes = 0
if (-not $StaticOnly) {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $environmentImported = Import-MsvcEnvironment `
        -TemporaryDirectory $temporaryRoot
    $compilerCommand = Get-Command cl.exe -ErrorAction SilentlyContinue
    $dumpbinCommand = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
    if ($environmentImported -and $null -ne $compilerCommand) {
        $compiler = $compilerCommand.Source
    }
    if ($environmentImported -and $null -ne $dumpbinCommand) {
        $dumpbin = $dumpbinCommand.Source
    }
    $compilerDetail = if ($null -eq $compiler -or $null -eq $dumpbin) {
        'Visual Studio x64 C++ tools are unavailable.'
    }
    else {
        "Using $compiler and $dumpbin"
    }
    Add-Check `
        -Name 'toolchain.msvc-x64-available' `
        -Passed ($null -ne $compiler -and $null -ne $dumpbin) `
        -Detail $compilerDetail
}

if ($null -ne $compiler -and $null -ne $dumpbin) {
    Push-Location $temporaryRoot
    try {
        $harnessExecutable = Join-Path $temporaryRoot (
            'jarvis-explorer-bridge-core-harness.exe')
        $harnessCompileOutput = @(
            & $compiler `
                /nologo `
                /std:c++20 `
                /O2 `
                /W4 `
                /WX `
                /EHsc `
                /permissive- `
                /Zc:preprocessor `
                /DJARVIS_BRIDGE_CORE_STATIC `
                "/I$sourceRoot" `
                $corePath `
                $harnessPath `
                "/Fe$harnessExecutable" 2>&1
        )
        $harnessCompileExitCode = $LASTEXITCODE
        $harnessCompileDetail =
            "Compiler exit $harnessCompileExitCode. " +
            (($harnessCompileOutput | Select-Object -Last 12) -join ' ')
        Add-Check `
            -Name 'build.concurrent-fault-harness' `
            -Passed (
                $harnessCompileExitCode -eq 0 -and
                (Test-Path -LiteralPath $harnessExecutable -PathType Leaf)) `
            -Detail $harnessCompileDetail

        if ($harnessCompileExitCode -eq 0) {
            $harnessOutput = @(& $harnessExecutable 2>&1)
            $harnessExitCode = $LASTEXITCODE
            try {
                $harnessReceipt =
                    ($harnessOutput -join [Environment]::NewLine) |
                        ConvertFrom-Json
            }
            catch {
                $harnessReceipt = $null
            }
            $harnessDetail = if ($null -eq $harnessReceipt) {
                "Harness exit $harnessExitCode; receipt unavailable."
            }
            else {
                "Harness exit $harnessExitCode; passed " +
                "$($harnessReceipt.passedCount)/" +
                "$($harnessReceipt.scenarioCount)."
            }
            Add-Check `
                -Name 'harness.concurrent-fault-matrix' `
                -Passed (
                    $harnessExitCode -eq 0 -and
                    $null -ne $harnessReceipt -and
                    $harnessReceipt.result -eq 'passed' -and
                    $harnessReceipt.scenarioCount -ge 35 -and
                    $harnessReceipt.passedCount -eq
                        $harnessReceipt.scenarioCount -and
                    $harnessReceipt.bridgeCoreBuilt -and
                    -not $harnessReceipt.transportIncluded -and
                    -not $harnessReceipt.hookInstallerIncluded -and
                    -not $harnessReceipt.activationPermitted -and
                    -not $harnessReceipt.mutationPerformed -and
                    $harnessReceipt.liveExplorer -eq 'not-run') `
                -Detail $harnessDetail
        }

        $dllPath = Join-Path $temporaryRoot $(if ($isWindows10) {
            'jarvis-win10-explorer-bridge-core.dll'
        } else {
            'jarvis-explorer-bridge-core.dll'
        })
        $dllCompileOutput = @(
            & $compiler `
                /nologo `
                /std:c++20 `
                /O2 `
                /W4 `
                /WX `
                /EHsc `
                /permissive- `
                /Zc:preprocessor `
                /LD `
                "/I$sourceRoot" `
                $corePath `
                "/Fe$dllPath" 2>&1
        )
        $dllCompileExitCode = $LASTEXITCODE
        $bridgeBuilt =
            $dllCompileExitCode -eq 0 -and
            (Test-Path -LiteralPath $dllPath -PathType Leaf)
        $dllCompileDetail =
            "Compiler exit $dllCompileExitCode. " +
            (($dllCompileOutput | Select-Object -Last 12) -join ' ')
        Add-Check `
            -Name 'build.standalone-pe-bridge-core' `
            -Passed $bridgeBuilt `
            -Detail $dllCompileDetail

        if ($bridgeBuilt) {
            $exportOutput = @(& $dumpbin /nologo /exports $dllPath 2>&1)
            $exportExitCode = $LASTEXITCODE
            $expectedExports = @(
                'JarvisBridge_QueryContract',
                'JarvisBridge_Initialize',
                'JarvisBridge_Quiesce',
                'JarvisBridge_QueryState'
            )
            if ($isWindows10) {
                $expectedExports += 'JarvisBridge_AcquireSharedInstance'
            }
            $actualExports = @(
                foreach ($line in $exportOutput) {
                    if ($line -match (
                        '^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]+\s+' +
                        '(JarvisBridge_\S+)\s*$')) {
                        $Matches[1]
                    }
                }
            )
            $exportDifference = @(
                Compare-Object `
                    -ReferenceObject $expectedExports `
                    -DifferenceObject $actualExports
            )
            $exportsExact =
                $actualExports.Count -eq $expectedExports.Count -and
                $exportDifference.Count -eq 0
            $exportDetail =
                "dumpbin exit $exportExitCode; actual exports: " +
                ($actualExports -join ', ')
            Add-Check `
                -Name 'binary.exact-reviewed-exports' `
                -Passed ($exportExitCode -eq 0 -and $exportsExact) `
                -Detail $exportDetail
            $dllItem = Get-Item -LiteralPath $dllPath
            $dllBytes = $dllItem.Length
            $dllSha256 = (
                Get-FileHash -LiteralPath $dllPath -Algorithm SHA256
            ).Hash
        }
    }
    finally {
        Pop-Location
    }
}

if (Test-Path -LiteralPath $temporaryRoot) {
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedTemp = [IO.Path]::GetFullPath(
        [IO.Path]::GetTempPath()
    ).TrimEnd('\') + '\'
    if (
        $resolvedTemporaryRoot.StartsWith(
            $resolvedTemp,
            [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTemporaryRoot).StartsWith(
            'jarvis2-explorer-bridge-core-',
            [StringComparison]::Ordinal)
    ) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
    else {
        throw "Refusing to remove unexpected temp path: $temporaryRoot"
    }
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-bridge-core-audit'
    platform = $Platform
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    bridgeCoreImplemented = $true
    bridgeCoreBuilt = $bridgeBuilt
    dllSha256 = $dllSha256
    dllBytes = $dllBytes
    scenarioCount = if ($null -eq $harnessReceipt) { 0 } else {
        $harnessReceipt.scenarioCount
    }
    scenarioPassedCount = if ($null -eq $harnessReceipt) { 0 } else {
        $harnessReceipt.passedCount
    }
    transportIncluded = $false
    hookInstallerIncluded = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
