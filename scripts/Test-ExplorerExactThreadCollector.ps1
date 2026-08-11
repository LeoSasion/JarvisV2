[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$pinnedPwshPath = Join-Path $root (
    'artifacts\toolchains\powershell-7.6.3\pwsh.exe')
$pwshPath = if (Test-Path -LiteralPath $pinnedPwshPath -PathType Leaf) {
    $pinnedPwshPath
}
elseif (Test-Path -LiteralPath (Join-Path $PSHOME 'pwsh.exe') -PathType Leaf) {
    Join-Path $PSHOME 'pwsh.exe'
}
else {
    (Get-Command pwsh -ErrorAction Stop).Source
}
$zigPath = Join-Path $root (
    'artifacts\toolchains\zig-0.16.0-extract\' +
    'zig-x86_64-windows-0.16.0\zig.exe')
$hasPinnedZig = Test-Path -LiteralPath $zigPath -PathType Leaf
$liveScript = Join-Path $PSScriptRoot (
    'Invoke-ExplorerExactThreadCollectorLive.ps1')
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
$labRoot = Join-Path $root (
    "artifacts\win10-explorer-exact-thread-offline-tests\$runId")
$expectedSourcePaths = @(
    'src/platforms/windows10/Jarvis.Win10.ExplorerBridgeCore/jarvis_explorer_bridge_core.cpp',
    'src/platforms/windows10/Jarvis.Win10.ExplorerBridgeCore/jarvis_explorer_bridge_core.h',
    'src/platforms/windows10/Jarvis.Win10.ExplorerBridgeCore/jarvis_explorer_bridge_core_internal.h',
    'src/platforms/windows10/Jarvis.Win10.ExplorerCallWndProcBridge/jarvis_explorer_callwndproc_bridge.cpp',
    'src/platforms/windows10/Jarvis.Win10.ExplorerCallWndProcBridge/jarvis_explorer_callwndproc_bridge_windows.cpp',
    'src/platforms/windows10/Jarvis.Win10.ExplorerCallWndProcBridge/jarvis_explorer_callwndproc_bridge.h',
    'src/platforms/windows10/Jarvis.Win10.ExplorerCallWndProcBridge/jarvis_explorer_callwndproc_bridge_internal.h',
    'scripts/New-ExplorerExactThreadCollectorPackage.ps1'
)
$checks = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$Passed,
        [Parameter(Mandatory)][string]$Detail
    )
    $checks.Add([pscustomobject]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    })
    if (-not $Passed) {
        $failures.Add("$Name`: $Detail")
    }
}

function Resolve-Package {
    $candidate = if ([IO.Path]::IsPathRooted($PackageDirectory)) {
        [IO.Path]::GetFullPath($PackageDirectory)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $root $PackageDirectory))
    }
    $allowed = [IO.Path]::GetFullPath(
        (Join-Path $root 'artifacts')).TrimEnd('\') + '\'
    if (
        -not $candidate.StartsWith(
            $allowed,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($candidate).StartsWith(
            'win10-explorer-exact-thread-collector-',
            [StringComparison]::Ordinal)
    ) {
        throw 'PackageDirectory is outside the reviewed offline artifact boundary.'
    }
    $candidate
}

function Test-BinaryIdentity {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][pscustomobject]$Identity
    )
    try {
        return (
            (Get-Item -LiteralPath $Path).Length -eq [long]$Identity.bytes -and
            (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.
                ToLowerInvariant() -ceq [string]$Identity.sha256)
    }
    catch {
        return $false
    }
}

