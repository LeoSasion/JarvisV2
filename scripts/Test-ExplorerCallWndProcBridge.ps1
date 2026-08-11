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
$expectedCoreExportCount = if ($isWindows10) { 5 } else { 4 }
$expectedModuleExportCount = if ($isWindows10) { 6 } else { 5 }
$callbackRoot = Join-Path $root (
    "src\platforms\$Platform\${platformPrefix}ExplorerCallWndProcBridge")
$bridgeRoot = Join-Path $root (
    "src\platforms\$Platform\${platformPrefix}ExplorerBridgeCore")
$callbackHeaderPath = Join-Path $callbackRoot (
    'jarvis_explorer_callwndproc_bridge.h')
$callbackInternalPath = Join-Path $callbackRoot (
    'jarvis_explorer_callwndproc_bridge_internal.h')
$callbackCorePath = Join-Path $callbackRoot (
    'jarvis_explorer_callwndproc_bridge.cpp')
$callbackWindowsPath = Join-Path $callbackRoot (
    'jarvis_explorer_callwndproc_bridge_windows.cpp')
$bridgeHeaderPath = Join-Path $bridgeRoot 'jarvis_explorer_bridge_core.h'
$bridgeInternalPath = Join-Path $bridgeRoot (
    'jarvis_explorer_bridge_core_internal.h')
$bridgeCorePath = Join-Path $bridgeRoot 'jarvis_explorer_bridge_core.cpp'
$harnessPath = Join-Path $root (
    $(if ($Platform -ceq 'windows10') {
        'tests\native\windows10\' +
        'jarvis_win10_explorer_callwndproc_bridge_harness.cpp'
    } else {
        'tests\native\windows11\' +
        'jarvis_explorer_callwndproc_bridge_harness.cpp'
    }))
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'jarvis2-callwndproc-bridge-' + [Guid]::NewGuid().ToString('N'))
$checks = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()

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
    if (-not $Passed) {
        $failures.Add("${Name}: $Detail")
    }
}

