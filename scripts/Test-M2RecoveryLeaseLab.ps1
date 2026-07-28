[CmdletBinding()]
param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$moduleId = 'jarvis-taskbar-icon-size'
$schemaPath =
    Join-Path $root 'config\m2-recovery-lease-lab-receipt.schema.json'
$planRoot =
    Join-Path $root 'artifacts\m2-validation-session-plans\runs'
$labRoot = Join-Path $root 'artifacts\m2-recovery-lease-lab\runs'
$supervisorProject =
    Join-Path $root 'src\Jarvis.Supervisor\Jarvis.Supervisor.csproj'
$recoveryCommand = (
    'dotnet run --project .\src\Jarvis.Supervisor ' +
    '--configuration Release --no-build -- arm-kill-switch'
)
$sourcePaths = [ordered]@{
    planner = 'scripts/New-M2ValidationSessionPlan.ps1'
    planSchema = 'config/m2-validation-session-plan.schema.json'
    readinessScript = 'scripts/Test-M2LiveReadiness.ps1'
    readinessSchema = 'config/m2-live-readiness-receipt.schema.json'
    recoveryTerminalScript = 'scripts/Open-M2RecoveryTerminal.ps1'
    recoveryLeaseSchema = 'config/m2-recovery-terminal-lease.schema.json'
    observerScript = 'scripts/Test-M2ObservationRehearsal.ps1'
    observerSchema = 'config/m2-observation-rehearsal-receipt.schema.json'
    controlledLiveController =
        'scripts/Invoke-M2ControlledLiveValidation.ps1'
    controlledLiveReceiptSchema =
        'config/m2-controlled-live-controller-receipt.schema.json'
    nativeBuildReceipt = 'docs/receipts/native-build-2026-07-22.json'
    m2Source = 'mods/jarvis-taskbar-icon-size.wh.cpp'
    supervisorAssembly = (
        'src/Jarvis.Supervisor/bin/Release/net8.0-windows/' +
        'jarvis-supervisor.dll'
    )
}