function Test-ExactSourceIdentity {
    param([Parameter(Mandatory)][pscustomobject]$Receipt)
    try {
        $properties = @($Receipt.sourceIdentity.PSObject.Properties)
        $actualPaths = @($properties | ForEach-Object { [string]$_.Name })
        if (
            $actualPaths.Count -ne $expectedSourcePaths.Count -or
            @(Compare-Object `
                -ReferenceObject $expectedSourcePaths `
                -DifferenceObject $actualPaths `
                -CaseSensitive).Count -ne 0
        ) {
            throw 'source-path-set-mismatch'
        }
        $computed = [ordered]@{}
        foreach ($relative in $expectedSourcePaths) {
            $identity = $Receipt.sourceIdentity.PSObject.Properties[$relative].Value
            $source = Join-Path $root $relative.Replace('/', '\')
            $item = Get-Item -LiteralPath $source -ErrorAction Stop
            $sha256 = (Get-FileHash `
                -LiteralPath $source `
                -Algorithm SHA256).Hash.ToLowerInvariant()
            if (
                [long]$identity.bytes -ne [long]$item.Length -or
                [string]$identity.sha256 -cne $sha256
            ) {
                throw "source-identity-mismatch:$relative"
            }
            $computed[$relative] = $sha256
        }
        $material = @(
            foreach ($relative in $computed.Keys | Sort-Object) {
                "$relative=$($computed[$relative])"
            }
        ) -join "`n"
        $computedSet = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData(
                [Text.Encoding]::UTF8.GetBytes($material)
            )).ToLowerInvariant()
        if ([string]$Receipt.sourceSetSha256 -cne $computedSet) {
            throw 'source-set-digest-mismatch'
        }
        [pscustomobject]@{ valid = $true; failure = $null }
    }
    catch {
        [pscustomobject]@{ valid = $false; failure = $_.Exception.Message }
    }
}

function Get-ExplorerCallbackMappingObservation {
    $mappings = [Collections.Generic.List[string]]::new()
    $unobservable = [Collections.Generic.List[string]]::new()
    $currentSessionId = (Get-Process -Id $PID).SessionId
    $sessionExplorers = @(
        Get-Process -Name explorer -ErrorAction SilentlyContinue |
            Where-Object SessionId -eq $currentSessionId
    )
    foreach ($process in $sessionExplorers) {
        try {
            foreach ($module in $process.Modules) {
                if (
                    [string]$module.ModuleName -ieq
                        'jarvis-win10-explorer-callwndproc-bridge.dll'
                ) {
                    $mappings.Add("$($process.Id)|$($module.FileName)")
                }
            }
        }
        catch {
            $exited = try { $process.HasExited } catch { $true }
            if (-not $exited) {
                $unobservable.Add("$($process.Id)|$($_.Exception.Message)")
            }
        }
        finally {
            $process.Dispose()
        }
    }
    [pscustomobject]@{
        mappings = @($mappings | Sort-Object)
        unobservable = @($unobservable | Sort-Object)
    }
}

function Get-CollectorProcesses {
    @(
        Get-Process `
            -Name 'jarvis-win10-explorer-exact-thread-collector' `
            -ErrorAction SilentlyContinue |
            ForEach-Object {
                try { "$($_.Id)|$($_.StartTime.ToUniversalTime().ToFileTimeUtc())" }
                finally { $_.Dispose() }
            }
    )
}

[IO.Directory]::CreateDirectory($labRoot) | Out-Null
$resolvedPackage = Resolve-Package
$receiptPath = Join-Path $resolvedPackage 'package-receipt.json'
$modulePath = Join-Path $resolvedPackage (
    'jarvis-win10-explorer-callwndproc-bridge.dll')
$packageReceipt = Get-Content -LiteralPath $receiptPath -Raw |
    ConvertFrom-Json -Depth 20
$packageFiles = @(Get-ChildItem -LiteralPath $resolvedPackage -File)
$packageNames = @($packageFiles.Name | Sort-Object)
$expectedPackageNames = @(
    'jarvis-win10-explorer-callwndproc-bridge.dll',
    'package-receipt.json'
) | Sort-Object

Add-Check `
    -Name 'package.exact-dll-and-receipt-fileset' `
    -Passed (
        $packageFiles.Count -eq 2 -and
        @(Compare-Object $expectedPackageNames $packageNames -CaseSensitive).
            Count -eq 0 -and
        [int]$packageReceipt.packageFileCount -eq 2 -and
        @(Compare-Object `
            $expectedPackageNames `
            @($packageReceipt.packageFileSet | Sort-Object) `
            -CaseSensitive).Count -eq 0) `
    -Detail 'The offline package must contain exactly the callback DLL and receipt.'

Add-Check `
    -Name 'package.offline-only-contract' `
    -Passed (
        [string]$packageReceipt.result -ceq 'passed' -and
        [bool]$packageReceipt.offlineOnly -and
        -not [bool]$packageReceipt.collectorExecutablePublished -and
        -not [bool]$packageReceipt.callbackDllExecuted -and
        -not [bool]$packageReceipt.activationPermitted -and
        [string]$packageReceipt.liveExplorer -ceq 'not-run' -and
        -not [bool]$packageReceipt.mutationPerformed) `
    -Detail 'The receipt must make the fixed offline-only boundary explicit.'

Add-Check `
    -Name 'package.no-executable-anywhere' `
    -Passed (
        @(Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File |
            Where-Object Extension -ieq '.exe').Count -eq 0) `
    -Detail 'No collector or other executable may be published in the package.'

Add-Check `
    -Name 'package.callback-bytes-and-hash-match' `
    -Passed (Test-BinaryIdentity $modulePath $packageReceipt.callbackDll) `
    -Detail 'The callback DLL must match its immutable byte count and SHA-256.'

$expectedExports = @(
    'JarvisBridge_AcquireSharedInstance',
    'JarvisBridge_CallWndProc',
    'JarvisBridge_Initialize',
    'JarvisBridge_QueryContract',
    'JarvisBridge_QueryState',
    'JarvisBridge_Quiesce'
) | Sort-Object
Add-Check `
    -Name 'package.callback-offline-pe-contract' `
    -Passed (
        [bool]$packageReceipt.callbackDll.zeroEntryPoint -and
        [bool]$packageReceipt.callbackDll.bridgeSection.shared -and
        [bool]$packageReceipt.callbackDll.bridgeSection.readable -and
        [bool]$packageReceipt.callbackDll.bridgeSection.writable -and
        -not [bool]$packageReceipt.callbackDll.bridgeSection.executable -and
        @(Compare-Object `
            $expectedExports `
            @($packageReceipt.callbackDll.exports | Sort-Object) `
            -CaseSensitive).Count -eq 0) `
    -Detail 'The disk-only callback must retain the reviewed no-entry PE contract.'

$sourceValidation = Test-ExactSourceIdentity $packageReceipt
Add-Check `
    -Name 'package.exact-eight-source-set' `
    -Passed ([bool]$sourceValidation.valid) `
    -Detail $(if ($sourceValidation.valid) {
        'Eight linked/published DLL sources plus builder match bytes, hashes and set digest.'
    } else { [string]$sourceValidation.failure })

$receiptJson = $packageReceipt | ConvertTo-Json -Depth 20
$firstSource = $expectedSourcePaths[0]
$forged = $receiptJson | ConvertFrom-Json -Depth 20
$forged.sourceIdentity.PSObject.Properties[$firstSource].Value.sha256 = '0' * 64
$truncated = $receiptJson | ConvertFrom-Json -Depth 20
$truncated.sourceIdentity.PSObject.Properties.Remove($firstSource)
$extra = $receiptJson | ConvertFrom-Json -Depth 20
$extra.sourceIdentity | Add-Member `
    -NotePropertyName 'src/platforms/windows10/unpublished-extra.cpp' `
    -NotePropertyValue ([pscustomobject]@{ bytes = 1; sha256 = ('0' * 64) })
Add-Check `
    -Name 'package.forged-source-identity-rejected' `
    -Passed (-not (Test-ExactSourceIdentity $forged).valid) `
    -Detail 'A forged source identity must fail closed.'
Add-Check `
    -Name 'package.truncated-source-identity-rejected' `
    -Passed (-not (Test-ExactSourceIdentity $truncated).valid) `
    -Detail 'A truncated source identity must fail closed.'
Add-Check `
    -Name 'package.extra-source-identity-rejected' `
    -Passed (-not (Test-ExactSourceIdentity $extra).valid) `
    -Detail 'An extra source identity must fail closed.'

$blockedRoot = Join-Path $labRoot 'blocked-entry'
$localAppData = Join-Path $blockedRoot 'localappdata'
$stateRoot = Join-Path $localAppData 'JARVIS2'
$controllerReceiptRoot = Join-Path $blockedRoot 'receipt'
[IO.Directory]::CreateDirectory($stateRoot) | Out-Null
[IO.Directory]::CreateDirectory($controllerReceiptRoot) | Out-Null
$killSwitchPath = Join-Path $stateRoot 'disabled.flag'
$permitPath = Join-Path $stateRoot 'active-module.txt'
$killBytes = [byte[]](0, 255, 17, 34, 51, 68)
$permitBytes = [Text.Encoding]::UTF8.GetBytes('preserve-this-permit-byte-for-byte')
[IO.File]::WriteAllBytes($killSwitchPath, $killBytes)
[IO.File]::WriteAllBytes($permitPath, $permitBytes)
$stateNamesBefore = @(Get-ChildItem -LiteralPath $stateRoot -File).Name | Sort-Object
$mappingBefore = Get-ExplorerCallbackMappingObservation
$collectorBefore = @(Get-CollectorProcesses)
$controllerReceiptPath = Join-Path $controllerReceiptRoot 'controller-receipt.json'
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:LOCALAPPDATA = $localAppData
    $blockedOutput = @(& $pwshPath -NoLogo -NoProfile -File $liveScript `
        -PackageDirectory $resolvedPackage `
        -ControllerReceiptPath $controllerReceiptPath 2>&1)
    $blockedExit = $LASTEXITCODE
}
finally {
    $env:LOCALAPPDATA = $previousLocalAppData
}
$mappingAfter = Get-ExplorerCallbackMappingObservation
$collectorAfter = @(Get-CollectorProcesses)
$stateNamesAfter = @(Get-ChildItem -LiteralPath $stateRoot -File).Name | Sort-Object
$blockedReceipt = if (Test-Path -LiteralPath $controllerReceiptPath) {
    Get-Content -LiteralPath $controllerReceiptPath -Raw | ConvertFrom-Json
} else {
    $null
}
$mappingIssues = @($mappingBefore.unobservable) + @($mappingAfter.unobservable)
Add-Check `
    -Name 'live.official-entry-is-fixed-blocked' `
    -Passed (
        $blockedExit -ne 0 -and $null -ne $blockedReceipt -and
        [string]$blockedReceipt.result -ceq 'blocked' -and
        [string]$blockedReceipt.blockReason -match 'unload.*callback-drain' -and
        [bool]$blockedReceipt.offlineOnly -and
        -not [bool]$blockedReceipt.collectorExecutablePublished -and
        -not [bool]$blockedReceipt.activationPermitted -and
        [string]$blockedReceipt.liveExplorer -ceq 'not-run' -and
        -not [bool]$blockedReceipt.mutationPerformed) `
    -Detail 'The official entry must emit only a blocked receipt and return nonzero.'
Add-Check `
    -Name 'live.blocked-entry-preserves-state-byte-for-byte' `
    -Passed (
        [Convert]::ToHexString([IO.File]::ReadAllBytes($killSwitchPath)) -ceq
            [Convert]::ToHexString($killBytes) -and
        [Convert]::ToHexString([IO.File]::ReadAllBytes($permitPath)) -ceq
            [Convert]::ToHexString($permitBytes) -and
        @(Compare-Object $stateNamesBefore $stateNamesAfter -CaseSensitive).
            Count -eq 0) `
    -Detail 'Blocked invocation must not rearm, clear, replace or add state files.'
Add-Check `
    -Name 'live.blocked-entry-creates-no-explorer-mapping' `
    -Passed (
        $mappingBefore.unobservable.Count -eq 0 -and
        $mappingAfter.unobservable.Count -eq 0 -and
        $mappingBefore.mappings.Count -eq 0 -and
        $mappingAfter.mappings.Count -eq 0) `
    -Detail (
        'The callback DLL must remain unmapped and every stable Explorer ' +
        'process must remain observable before and after the blocked entry. ' +
        "before=$($mappingBefore.mappings.Count)/" +
        "$($mappingBefore.unobservable.Count), " +
        "after=$($mappingAfter.mappings.Count)/" +
        "$($mappingAfter.unobservable.Count); " +
        "issues=$($mappingIssues -join ' | ').")
Add-Check `
    -Name 'live.blocked-entry-starts-no-collector-process' `
    -Passed (
        $collectorBefore.Count -eq 0 -and $collectorAfter.Count -eq 0) `
    -Detail 'The offline suite and official entry must not start a collector process.'

$auditReceipts = @{}
foreach ($audit in @(
    'Test-ExplorerBridgeCore.ps1',
    'Test-ExplorerCallWndProcBridge.ps1',
    'Test-ExplorerExactThreadTransport.ps1'
)) {
    $auditArguments = @(
        '-NoLogo', '-NoProfile', '-File', (Join-Path $PSScriptRoot $audit),
        '-Platform', 'windows10')
    if ($hasPinnedZig) {
        $auditArguments += '-StaticOnly'
    }
    $output = @(& $pwshPath @auditArguments 2>&1)
    $exitCode = $LASTEXITCODE
    $parsed = $null
    try {
        $parsed = ($output -join [Environment]::NewLine) |
            ConvertFrom-Json -Depth 20
    }
    catch {
        $parsed = $null
    }
    $auditKey = [IO.Path]::GetFileNameWithoutExtension($audit)
    $auditReceipts[$auditKey] = $parsed
    Add-Check `
        -Name ('audit.' + [IO.Path]::GetFileNameWithoutExtension($audit)) `
        -Passed (
            $exitCode -eq 0 -and $null -ne $parsed -and
            [string]$parsed.result -ceq 'passed') `
        -Detail $(if ($null -eq $parsed) {
            ($output | Select-Object -Last 10) -join ' '
        } else {
            "$($parsed.passedCount)/$($parsed.checkCount) checks passed."
        })
}

$bridgeRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ExplorerBridgeCore')
$callbackRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ExplorerCallWndProcBridge')
$transportRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ExplorerExactThreadTransport')
$harnesses = @(
    [pscustomobject]@{
        Name = 'bridge-core'
        AuditKey = 'Test-ExplorerBridgeCore'
        ExpectedScenarios = 38
        Includes = @($bridgeRoot)
        Sources = @(
            (Join-Path $bridgeRoot 'jarvis_explorer_bridge_core.cpp'),
            (Join-Path $root (
                'tests\native\windows10\' +
                'jarvis_win10_explorer_bridge_core_harness.cpp')))
    },
    [pscustomobject]@{
        Name = 'callwndproc-bridge'
        AuditKey = 'Test-ExplorerCallWndProcBridge'
        ExpectedScenarios = 13
        Includes = @($bridgeRoot, $callbackRoot)
        Sources = @(
            (Join-Path $bridgeRoot 'jarvis_explorer_bridge_core.cpp'),
            (Join-Path $callbackRoot 'jarvis_explorer_callwndproc_bridge.cpp'),
            (Join-Path $root (
                'tests\native\windows10\' +
                'jarvis_win10_explorer_callwndproc_bridge_harness.cpp')))
    },
    [pscustomobject]@{
        Name = 'exact-thread-transport'
        AuditKey = 'Test-ExplorerExactThreadTransport'
        ExpectedScenarios = 39
        Includes = @($bridgeRoot, $transportRoot)
        Sources = @(
            (Join-Path $bridgeRoot 'jarvis_explorer_bridge_core.cpp'),
            (Join-Path $transportRoot (
                'jarvis_explorer_exact_thread_transport.cpp')),
            (Join-Path $root (
                'tests\native\windows10\' +
                'jarvis_win10_explorer_exact_thread_transport_harness.cpp')))
    }
)
if ($hasPinnedZig) {
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $root (
        'artifacts\toolchains\zig-cache\global')
    $env:ZIG_LOCAL_CACHE_DIR = Join-Path $root (
        'artifacts\toolchains\zig-cache\local')
    foreach ($harness in $harnesses) {
    $executable = Join-Path $labRoot "$($harness.Name)-harness.exe"
    $arguments = @(
        'c++', '-target', 'x86_64-windows-gnu', '-std=c++20', '-O2',
        '-Wall', '-Wextra', '-Werror', '-Wno-nullability-completeness',
        '-Wno-unknown-pragmas', '-DJARVIS_BRIDGE_CORE_STATIC')
    foreach ($include in $harness.Includes) {
        $arguments += "-I$include"
    }
    $arguments += @($harness.Sources)
    $arguments += @('-o', $executable)
    $compileOutput = @(& $zigPath @arguments 2>&1)
    $compileExit = $LASTEXITCODE
    $harnessReceipt = $null
    $harnessExit = -1
    if ($compileExit -eq 0 -and (Test-Path -LiteralPath $executable)) {
        $harnessOutput = @(& $executable 2>&1)
        $harnessExit = $LASTEXITCODE
        try {
            $harnessReceipt = ($harnessOutput -join [Environment]::NewLine) |
                ConvertFrom-Json -Depth 20
        }
        catch {
            $harnessReceipt = $null
        }
    }
        Add-Check `
            -Name "zig-harness.$($harness.Name)" `
        -Passed (
            $compileExit -eq 0 -and $harnessExit -eq 0 -and
            $null -ne $harnessReceipt -and
            [string]$harnessReceipt.result -ceq 'passed' -and
            [int]$harnessReceipt.scenarioCount -eq $harness.ExpectedScenarios -and
            [int]$harnessReceipt.passedCount -eq $harness.ExpectedScenarios) `
            -Detail $(if ($null -ne $harnessReceipt) {
                "$($harnessReceipt.passedCount)/" +
                    "$($harnessReceipt.scenarioCount) scenarios passed."
            } else {
                "compile=$compileExit run=$harnessExit " +
                    (($compileOutput | Select-Object -Last 8) -join ' ')
            })
    }
}
else {
    foreach ($harness in $harnesses) {
        $auditReceipt = $auditReceipts[$harness.AuditKey]
        Add-Check `
            -Name "msvc-harness.$($harness.Name)" `
            -Passed (
                $null -ne $auditReceipt -and
                -not [bool]$auditReceipt.staticOnly -and
                [string]$auditReceipt.result -ceq 'passed' -and
                [int]$auditReceipt.scenarioCount -eq
                    $harness.ExpectedScenarios -and
                [int]$auditReceipt.scenarioPassedCount -eq
                    $harness.ExpectedScenarios) `
            -Detail $(if ($null -eq $auditReceipt) {
                'The full MSVC audit receipt was unavailable.'
            } else {
                "$($auditReceipt.scenarioPassedCount)/" +
                    "$($auditReceipt.scenarioCount) MSVC scenarios passed."
            })
    }
}

$passed = $failures.Count -eq 0
$testReceipt = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-explorer-exact-thread-offline-tests'
    result = if ($passed) { 'passed' } else { 'failed' }
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    packageDirectory = [IO.Path]::GetRelativePath(
        $root,
        $resolvedPackage).Replace('\', '/')
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    offlineOnly = $true
    collectorExecutablePublished = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
}
$testReceiptPath = Join-Path $labRoot 'test-receipt.json'
[IO.File]::WriteAllText(
    $testReceiptPath,
    ($testReceipt | ConvertTo-Json -Depth 10) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
$testReceipt | ConvertTo-Json -Depth 10
if (-not $passed) {
    exit 1
}
