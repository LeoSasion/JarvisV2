[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.OwnedTaskManager')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.OwnedTaskManager.csproj')
$sourcePath = Join-Path $sourceRoot 'WindowsProcessSnapshotSource.cs'
$samplerPath = Join-Path $sourceRoot 'TaskSnapshotSampler.cs'
$contractsPath = Join-Path $sourceRoot 'Contracts.cs'
$scenarioPath = Join-Path $sourceRoot 'ModelScenarios.cs'
$programPath = Join-Path $sourceRoot 'Program.cs'
$contractTestRoot = Join-Path $sourceRoot 'Tests'
$contractTestProjectPath = Join-Path $contractTestRoot (
    'Jarvis.Win10.OwnedTaskManager.ContractTests.csproj')
$contractTestProgramPath = Join-Path $contractTestRoot 'Program.cs'

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

$requiredFiles = @(
    $projectPath,
    $sourcePath,
    $samplerPath,
    $contractsPath,
    $scenarioPath,
    $programPath,
    $contractTestProjectPath,
    $contractTestProgramPath
)
Add-Check `
    -Name 'layout.required-source-set' `
    -Passed (@($requiredFiles | Where-Object {
        -not (Test-Path -LiteralPath $_ -PathType Leaf)
    }).Count -eq 0) `
    -Detail 'The Win10-owned model, source, scenarios and CLI must all exist.'

$allSourceText = @(
    Get-ChildItem -LiteralPath $sourceRoot -File -Filter *.cs -Recurse |
        Where-Object FullName -NotMatch '\\(?:bin|obj)\\' |
        Sort-Object Name |
        ForEach-Object { [IO.File]::ReadAllText($_.FullName) }
) -join [Environment]::NewLine
$projectText = [IO.File]::ReadAllText($projectPath)
$sourceText = [IO.File]::ReadAllText($sourcePath)
$samplerText = [IO.File]::ReadAllText($samplerPath)
$contractsText = [IO.File]::ReadAllText($contractsPath)
$scenarioText = [IO.File]::ReadAllText($scenarioPath)
$programText = [IO.File]::ReadAllText($programPath)

Add-Check `
    -Name 'project.windows10-owned-executable' `
    -Passed (
        $projectText.Contains('<OutputType>Exe</OutputType>') -and
        $projectText.Contains(
            '<TargetFramework>net8.0-windows</TargetFramework>') -and
        $projectText.Contains(
            '<AssemblyName>jarvis-win10-owned-task-manager</AssemblyName>') -and
        -not $projectText.Contains('windows11')) `
    -Detail (
        'The sampler must remain an independently runnable Win10-owned ' +
        'net8.0-windows project.')

$forbiddenCapabilityPattern = (
    '(?i)(?:\b(?:DllImport|LibraryImport|NativeLibrary|LoadLibrary|' +
    'FreeLibrary|GetProcAddress|OpenProcess|Terminate\w*|' +
    'SetPriority\w*|ProcessPriorityClass|ProcessorAffinity|' +
    'PriorityBoostEnabled|ProcessThread|ProcessStartInfo|' +
    'CloseMainWindow|SetWindowPos|SetWindowLong|' +
    'SendMessage|PostMessage|SetWindowsHookEx|CreateRemoteThread|' +
    'WriteProcessMemory|ShellExecute|GetDelegateForFunctionPointer|' +
    'Windhawk)\b|' +
    '\bProcess\s*\.\s*(?:Kill|Start|GetProcessById|' +
    'EnterDebugMode|LeaveDebugMode)\b|\.\s*Kill\s*\()'
)
Add-Check `
    -Name 'source.read-only-capability-boundary' `
    -Passed (-not [regex]::IsMatch(
        $allSourceText,
        $forbiddenCapabilityPattern)) `
    -Detail (
        'The model may inspect process counters only; process control, ' +
        'window control, native loading and injection APIs are forbidden.')

Add-Check `
    -Name 'source.real-bounded-process-counters' `
    -Passed (
        $sourceText.Contains('Process.GetProcesses()') -and
        $sourceText.Contains('_process.ProcessName') -and
        $sourceText.Contains('_process.Id') -and
        $sourceText.Contains('_process.TotalProcessorTime.Ticks') -and
        $sourceText.Contains('_process.WorkingSet64') -and
        $sourceText.Contains('_process.Threads.Count') -and
        $sourceText.Contains('UnauthorizedAccessException') -and
        $sourceText.Contains('InvalidOperationException') -and
        $contractsText.Contains('IProcessCounterSource') -and
        $samplerText.Contains('AbsoluteMaximumProcessCount = 256') -and
        $programText.Contains('snapshot')) `
    -Detail (
        'Production capture must expose the required real counters through ' +
        'an injectable source and enforce a hard output bound.')