function Write-JsonAtomic {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [object]$Value
    )

    $directory = Split-Path -Parent $Path
    $null = [IO.Directory]::CreateDirectory($directory)
    $temporaryPath =
        Join-Path $directory ('.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            ($Value | ConvertTo-Json -Depth 30) + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $Path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Get-FileIdentity {
    param([Parameter(Mandatory)] [string]$RelativePath)

    $path = Join-Path $root $RelativePath
    $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    return [ordered]@{
        relativePath = $RelativePath.Replace('\', '/')
        size = [int64]$item.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
}

function Invoke-LeaseInspection {
    param(
        [Parameter(Mandatory)] [string]$ScenarioId,
        [Parameter(Mandatory)] [object]$Lease,
        [Parameter(Mandatory)] [int]$ExpectedExitCode,
        [Parameter(Mandatory)] [bool]$ExpectedReady,
        [AllowNull()] [AllowEmptyString()] [string]$ExpectedError
    )

    Write-JsonAtomic -Path $fixtureLeasePath -Value $Lease
    $output =
        (& dotnet run --project $supervisorProject `
            --configuration Release --no-build -- `
            inspect-recovery-terminal --module $moduleId `
            --lease-path $fixtureLeasePath 2>&1 |
            ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    $exitCode = $LASTEXITCODE
    $probe = $output | ConvertFrom-Json -Depth 20
    $actualError = if ($null -eq $probe.error) {
        $null
    }
    else {
        [string]$probe.error
    }
    $normalizedExpectedError = if ([string]::IsNullOrEmpty($ExpectedError)) {
        $null
    }
    else {
        $ExpectedError
    }
    $errorMatched = if ($null -eq $normalizedExpectedError) {
        $null -eq $actualError
    }
    else {
        $null -ne $actualError -and
        $actualError.Contains(
            $normalizedExpectedError,
            [StringComparison]::Ordinal)
    }
    $passed =
        $exitCode -eq $ExpectedExitCode -and
        [bool]$probe.ready -eq $ExpectedReady -and
        $errorMatched
    return [ordered]@{
        id = $ScenarioId
        expectedExitCode = $ExpectedExitCode
        actualExitCode = $exitCode
        expectedReady = $ExpectedReady
        actualReady = [bool]$probe.ready
        expectedError = $normalizedExpectedError
        actualError = $actualError
        passed = $passed
    }
}

function Test-RecoveryPathIsolation {
    $watchRoot = Join-Path $fixtureDirectory 'watch-root'
    $recoveryRoot = Join-Path $watchRoot 'Recovery'
    $heartbeatPath = Join-Path $recoveryRoot 'm2-recovery-terminal.json'
    $null = [IO.Directory]::CreateDirectory($recoveryRoot)

    $watcher = [IO.FileSystemWatcher]::new($watchRoot)
    $watcher.IncludeSubdirectories = $false
    $watcher.NotifyFilter =
        [IO.NotifyFilters]::FileName -bor [IO.NotifyFilters]::DirectoryName
    $sourceIds = @(
        "jarvis2-path-created-$runId",
        "jarvis2-path-deleted-$runId",
        "jarvis2-path-renamed-$runId"
    )
    $subscriptions = @()
    try {
        $subscriptions += Register-ObjectEvent `
            -InputObject $watcher `
            -EventName Created `
            -SourceIdentifier $sourceIds[0]
        $subscriptions += Register-ObjectEvent `
            -InputObject $watcher `
            -EventName Deleted `
            -SourceIdentifier $sourceIds[1]
        $subscriptions += Register-ObjectEvent `
            -InputObject $watcher `
            -EventName Renamed `
            -SourceIdentifier $sourceIds[2]
        $watcher.EnableRaisingEvents = $true

        foreach ($sequence in 1..3) {
            Write-JsonAtomic -Path $heartbeatPath -Value ([ordered]@{
                sequence = $sequence
                heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
            })
            Start-Sleep -Milliseconds 100
        }
        Start-Sleep -Milliseconds 300
        $rootEvents = @(
            foreach ($sourceId in $sourceIds) {
                Get-Event -SourceIdentifier $sourceId -ErrorAction SilentlyContinue
            }
        )
        $passed = $rootEvents.Count -eq 0
        return [ordered]@{
            id = 'recovery-child-path-isolation'
            expectedExitCode = 0
            actualExitCode = if ($passed) { 0 } else { 11 }
            expectedReady = $true
            actualReady = $passed
            expectedError = $null
            actualError = if ($passed) {
                $null
            }
            else {
                "state-root-file-name-events:$($rootEvents.Count)"
            }
            passed = $passed
        }
    }
    finally {
        $watcher.EnableRaisingEvents = $false
        foreach ($subscription in $subscriptions) {
            Unregister-Event `
                -SubscriptionId $subscription.Id `
                -ErrorAction SilentlyContinue
        }
        foreach ($sourceId in $sourceIds) {
            Remove-Event `
                -SourceIdentifier $sourceId `
                -ErrorAction SilentlyContinue
        }
        $watcher.Dispose()
    }
}

$startedAt = [DateTime]::UtcNow
$runId = (
    $startedAt.ToString('yyyyMMddTHHmmssfffZ') +
    '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
)
$resolvedOutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $labRoot ($runId + '.json')
}
elseif ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $root $OutputPath))
}
$allowedLabRoot = [IO.Path]::GetFullPath($labRoot).TrimEnd('\') + '\'
if (-not $resolvedOutputPath.StartsWith(
        $allowedLabRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must stay under $labRoot."
}
if (Test-Path -LiteralPath $resolvedOutputPath) {
    throw 'Refusing to overwrite an existing recovery lease lab receipt.'
}

$fixtureDirectory = Join-Path $labRoot ('fixtures-' + $runId)
$fixtureLeasePath = Join-Path $fixtureDirectory 'lease.json'
$fixturePlanPath = Join-Path $planRoot ('lease-lab-' + $runId + '.json')
$process = Get-Process -Id $PID -ErrorAction Stop
$processStart = $process.StartTime.ToUniversalTime()
$openedAt = [DateTime]::UtcNow
$expiresAt = $openedAt.AddMinutes(5)
$sourceIdentity = [ordered]@{}
foreach ($entry in $sourcePaths.GetEnumerator()) {
    $sourceIdentity[$entry.Key] = Get-FileIdentity $entry.Value
}

$plan = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-m2-validation-session-plan'
    runId = $runId
    createdAtUtc = $openedAt.ToString('o')
    expiresAtUtc = $expiresAt.ToString('o')
    result = 'passed'
    state = 'awaiting-exact-approval'
    moduleId = $moduleId
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    sourceIdentity = $sourceIdentity
    readiness = [ordered]@{}
    recoveryTerminal = [ordered]@{
        command = $recoveryCommand
        openCommand = 'offline-lease-lab'
        launchPerformed = $false
        terminalAvailable = $false
    }
    approval = [ordered]@{
        exactCommand = (
            'dotnet run --project .\src\Jarvis.Supervisor ' +
            '--configuration Release --no-build -- clear-kill-switch ' +
            '--module jarvis-taskbar-icon-size --confirm'
        )
        exactCommandApproved = $false
        canExecuteNow = $false
    }
    errors = @()
}

$scenarios = [System.Collections.Generic.List[object]]::new()
$errors = [System.Collections.Generic.List[string]]::new()
$fixturesRemoved = $false
try {
    Write-JsonAtomic -Path $fixturePlanPath -Value $plan
    $planHash =
        (Get-FileHash -LiteralPath $fixturePlanPath -Algorithm SHA256).Hash
    $baseLease = [ordered]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-m2-recovery-terminal-lease'
        state = 'ready'
        moduleId = $moduleId
        sessionPlanRunId = $runId
        planPath = $fixturePlanPath
        planSha256 = $planHash
        processId = [int]$PID
        processStartTimeUtc = $processStart.ToString('o')
        openedAtUtc = $openedAt.ToString('o')
        heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
        heartbeatSequence = 1
        planExpiresAtUtc = $expiresAt.ToString('o')
        recoveryCommand = $recoveryCommand
        activationPermitted = $false
        mutationPerformed = $false
    }

    $scenarios.Add((Invoke-LeaseInspection `
        -ScenarioId 'fresh-valid' `
        -Lease $baseLease `
        -ExpectedExitCode 0 `
        -ExpectedReady $true `
        -ExpectedError $null))

    $fixture =
        $baseLease | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
    $fixture.openedAtUtc = [DateTime]::UtcNow.AddSeconds(-20).ToString('o')
    $fixture.heartbeatAtUtc = [DateTime]::UtcNow.AddSeconds(-10).ToString('o')
    $scenarios.Add((Invoke-LeaseInspection `
        -ScenarioId 'stale-heartbeat' `
        -Lease $fixture `
        -ExpectedExitCode 11 `
        -ExpectedReady $false `
        -ExpectedError 'lease-heartbeat-stale'))

    $fixture =
        $baseLease | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
    $fixture.state = 'closing'
    $fixture.heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
    $scenarios.Add((Invoke-LeaseInspection `
        -ScenarioId 'closing-state' `
        -Lease $fixture `
        -ExpectedExitCode 11 `
        -ExpectedReady $false `
        -ExpectedError 'lease-not-ready'))

    $fixture =
        $baseLease | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
    $fixture.planSha256 = ('0' * 64)
    $fixture.heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
    $scenarios.Add((Invoke-LeaseInspection `
        -ScenarioId 'plan-hash-mismatch' `
        -Lease $fixture `
        -ExpectedExitCode 11 `
        -ExpectedReady $false `
        -ExpectedError 'lease-plan-hash-mismatch'))

    $fixture =
        $baseLease | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
    $fixture.processStartTimeUtc =
        $processStart.AddMinutes(-1).ToString('o')
    $fixture.heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
    $scenarios.Add((Invoke-LeaseInspection `
        -ScenarioId 'process-start-mismatch' `
        -Lease $fixture `
        -ExpectedExitCode 11 `
        -ExpectedReady $false `
        -ExpectedError 'lease-process-start-mismatch'))

    $driftedPlan =
        $plan | ConvertTo-Json -Depth 30 | ConvertFrom-Json -Depth 30
    $driftedSources = [ordered]@{}
    foreach ($entry in $sourceIdentity.GetEnumerator()) {
        $driftedSources[$entry.Key] = $entry.Value
    }
    $driftedSource = [ordered]@{
        relativePath = [string]$sourceIdentity.m2Source.relativePath
        size = [int64]$sourceIdentity.m2Source.size
        sha256 = ('F' * 64)
    }
    $driftedSources.m2Source = $driftedSource
    $driftedPlan.sourceIdentity = $driftedSources
    Write-JsonAtomic -Path $fixturePlanPath -Value $driftedPlan
    $fixture =
        $baseLease | ConvertTo-Json -Depth 20 | ConvertFrom-Json -Depth 20
    $fixture.planSha256 =
        (Get-FileHash -LiteralPath $fixturePlanPath -Algorithm SHA256).Hash
    $fixture.heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
    $scenarios.Add((Invoke-LeaseInspection `
        -ScenarioId 'source-identity-drift' `
        -Lease $fixture `
        -ExpectedExitCode 11 `
        -ExpectedReady $false `
        -ExpectedError 'plan-source-hash-mismatch:m2Source'))

    $scenarios.Add((Test-RecoveryPathIsolation))
}
catch {
    $errors.Add($_.Exception.Message)
}
finally {
    foreach ($path in @($fixtureLeasePath, $fixturePlanPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    if (Test-Path -LiteralPath $fixtureDirectory) {
        $resolvedFixtureDirectory =
            [IO.Path]::GetFullPath($fixtureDirectory).TrimEnd('\')
        $resolvedLabRoot = [IO.Path]::GetFullPath($labRoot).TrimEnd('\')
        if (-not $resolvedFixtureDirectory.StartsWith(
                $resolvedLabRoot + '\',
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to recursively remove a fixture outside the lab root.'
        }
        Remove-Item `
            -LiteralPath $resolvedFixtureDirectory `
            -Recurse `
            -Force
    }
    $fixturesRemoved =
        -not (Test-Path -LiteralPath $fixtureLeasePath) -and
        -not (Test-Path -LiteralPath $fixturePlanPath) -and
        -not (Test-Path -LiteralPath $fixtureDirectory)
}

foreach ($scenario in $scenarios) {
    if (-not $scenario.passed) {
        $errors.Add("scenario-failed:$($scenario.id)")
    }
}
if (-not $fixturesRemoved) {
    $errors.Add('transient-fixture-cleanup-failed')
}

$passed =
    $errors.Count -eq 0 -and
    $scenarios.Count -eq 7 -and
    @($scenarios | Where-Object { $_.passed }).Count -eq 7
$receipt = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-m2-recovery-lease-lab'
    runId = $runId
    startedAtUtc = $startedAt.ToString('o')
    completedAtUtc = [DateTime]::UtcNow.ToString('o')
    result = if ($passed) { 'passed' } else { 'failed' }
    mode = 'offline-read-only-inspection'
    scenarioCount = 7
    scenariosPassed = @($scenarios | Where-Object { $_.passed }).Count
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    stateDirectoryTouched = $false
    transientFixturesRemoved = $fixturesRemoved
    scenarios = @($scenarios)
    errors = @($errors)
}
$json = $receipt | ConvertTo-Json -Depth 20
if (-not ($json | Test-Json -SchemaFile $schemaPath -ErrorAction Stop)) {
    throw 'The recovery lease lab receipt failed schema validation.'
}
Write-JsonAtomic -Path $resolvedOutputPath -Value $receipt
Write-Output $json
if (-not $passed) {
    exit 1
}
exit 0