function Import-MsvcEnvironment {
    param(
        [Parameter(Mandatory)]
        [string]$TemporaryDirectory
    )
    if ($null -ne (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
        return $true
    }
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

$requiredPaths = @(
    $callbackHeaderPath,
    $callbackInternalPath,
    $callbackCorePath,
    $callbackWindowsPath,
    $bridgeHeaderPath,
    $bridgeInternalPath,
    $bridgeCorePath,
    $harnessPath
)
$missingPaths = @(
    $requiredPaths | Where-Object {
        -not (Test-Path -LiteralPath $_ -PathType Leaf)
    }
)
Add-Check `
    -Name 'source.required-component-set' `
    -Passed ($missingPaths.Count -eq 0) `
    -Detail 'The callback, shared bridge core and fault harness must exist.'
if ($missingPaths.Count -ne 0) {
    [pscustomobject]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-callwndproc-bridge-audit'
        platform = $Platform
        result = 'failed'
        staticOnly = [bool]$StaticOnly
        checkCount = $checks.Count
        passedCount = @($checks | Where-Object passed).Count
        callbackCoreBuilt = $false
        callbackDllBuilt = $false
        callbackDllExecuted = $false
        liveExplorer = 'not-run'
        activationPermitted = $false
        mutationPerformed = $false
        checks = $checks
        failures = $failures
    } | ConvertTo-Json -Depth 10
    exit 1
}

$callbackHeaderText = [IO.File]::ReadAllText($callbackHeaderPath)
$callbackInternalText = [IO.File]::ReadAllText($callbackInternalPath)
$callbackCoreText = [IO.File]::ReadAllText($callbackCorePath)
$callbackWindowsText = [IO.File]::ReadAllText($callbackWindowsPath)
$bridgeHeaderText = [IO.File]::ReadAllText($bridgeHeaderPath)
$bridgeInternalText = [IO.File]::ReadAllText($bridgeInternalPath)
$bridgeCoreText = [IO.File]::ReadAllText($bridgeCorePath)
$harnessText = [IO.File]::ReadAllText($harnessPath)
$productionText = @(
    $callbackHeaderText,
    $callbackInternalText,
    $callbackCoreText,
    $callbackWindowsText,
    $bridgeHeaderText,
    $bridgeInternalText,
    $bridgeCoreText
) -join [Environment]::NewLine

$forbiddenPattern = (
    '(?i)\b(?:SetWindowsHookEx|UnhookWindowsHookEx|LoadLibrary|' +
    'GetProcAddress|OpenProcess|CreateRemoteThread|VirtualAllocEx|' +
    'WriteProcessMemory|ReadProcessMemory|CreateToolhelp32Snapshot|' +
    'EnumProcesses|Process32First|Process32Next|NtGetNextProcess|' +
    'StartService|RegOpenKey|TerminateProcess|GetShellWindow|' +
    'FindWindow|EnumWindows|SetWindowLong|SetWindowPos|DwmSetWindowAttribute)\b'
)
Add-Check `
    -Name 'source.no-installer-loader-discovery-or-mutation-api' `
    -Passed (-not [regex]::IsMatch($productionText, $forbiddenPattern)) `
    -Detail (
        'The callback DLL may chain and read its current identity but must ' +
        'not install, unhook, load, discover, open or mutate any target.')

Add-Check `
    -Name 'bridge.optional-shared-instance-is-fixed-and-lock-free' `
    -Passed (
        $bridgeCoreText.Contains('JARVIS_BRIDGE_CORE_SHARED_INSTANCE') -and
        $bridgeCoreText.Contains(
            'std::atomic<std::uint32_t>::is_always_lock_free') -and
        $bridgeCoreText.Contains(
            '#pragma section(".jvbrdg", read, write, shared)') -and
        $bridgeCoreText.Contains(
            '__declspec(allocate(".jvbrdg"))') -and
        $bridgeCoreText.Contains(
            'constinit jarvis_bridge_core_instance global_instance{}') -and
        $bridgeInternalText.Contains(
            'jarvis_bridge_core_global_instance() noexcept')) `
    -Detail (
        'Only the callback DLL build may place the pointer-free, lock-free ' +
        'bridge instance in the named cross-process PE section.')

$coreExportCount = @(
    [regex]::Matches(
        $bridgeHeaderText,
        '(?m)^JarvisBridge_(?:QueryContract|Initialize|Quiesce|QueryState|' +
        'AcquireSharedInstance)\(')
).Count
$hasZigZeroEntryStub =
    $callbackWindowsText.Contains(
        '#if defined(JARVIS_ZIG_ZERO_ENTRY_LINK_STUB)') -and
    ([regex]::Matches(
        $callbackWindowsText,
        '(?m)^extern "C" BOOL WINAPI _DllMainCRTStartup\(').Count -eq 1)
Add-Check `
    -Name 'contract.platform-specific-export-source-boundary' `
    -Passed (
        $coreExportCount -eq $expectedCoreExportCount -and
        $callbackWindowsText.Contains('JarvisBridge_CallWndProc(') -and
        $callbackWindowsText.Contains('__declspec(dllexport)') -and
        ($hasZigZeroEntryStub -eq $isWindows10) -and
        -not [regex]::IsMatch($callbackWindowsText, '\bDllMain\s*\(')) `
    -Detail (
        "The $Platform disk-only module must expose exactly " +
        "$expectedModuleExportCount exports. Only the Win10 fork carries " +
        'the guarded Zig no-entry link stub; neither backend defines DllMain.')

Add-Check `
    -Name 'callback.negative-code-direct-chain' `
    -Passed (
        $callbackCoreText.Contains('if (n_code < 0)') -and
        $callbackCoreText.Contains('negative nCode') -and
        [regex]::IsMatch(
            $callbackCoreText,
            '(?s)if \(n_code < 0\) \{\s*return chain\(') -and
        $harnessText.Contains('receipt.size == 0xA5A5A5A5U')) `
    -Detail (
        'A negative nCode must bypass bridge ownership and pass directly to ' +
        'the next Hook as required by the Windows callback contract.')

Add-Check `
    -Name 'callback.exact-current-process-and-thread-identity' `
    -Passed (
        $callbackWindowsText.Contains('GetCurrentProcessId()') -and
        $callbackWindowsText.Contains('GetCurrentThreadId()') -and
        $callbackCoreText.Contains('jarvis_bridge_core_try_enter_callback')) `
    -Detail (
        'Every nonnegative callback must offer its actual current PID/TID to ' +
        'the exact bridge admission check.')

$leaveIndex = $callbackCoreText.LastIndexOf(
    'jarvis_bridge_core_leave_callback')
$finalChainIndex = $callbackCoreText.LastIndexOf('return ChainAndRecord(')
Add-Check `
    -Name 'callback.leave-before-chain-and-chain-result-preserved' `
    -Passed (
        $leaveIndex -ge 0 -and $finalChainIndex -gt $leaveIndex -and
        $callbackCoreText.Contains('const auto chain_result = chain(') -and
        $callbackCoreText.Contains('return chain_result;') -and
        $harnessText.Contains('chain.zero_active_observed.load() == 1U')) `
    -Detail (
        'An admitted callback must release ownership before chaining exactly ' +
        'once and return the next Hook result unchanged.')

Add-Check `
    -Name 'callback.current-windows-body-is-empty' `
    -Passed (
        $callbackWindowsText.Contains(
            'jarvis_callwndproc_dispatch(') -and
        [regex]::IsMatch(
            $callbackWindowsText,
            '(?s)static_cast<std::int64_t>\(l_param\),\s*nullptr,\s*nullptr,\s*&ChainToNextHook') -and
        -not [regex]::IsMatch(
            $callbackWindowsText,
            '(?i)CWPSTRUCT|message|property|xaml|visual|style')) `
    -Detail (
        'The real callback currently supplies no body and never reads or ' +
        'changes the CWPSTRUCT payload.')

Add-Check `
    -Name 'callback.hot-path-fixed-storage-only' `
    -Passed (
        -not [regex]::IsMatch(
            $callbackCoreText + $callbackWindowsText,
            '(?i)\b(?:new|delete|malloc|calloc|realloc|free|mutex|' +
            'condition_variable|sleep|yield|vector|string|iostream)\b')) `
    -Detail (
        'The real callback path must allocate no memory, take no lock and ' +
        'perform no wait, scheduler or stream operation.')

Add-Check `
    -Name 'harness.callback-drain-and-concurrency-matrix' `
    -Passed (
        $harnessText.Contains('body.release.store(0U') -and
        $harnessText.Contains('JARVIS_BRIDGE_CORE_RESULT_QUIESCE_PENDING') -and
        $harnessText.Contains('total_chains.load') -and
        $harnessText.Contains('4000U') -and
        $harnessText.Contains('\"windowsCallbackDllExecuted\":false') -and
        $harnessText.Contains('\"liveExplorer\":\"not-run\"')) `
    -Detail (
        'The harness must cover negative codes, rejection, admitted body, ' +
        'drain ordering and 4,000 callback/quiesce dispatches.')

Add-Check `
    -Name 'receipt.never-claims-activation-or-mutation' `
    -Passed (
        $callbackCoreText.Contains('.activation_permitted = 0U') -and
        $callbackCoreText.Contains('.mutation_performed = 0U') -and
        $harnessText.Contains('\"activationPermitted\":false') -and
        $harnessText.Contains('\"mutationPerformed\":false')) `
    -Detail (
        'Disk-only callback evidence must remain non-authorizing, non-live ' +
        'and non-mutating.')

$compiler = $null
$dumpbin = $null
$callbackCoreBuilt = $false
$callbackDllBuilt = $false
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
    Add-Check `
        -Name 'toolchain.msvc-x64-available' `
        -Passed ($null -ne $compiler -and $null -ne $dumpbin) `
        -Detail $(if ($null -ne $compiler -and $null -ne $dumpbin) {
            "Using $compiler and $dumpbin"
        } else {
            'Visual Studio x64 C++ tools are unavailable.'
        })
}

if ($null -ne $compiler -and $null -ne $dumpbin) {
    Push-Location $temporaryRoot
    try {
        $harnessExecutable = Join-Path $temporaryRoot (
            'jarvis-callwndproc-bridge-harness.exe')
        $compileOutput = @(
            & $compiler `
                /nologo /std:c++20 /O2 /W4 /WX /EHsc /permissive- `
                /Zc:preprocessor /DJARVIS_BRIDGE_CORE_STATIC `
                "/I$callbackRoot" "/I$bridgeRoot" `
                $bridgeCorePath $callbackCorePath $harnessPath `
                "/Fe$harnessExecutable" 2>&1
        )
        $compileExit = $LASTEXITCODE
        $callbackCoreBuilt =
            $compileExit -eq 0 -and
            (Test-Path -LiteralPath $harnessExecutable -PathType Leaf)
        Add-Check `
            -Name 'build.callback-fault-harness' `
            -Passed $callbackCoreBuilt `
            -Detail (
                "Compiler exit $compileExit. " +
                (($compileOutput | Select-Object -Last 12) -join ' '))
        if ($callbackCoreBuilt) {
            $harnessOutput = @(& $harnessExecutable 2>&1)
            $harnessExit = $LASTEXITCODE
            try {
                $harnessReceipt =
                    ($harnessOutput -join [Environment]::NewLine) |
                        ConvertFrom-Json
            }
            catch {
                $harnessReceipt = $null
            }
            Add-Check `
                -Name 'harness.callwndproc-fault-matrix' `
                -Passed (
                    $harnessExit -eq 0 -and
                    $null -ne $harnessReceipt -and
                    $harnessReceipt.result -eq 'passed' -and
                    $harnessReceipt.scenarioCount -ge 12 -and
                    $harnessReceipt.passedCount -eq
                        $harnessReceipt.scenarioCount -and
                    -not $harnessReceipt.windowsCallbackDllExecuted -and
                    -not $harnessReceipt.callbackBodyMutationIncluded -and
                    -not $harnessReceipt.activationPermitted -and
                    -not $harnessReceipt.mutationPerformed -and
                    $harnessReceipt.liveExplorer -eq 'not-run') `
                -Detail $(if ($null -eq $harnessReceipt) {
                    "Harness exit $harnessExit; receipt unavailable."
                } else {
                    "Harness exit $harnessExit; passed " +
                    "$($harnessReceipt.passedCount)/" +
                    "$($harnessReceipt.scenarioCount)."
                })
        }

        $dllPath = Join-Path $temporaryRoot $(if ($isWindows10) {
            'jarvis-win10-explorer-callwndproc-bridge.dll'
        } else {
            'jarvis-explorer-callwndproc-bridge.dll'
        })
        $dllOutput = @(
            & $compiler `
                /nologo /std:c++20 /O2 /W4 /WX /permissive- `
                /Zc:preprocessor /GS- /GR- /Zl /LD `
                /DJARVIS_BRIDGE_CORE_SHARED_INSTANCE `
                "/I$callbackRoot" "/I$bridgeRoot" `
                $bridgeCorePath $callbackCorePath $callbackWindowsPath `
                user32.lib kernel32.lib "/Fe$dllPath" `
                /link /NOENTRY /NODEFAULTLIB 2>&1
        )
        $dllExit = $LASTEXITCODE
        $callbackDllBuilt =
            $dllExit -eq 0 -and
            (Test-Path -LiteralPath $dllPath -PathType Leaf)
        Add-Check `
            -Name 'build.disk-only-shared-callback-dll' `
            -Passed $callbackDllBuilt `
            -Detail (
                "Compiler exit $dllExit. " +
                (($dllOutput | Select-Object -Last 12) -join ' '))
        if ($callbackDllBuilt) {
            $exportOutput = @(& $dumpbin /nologo /exports $dllPath 2>&1)
            $exportExit = $LASTEXITCODE
            $expectedExports = @(
                'JarvisBridge_CallWndProc',
                'JarvisBridge_Initialize',
                'JarvisBridge_QueryContract',
                'JarvisBridge_QueryState',
                'JarvisBridge_Quiesce'
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
            Add-Check `
                -Name 'binary.exact-platform-exports' `
                -Passed (
                    $exportExit -eq 0 -and
                    $actualExports.Count -eq $expectedExports.Count -and
                    $exportDifference.Count -eq 0) `
                -Detail (
                    "dumpbin exit $exportExit; actual exports: " +
                    ($actualExports -join ', '))

            $headerOutput = @(& $dumpbin /nologo /headers $dllPath 2>&1)
            $headerExit = $LASTEXITCODE
            $headerText = $headerOutput -join [Environment]::NewLine
            $sectionMatch = [regex]::Match(
                $headerText,
                '(?is)SECTION HEADER #\d+\s+\.jvbrdg name' +
                '(?<body>.*?)(?=SECTION HEADER #|Summary|\z)')
            $sectionBody = if ($sectionMatch.Success) {
                $sectionMatch.Groups['body'].Value
            } else {
                ''
            }
            Add-Check `
                -Name 'binary.shared-readwrite-nonexecute-bridge-section' `
                -Passed (
                    $headerExit -eq 0 -and $sectionMatch.Success -and
                    $sectionBody -match '(?i)Shared' -and
                    $sectionBody -match '(?i)Read' -and
                    $sectionBody -match '(?i)Write' -and
                    $sectionBody -notmatch '(?i)Execute') `
                -Detail (
                    "dumpbin exit $headerExit; .jvbrdg must be shared, " +
                    'readable, writable and non-executable.')

            Add-Check `
                -Name 'binary.zero-entry-point-no-crt-startup' `
                -Passed (
                    $headerExit -eq 0 -and
                    $headerText -match '(?im)^\s*0 entry point\s*$') `
                -Detail (
                    "dumpbin exit $headerExit; the callback DLL must have " +
                    'a zero PE entry point and no CRT or custom loader startup.')

            $importOutput = @(& $dumpbin /nologo /imports $dllPath 2>&1)
            $importExit = $LASTEXITCODE
            $importText = $importOutput -join [Environment]::NewLine
            $requiredImports = @(
                'CallNextHookEx',
                'GetCurrentProcessId',
                'GetCurrentThreadId'
            )
            $requiredImportsPresent = @(
                $requiredImports | Where-Object {
                    $importText.Contains($_)
                }
            ).Count -eq $requiredImports.Count
            Add-Check `
                -Name 'binary.exact-runtime-identity-and-chain-imports' `
                -Passed (
                    $importExit -eq 0 -and $requiredImportsPresent -and
                    -not [regex]::IsMatch($importText, $forbiddenPattern)) `
                -Detail (
                    "dumpbin exit $importExit; required imports: " +
                    ($requiredImports -join ', '))
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
            'jarvis2-callwndproc-bridge-',
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
    receiptType = 'jarvisv2-callwndproc-bridge-audit'
    platform = $Platform
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    callbackCoreBuilt = $callbackCoreBuilt
    callbackDllBuilt = $callbackDllBuilt
    callbackDllExecuted = $false
    dllSha256 = $dllSha256
    dllBytes = $dllBytes
    scenarioCount = if ($null -eq $harnessReceipt) { 0 } else {
        $harnessReceipt.scenarioCount
    }
    scenarioPassedCount = if ($null -eq $harnessReceipt) { 0 } else {
        $harnessReceipt.passedCount
    }
    sharedBridgeSectionIncluded = $true
    callbackBodyMutationIncluded = $false
    hookInstallerIncluded = $false
    loaderIncluded = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