$requiredScenarioNames = @(
    'first-frame-establishes-cpu-baseline',
    'cpu-delta-is-normalized-by-core-count',
    'pid-reuse-requires-a-new-baseline',
    'access-denied-and-process-exit-are-skipped',
    'sort-order-is-stable-and-deterministic',
    'result-count-is-strictly-bounded'
)
Add-Check `
    -Name 'model.deterministic-scenario-contract' `
    -Passed (@($requiredScenarioNames | Where-Object {
        -not $scenarioText.Contains($_)
    }).Count -eq 0) `
    -Detail (
        'The deterministic model suite must cover baseline, deltas, PID ' +
        'reuse, expected read failures, stable sorting and result limits.')

$buildOutput = @(
    & $DotnetPath build `
        $projectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 12) -join
        [Environment]::NewLine)

$testReceipt = $null
$contractTestReceipt = $null
$snapshotReceipt = $null
if ($buildExitCode -eq 0) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-owned-task-manager.dll')

    $testOutput = @(& $DotnetPath $assemblyPath test-model 2>&1)
    $testExitCode = $LASTEXITCODE
    try {
        $testReceipt =
            ($testOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $testReceipt = $null
    }
    Add-Check `
        -Name 'model.scenarios' `
        -Passed (
            $testExitCode -eq 0 -and
            $null -ne $testReceipt -and
            $testReceipt.result -eq 'passed' -and
            $testReceipt.scenarioCount -eq 6 -and
            $testReceipt.passedCount -eq 6 -and
            $testReceipt.sourceInjectable -and
            $testReceipt.readOnlyProcessTelemetry -and
            -not $testReceipt.processControlSupported -and
            -not $testReceipt.windowControlSupported -and
            -not $testReceipt.injectionSupported -and
            -not $testReceipt.mutationPerformed) `
        -Detail (
            "Model test exit $testExitCode; receipt=" +
            $(if ($null -eq $testReceipt) {
                'unavailable'
            }
            else {
                "$($testReceipt.passedCount)/$($testReceipt.scenarioCount)"
            }))

    $contractTestBuildOutput = @(
        & $DotnetPath build `
            $contractTestProjectPath `
            --configuration Release `
            --nologo `
            --warnaserror 2>&1
    )
    $contractTestBuildExitCode = $LASTEXITCODE
    $contractTestAssemblyPath = Join-Path $contractTestRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-owned-task-manager-contract-tests.dll')
    $contractTestOutput = @()
    $contractTestExitCode = -1
    if ($contractTestBuildExitCode -eq 0) {
        $contractTestOutput = @(
            & $DotnetPath $contractTestAssemblyPath 2>&1
        )
        $contractTestExitCode = $LASTEXITCODE
        try {
            $contractTestReceipt =
                ($contractTestOutput -join [Environment]::NewLine) |
                    ConvertFrom-Json
        }
        catch {
            $contractTestReceipt = $null
        }
    }
    Add-Check `
        -Name 'model.external-contract-scenarios' `
        -Passed (
            $contractTestBuildExitCode -eq 0 -and
            $contractTestExitCode -eq 0 -and
            $null -ne $contractTestReceipt -and
            $contractTestReceipt.result -eq 'passed' -and
            $contractTestReceipt.scenarioCount -eq 6 -and
            $contractTestReceipt.passedCount -eq 6) `
        -Detail (
            "External contract build $contractTestBuildExitCode, " +
            "test $contractTestExitCode; receipt=" +
            $(if ($null -eq $contractTestReceipt) {
                'unavailable'
            }
            else {
                "$($contractTestReceipt.passedCount)/" +
                "$($contractTestReceipt.scenarioCount)"
            }))

    $snapshotOutput = @(
        & $DotnetPath $assemblyPath `
            snapshot --limit 8 --interval-ms 100 2>&1
    )
    $snapshotExitCode = $LASTEXITCODE
    try {
        $snapshotReceipt =
            ($snapshotOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $snapshotReceipt = $null
    }
    $snapshotRowsValid =
        $null -ne $snapshotReceipt -and
        @($snapshotReceipt.processes).Count -eq
            [int]$snapshotReceipt.processCount -and
        @($snapshotReceipt.processes | Where-Object {
            [string]::IsNullOrWhiteSpace([string]$_.processName) -or
            [int]$_.processId -le 0 -or
            [double]$_.cpuPercent -lt 0.0 -or
            [double]$_.cpuPercent -gt 100.0 -or
            $null -eq $_.cpuPercentMeasured -or
            $_.cpuPercentMeasured -isnot [bool] -or
            [long]$_.workingSetBytes -lt 0 -or
            [int]$_.threadCount -lt 0
        }).Count -eq 0
    Add-Check `
        -Name 'runtime.bounded-read-only-snapshot' `
        -Passed (
            $snapshotExitCode -eq 0 -and
            $null -ne $snapshotReceipt -and
            $snapshotReceipt.result -eq
                'captured-read-only-process-snapshot' -and
            $snapshotReceipt.processCount -ge 1 -and
            $snapshotReceipt.processCount -le 8 -and
            $snapshotReceipt.maximumProcessCount -eq 8 -and
            $snapshotReceipt.readOnlyProcessTelemetry -and
            -not $snapshotReceipt.processControlSupported -and
            -not $snapshotReceipt.windowControlSupported -and
            -not $snapshotReceipt.injectionSupported -and
            $snapshotRowsValid) `
        -Detail (
            "Snapshot exit $snapshotExitCode; rows=" +
            $(if ($null -eq $snapshotReceipt) {
                'unavailable'
            }
            else {
                "$($snapshotReceipt.processCount)/" +
                "$($snapshotReceipt.maximumProcessCount)"
            }))

    $invalidOutputs = @(
        @(& $DotnetPath $assemblyPath `
            snapshot --limit 2 --limit 3 2>&1)
        $duplicateLimitExitCode = $LASTEXITCODE
        @(& $DotnetPath $assemblyPath `
            snapshot --interval-ms 99 2>&1)
        $invalidIntervalExitCode = $LASTEXITCODE
    )
    Add-Check `
        -Name 'cli.invalid-options-fail-closed' `
        -Passed (
            $duplicateLimitExitCode -eq 2 -and
            $invalidIntervalExitCode -eq 2 -and
            ($invalidOutputs -join [Environment]::NewLine).Contains('Usage:')) `
        -Detail (
            "Duplicate limit exit $duplicateLimitExitCode; " +
            "invalid interval exit $invalidIntervalExitCode.")
}
else {
    Add-Check `
        -Name 'model.scenarios' `
        -Passed $false `
        -Detail 'Skipped because the Release build failed.'
    Add-Check `
        -Name 'model.external-contract-scenarios' `
        -Passed $false `
        -Detail 'Skipped because the Release build failed.'
    Add-Check `
        -Name 'runtime.bounded-read-only-snapshot' `
        -Passed $false `
        -Detail 'Skipped because the Release build failed.'
    Add-Check `
        -Name 'cli.invalid-options-fail-closed' `
        -Passed $false `
        -Detail 'Skipped because the Release build failed.'
}

$passedCount = @($checks | Where-Object passed).Count
$receipt = [ordered]@{
    schemaVersion = 1
    result = if ($failures.Count -eq 0) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = $passedCount
    readOnlyProcessTelemetry = $true
    processControlSupported = $false
    windowControlSupported = $false
    injectionSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
}

$receipt | ConvertTo-Json -Depth 8
if ($failures.Count -ne 0) {
    exit 1
}
