[CmdletBinding()]
param(
    [switch]$StaticOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$transportRoot = Join-Path $root (
    'src\platforms\windows11\Jarvis.ExplorerExactThreadTransport')
$bridgeRoot = Join-Path $root (
    'src\platforms\windows11\Jarvis.ExplorerBridgeCore')
$headerPath = Join-Path $transportRoot (
    'jarvis_explorer_exact_thread_transport.h')
$internalPath = Join-Path $transportRoot (
    'jarvis_explorer_exact_thread_transport_internal.h')
$corePath = Join-Path $transportRoot (
    'jarvis_explorer_exact_thread_transport.cpp')
$windowsPath = Join-Path $transportRoot (
    'jarvis_explorer_exact_thread_transport_windows.cpp')
$bridgeCorePath = Join-Path $bridgeRoot 'jarvis_explorer_bridge_core.cpp'
$harnessPath = Join-Path $root (
    'tests\native\windows11\' +
    'jarvis_explorer_exact_thread_transport_harness.cpp')
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'jarvis2-exact-thread-transport-' + [Guid]::NewGuid().ToString('N'))
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
    $headerPath,
    $internalPath,
    $corePath,
    $windowsPath,
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
    -Detail 'The transport, Win32 adapter, bridge core and harness must exist.'
if ($missingPaths.Count -ne 0) {
    [pscustomobject]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-exact-thread-transport-audit'
        result = 'failed'
        staticOnly = [bool]$StaticOnly
        checkCount = $checks.Count
        passedCount = @($checks | Where-Object passed).Count
        transportCoreBuilt = $false
        windowsAdapterBuilt = $false
        windowsAdapterExecuted = $false
        liveExplorer = 'not-run'
        activationPermitted = $false
        mutationPerformed = $false
        checks = $checks
        failures = $failures
    } | ConvertTo-Json -Depth 10
    exit 1
}

$headerText = [IO.File]::ReadAllText($headerPath)
$internalText = [IO.File]::ReadAllText($internalPath)
$coreText = [IO.File]::ReadAllText($corePath)
$windowsText = [IO.File]::ReadAllText($windowsPath)
$harnessText = [IO.File]::ReadAllText($harnessPath)
$productionText = @(
    $headerText,
    $internalText,
    $coreText,
    $windowsText
) -join [Environment]::NewLine
$sourceText = @(
    $headerText,
    $internalText,
    $coreText,
    $windowsText,
    $harnessText
) -join [Environment]::NewLine

$forbiddenScopePattern = (
    '(?i)\b(?:OpenProcess|CreateRemoteThread|VirtualAllocEx|' +
    'WriteProcessMemory|ReadProcessMemory|CreateToolhelp32Snapshot|' +
    'EnumProcesses|Process32First|Process32Next|NtGetNextProcess|' +
    'StartService|RegOpenKey|TerminateProcess|GetShellWindow|' +
    'FindWindow|EnumWindows|LoadLibrary|GetProcAddress)\b'
)
Add-Check `
    -Name 'source.no-discovery-loader-remote-service-or-registry-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenScopePattern)) `
    -Detail (
        'The transport may consume one exact target but must not discover, ' +
        'load, enumerate, open, terminate or configure processes or services.')

Add-Check `
    -Name 'contract.fixed-layout-and-exact-identity' `
    -Passed (
        $headerText.Contains(
            'static_assert(sizeof(jarvis_exact_thread_transport_request) == 80U)') -and
        $headerText.Contains(
            'static_assert(sizeof(jarvis_exact_thread_transport_response) == 80U)') -and
        $headerText.Contains('std::uint32_t explorer_process_id') -and
        $headerText.Contains('std::uint32_t shell_thread_id') -and
        $headerText.Contains('std::uint64_t shell_window_handle') -and
        $headerText.Contains('std::uint64_t session_nonce')) `
    -Detail (
        'ABI v1 must bind fixed-size PID, nonzero TID, HWND, module, hook ' +
        'procedure and session identity.')

Add-Check `
    -Name 'admission.kill-switch-permit-scope-and-architecture-required' `
    -Passed (
        $coreText.Contains('host_admission_passed != 1U') -and
        $coreText.Contains('kill_switch_armed != 1U') -and
        $coreText.Contains('one_shot_permit_valid != 1U') -and
        $coreText.Contains('JARVIS_EXACT_THREAD_SCOPE') -and
        $coreText.Contains('architecture_match != 1U')) `
    -Detail (
        'Preparation must require the exact admission, armed kill switch, ' +
        'one-shot permit, thread scope and architecture match.')

$setHookPattern = (
    '(?s)SetWindowsHookExW\(\s*WH_CALLWNDPROC,.*?' +
    'shell_thread_id\s*\)')
Add-Check `
    -Name 'windows.exact-nonzero-thread-callwndproc-only' `
    -Passed (
        [regex]::IsMatch($windowsText, $setHookPattern) -and
        $windowsText.Contains('shell_thread_id == 0U') -and
        $windowsText.Contains('GetWindowThreadProcessId') -and
        $windowsText.Contains('observed_process_id == explorer_process_id') -and
        $windowsText.Contains('observed_thread_id == shell_thread_id') -and
        -not [regex]::IsMatch(
            $windowsText,
            '(?s)SetWindowsHookExW\([^;]*,\s*0\s*\)')) `
    -Detail (
        'The Win32 adapter must validate HWND ownership and install only ' +
        'WH_CALLWNDPROC on the supplied nonzero thread ID.')

Add-Check `
    -Name 'lifecycle.bridge-identity-and-publication-bound' `
    -Passed (
        $coreText.Contains(
            'bridge->explorer_process_id != request->explorer_process_id') -and
        $coreText.Contains(
            'bridge->shell_thread_id != request->shell_thread_id') -and
        $coreText.Contains(
            'bridge->session_nonce != request->session_nonce') -and
        $coreText.Contains('jarvis_bridge_core_publish_transport') -and
        $coreText.Contains('jarvis_bridge_core_begin_quiesce')) `
    -Detail (
        'Transport preparation, publication and drain must share the exact ' +
        'Phase 18 bridge identity and lifecycle.')

Add-Check `
    -Name 'lifecycle.install-quiesce-race-closes' `
    -Passed (
        $internalText.Contains('cancel_requested') -and
        $internalText.Contains('install_in_flight') -and
        $coreText.Contains('CompleteUnhook') -and
        $coreText.Contains('A contradictory platform result') -and
        $harnessText.Contains('failure_handle_nonzero') -and
        $harnessText.Contains('block_install') -and
        $harnessText.Contains('release_install')) `
    -Detail (
        'A quiesce racing a blocked install must close the bridge, wait for ' +
        'installation to return and remove any resulting hook exactly once.')

Add-Check `
    -Name 'lifecycle.unhook-does-not-imply-unload' `
    -Passed (
        $windowsText.Contains('UnhookWindowsHookEx') -and
        $coreText.Contains('hook_entry_published.store(1U') -and
        $coreText.Contains('module_pin_required') -and
        $coreText.Contains('hook_entry_published == 0U') -and
        $harnessText.Contains('response.module_pin_required == 1U') -and
        $harnessText.Contains('response.unload_permitted == 0U')) `
    -Detail (
        'Once a system hook entry exists, successful unhook must retain the ' +
        'module pin because an in-flight callback may still be executing.')

Add-Check `
    -Name 'boundary.no-runnable-controller-or-loader-entry' `
    -Passed (
        -not $productionText.Contains('int main(') -and
        $harnessText.Contains('int main(')) `
    -Detail (
        'Production sources expose no main, DllMain, loader, command line or ' +
        'automatic installation entry; the only main belongs to the harness.')
Add-Check `
    -Name 'boundary.no-dllmain-or-exported-hook-yet' `
    -Passed (
        -not $headerText.Contains('__declspec(dllexport)') -and
        -not $windowsText.Contains('DllMain') -and
        -not $windowsText.Contains('CallNextHookEx')) `
    -Detail (
        'Phase 19 stops before a loadable callback DLL; hook chaining and the ' +
        'exported procedure require a separate review.')

Add-Check `
    -Name 'receipt.synthetic-and-live-truthfulness' `
    -Passed (
        $coreText.Contains('JARVIS_TRANSPORT_EXECUTION_WINDOWS_LIVE') -and
        $coreText.Contains('.activation_permitted = 0U') -and
        $coreText.Contains('.mutation_performed = 0U') -and
        $harnessText.Contains('response.live_explorer_touched == 1U') -and
        $harnessText.Contains('\"windowsAdapterExecuted\":false') -and
        $harnessText.Contains('\"liveExplorer\":\"not-run\"')) `
    -Detail (
        'Synthetic tests stay non-live while the real adapter path cannot ' +
        'inherit a false no-contact receipt if it is ever executed.')

$compiler = $null
$dumpbin = $null
$transportCoreBuilt = $false
$windowsAdapterBuilt = $false
$harnessReceipt = $null
$adapterSha256 = $null
$adapterBytes = 0
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
            'jarvis-exact-thread-transport-harness.exe')
        $compileOutput = @(
            & $compiler `
                /nologo /std:c++20 /O2 /W4 /WX /EHsc /permissive- `
                /Zc:preprocessor /DJARVIS_BRIDGE_CORE_STATIC `
                "/I$transportRoot" "/I$bridgeRoot" `
                $bridgeCorePath $corePath $harnessPath `
                "/Fe$harnessExecutable" 2>&1
        )
        $compileExit = $LASTEXITCODE
        $transportCoreBuilt =
            $compileExit -eq 0 -and
            (Test-Path -LiteralPath $harnessExecutable -PathType Leaf)
        Add-Check `
            -Name 'build.transport-fault-harness' `
            -Passed $transportCoreBuilt `
            -Detail (
                "Compiler exit $compileExit. " +
                (($compileOutput | Select-Object -Last 12) -join ' '))
        if ($transportCoreBuilt) {
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
                -Name 'harness.exact-thread-fault-matrix' `
                -Passed (
                    $harnessExit -eq 0 -and
                    $null -ne $harnessReceipt -and
                    $harnessReceipt.result -eq 'passed' -and
                    $harnessReceipt.scenarioCount -ge 30 -and
                    $harnessReceipt.passedCount -eq
                        $harnessReceipt.scenarioCount -and
                    -not $harnessReceipt.windowsAdapterExecuted -and
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

        $adapterObject = Join-Path $temporaryRoot (
            'jarvis-exact-thread-transport-windows.obj')
        $adapterOutput = @(
            & $compiler `
                /nologo /std:c++20 /O2 /W4 /WX /EHsc /permissive- `
                /Zc:preprocessor /c "/I$transportRoot" "/I$bridgeRoot" `
                $windowsPath "/Fo$adapterObject" 2>&1
        )
        $adapterExit = $LASTEXITCODE
        $windowsAdapterBuilt =
            $adapterExit -eq 0 -and
            (Test-Path -LiteralPath $adapterObject -PathType Leaf)
        Add-Check `
            -Name 'build.unlinked-windows-adapter-object' `
            -Passed $windowsAdapterBuilt `
            -Detail (
                "Compiler exit $adapterExit. " +
                (($adapterOutput | Select-Object -Last 12) -join ' '))
        if ($windowsAdapterBuilt) {
            $symbolOutput = @(& $dumpbin /nologo /symbols $adapterObject 2>&1)
            $symbolExit = $LASTEXITCODE
            $symbolText = $symbolOutput -join [Environment]::NewLine
            $requiredImports = @(
                'GetWindowThreadProcessId',
                'SetWindowsHookExW',
                'UnhookWindowsHookEx'
            )
            $requiredImportsPresent = @(
                $requiredImports | Where-Object {
                    $symbolText.Contains($_)
                }
            ).Count -eq $requiredImports.Count
            $forbiddenImportsPresent = [regex]::IsMatch(
                $symbolText,
                $forbiddenScopePattern)
            Add-Check `
                -Name 'binary.exact-reviewed-win32-symbol-boundary' `
                -Passed (
                    $symbolExit -eq 0 -and $requiredImportsPresent -and
                    -not $forbiddenImportsPresent) `
                -Detail (
                    "dumpbin exit $symbolExit; required symbols: " +
                    ($requiredImports -join ', '))
            $adapterItem = Get-Item -LiteralPath $adapterObject
            $adapterBytes = $adapterItem.Length
            $adapterSha256 = (
                Get-FileHash -LiteralPath $adapterObject -Algorithm SHA256
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
            'jarvis2-exact-thread-transport-',
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
    receiptType = 'jarvisv2-exact-thread-transport-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    transportCoreBuilt = $transportCoreBuilt
    windowsAdapterBuilt = $windowsAdapterBuilt
    windowsAdapterExecuted = $false
    adapterObjectSha256 = $adapterSha256
    adapterObjectBytes = $adapterBytes
    scenarioCount = if ($null -eq $harnessReceipt) { 0 } else {
        $harnessReceipt.scenarioCount
    }
    scenarioPassedCount = if ($null -eq $harnessReceipt) { 0 } else {
        $harnessReceipt.passedCount
    }
    exactThreadScope = $true
    globalHookIncluded = $false
    loaderIncluded = $false
    runnableControllerIncluded = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
