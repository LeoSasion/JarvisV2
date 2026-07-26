[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SessionPlanPath,

    [switch]$ConfirmOpen,

    [switch]$RecoveryConsole
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$moduleId = 'jarvis-taskbar-icon-size'
$planSchema =
    Join-Path $root 'config\m2-validation-session-plan.schema.json'
$leaseSchema =
    Join-Path $root 'config\m2-recovery-terminal-lease.schema.json'
$readinessScript = Join-Path $root 'scripts\Test-M2LiveReadiness.ps1'
$allowedPlanRoot =
    Join-Path $root 'artifacts\m2-validation-session-plans\runs'
$stateDirectory = Join-Path $env:LOCALAPPDATA 'JARVIS2'
$recoveryDirectory = Join-Path $stateDirectory 'Recovery'
$leasePath = Join-Path $recoveryDirectory 'm2-recovery-terminal.json'
$heartbeatIntervalMilliseconds = 1000
$heartbeatFreshnessSeconds = 4
$recoveryCommand = (
    'dotnet run --project .\src\Jarvis.Supervisor ' +
    '--configuration Release --no-build -- arm-kill-switch'
)

function Convert-JsonUtcDateTime {
    param(
        [Parameter(Mandatory)] [object]$Value,
        [Parameter(Mandatory)] [string]$FieldName
    )

    if ($Value -is [DateTime]) {
        $dateTime = [DateTime]$Value
        if ($dateTime.Kind -eq [DateTimeKind]::Unspecified) {
            throw "$FieldName must include an explicit UTC offset."
        }
        return $dateTime.ToUniversalTime()
    }
    if ($Value -is [DateTimeOffset]) {
        return ([DateTimeOffset]$Value).UtcDateTime
    }

    $text = [string]$Value
    if ($text -notmatch '(?i)(?:Z|[+-]\d{2}:\d{2})$') {
        throw "$FieldName must include an explicit UTC offset."
    }
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            $text,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$parsed)) {
        throw "$FieldName is not a valid UTC timestamp."
    }
    return $parsed.UtcDateTime
}

function Resolve-RestrictedPlanPath {
    param([Parameter(Mandatory)] [string]$Path)

    $candidate = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $root $Path))
    }
    $allowed = [IO.Path]::GetFullPath($allowedPlanRoot).TrimEnd('\')
    if (-not $candidate.StartsWith(
            $allowed + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "SessionPlanPath must stay under $allowed."
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw 'The session plan does not exist.'
    }
    return $candidate
}

function Test-CurrentSourceIdentity {
    param([Parameter(Mandatory)] [object]$Identity)

    $candidate = [IO.Path]::GetFullPath(
        (Join-Path $root ([string]$Identity.relativePath)))
    $rootPrefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    if (-not $candidate.StartsWith(
            $rootPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        return $false
    }
    $item = Get-Item -LiteralPath $candidate -Force
    return (
        [int64]$item.Length -eq [int64]$Identity.size -and
        (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -eq
            [string]$Identity.sha256
    )
}

function Write-RecoveryLease {
    param(
        [Parameter(Mandatory)] [string]$State,
        [Parameter(Mandatory)] [long]$Sequence,
        [Parameter(Mandatory)] [DateTime]$OpenedAtUtc,
        [Parameter(Mandatory)] [DateTime]$ProcessStartTimeUtc,
        [Parameter(Mandatory)] [DateTime]$PlanExpiresAtUtc,
        [Parameter(Mandatory)] [string]$PlanHash,
        [Parameter(Mandatory)] [object]$SessionPlan,
        [Parameter(Mandatory)] [string]$ResolvedPlanPath
    )

    $lease = [ordered]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-m2-recovery-terminal-lease'
        state = $State
        moduleId = $moduleId
        sessionPlanRunId = [string]$SessionPlan.runId
        planPath = $ResolvedPlanPath
        planSha256 = $PlanHash
        processId = [int]$PID
        processStartTimeUtc = $ProcessStartTimeUtc.ToString('o')
        openedAtUtc = $OpenedAtUtc.ToString('o')
        heartbeatAtUtc = [DateTime]::UtcNow.ToString('o')
        heartbeatSequence = $Sequence
        planExpiresAtUtc = $PlanExpiresAtUtc.ToString('o')
        recoveryCommand = $recoveryCommand
        activationPermitted = $false
        mutationPerformed = $false
    }
    $json = $lease | ConvertTo-Json -Depth 8
    if (-not ($json | Test-Json -SchemaFile $leaseSchema -ErrorAction Stop)) {
        throw 'The recovery-terminal lease failed schema validation.'
    }

    $null = [IO.Directory]::CreateDirectory($recoveryDirectory)
    $temporaryPath = Join-Path $recoveryDirectory (
        '.m2-recovery-terminal.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    )
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            $json + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $leasePath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Read-ReadyLease {
    param(
        [Parameter(Mandatory)] [int]$ExpectedProcessId,
        [Parameter(Mandatory)] [DateTime]$ExpectedProcessStartTimeUtc,
        [Parameter(Mandatory)] [string]$ExpectedPlanHash,
        [Parameter(Mandatory)] [string]$ExpectedPlanRunId
    )

    if (-not (Test-Path -LiteralPath $leasePath -PathType Leaf)) {
        return $null
    }
    try {
        $leaseJson = Get-Content -LiteralPath $leasePath -Raw
        if (-not ($leaseJson |
                Test-Json -SchemaFile $leaseSchema -ErrorAction Stop)) {
            return $null
        }
        $lease = $leaseJson | ConvertFrom-Json -Depth 20
        $processStart = Convert-JsonUtcDateTime `
            -Value $lease.processStartTimeUtc `
            -FieldName 'lease.processStartTimeUtc'
        $heartbeat = Convert-JsonUtcDateTime `
            -Value $lease.heartbeatAtUtc `
            -FieldName 'lease.heartbeatAtUtc'
        if ($lease.state -ne 'ready' -or
            $lease.moduleId -ne $moduleId -or
            [int]$lease.processId -ne $ExpectedProcessId -or
            [Math]::Abs(
                ($processStart - $ExpectedProcessStartTimeUtc).TotalSeconds) -gt
                    2 -or
            [string]$lease.planSha256 -ne $ExpectedPlanHash -or
            [string]$lease.sessionPlanRunId -ne $ExpectedPlanRunId -or
            ([DateTime]::UtcNow - $heartbeat).TotalSeconds -gt
                $heartbeatFreshnessSeconds) {
            return $null
        }
        return $lease
    }
    catch {
        return $null
    }
}

$resolvedPlanPath = Resolve-RestrictedPlanPath $SessionPlanPath
$planJson = Get-Content -LiteralPath $resolvedPlanPath -Raw
if (-not ($planJson | Test-Json -SchemaFile $planSchema -ErrorAction Stop)) {
    throw 'The session plan failed schema validation.'
}
$plan = $planJson | ConvertFrom-Json -Depth 100

if ($plan.result -ne 'passed' -or
    $plan.state -ne 'awaiting-exact-approval' -or
    $plan.moduleId -ne $moduleId -or
    $plan.activationPermitted -or
    $plan.liveExplorer -ne 'not-run' -or
    $plan.mutationPerformed -or
    $plan.approval.exactCommandApproved -or
    $plan.approval.canExecuteNow -or
    $plan.recoveryTerminal.command -ne $recoveryCommand) {
    throw 'The session plan is not a locked pre-activation plan.'
}

$expiresAt = Convert-JsonUtcDateTime `
    -Value $plan.expiresAtUtc `
    -FieldName 'plan.expiresAtUtc'
if ($expiresAt -le [DateTime]::UtcNow) {
    throw 'The session plan has expired.'
}

foreach ($identity in $plan.sourceIdentity.PSObject.Properties.Value) {
    if (-not (Test-CurrentSourceIdentity $identity)) {
        throw "Session plan source identity drifted: $($identity.relativePath)"
    }
}

$readinessJson =
    (& pwsh -NoLogo -NoProfile -File $readinessScript 2>&1 |
        ForEach-Object { [string]$_ }) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) {
    throw 'Fresh M2 readiness inspection failed.'
}
$readiness = $readinessJson | ConvertFrom-Json -Depth 100
if ($readiness.result -ne 'passed' -or
    -not $readiness.readyForExactApproval -or
    $readiness.killSwitch.state -ne 'armed' -or
    $readiness.activeModulePermit.state -ne 'absent' -or
    $readiness.windhawkService.state -ne 'Stopped' -or
    $readiness.windhawkService.startMode -ne 'Manual' -or
    $readiness.windhawkService.processId -ne 0 -or
    $readiness.runtime.moduleMappingCount -ne 0 -or
    -not $readiness.runtime.explorerModuleInspectionSucceeded -or
    $readiness.canonicalBuild.runId -ne $plan.readiness.canonicalRunId -or
    $readiness.canonicalBuild.m2SourceSha256 -ne
        $plan.readiness.m2SourceSha256 -or
    $readiness.activationPermitted -or
    $readiness.liveExplorer -ne 'not-run' -or
    $readiness.mutationPerformed) {
    throw 'Fresh host or canonical readiness no longer matches the plan.'
}

$planHash =
    (Get-FileHash -LiteralPath $resolvedPlanPath -Algorithm SHA256).Hash

if ($RecoveryConsole) {
    $host.UI.RawUI.WindowTitle =
        'JarvisV2 M2 recovery terminal - lease active; no command executed'
    Write-Host ''
    Write-Host 'JarvisV2 M2 recovery terminal' -ForegroundColor Cyan
    Write-Host "Session plan: $($plan.runId)"
    Write-Host 'The host was rechecked in the locked state.'
    Write-Host 'No recovery or activation command has been executed.'
    Write-Host ''
    Write-Host 'Emergency arm command:' -ForegroundColor Yellow
    Write-Host $recoveryCommand
    Write-Host ''
    Write-Host (
        'This window now publishes a one-second safety heartbeat. Closing it ' +
        'invalidates activation within four seconds.'
    )

    $openedAt = [DateTime]::UtcNow
    $processStart = (Get-Process -Id $PID -ErrorAction Stop).
        StartTime.ToUniversalTime()
    [long]$sequence = 0
    try {
        while ([DateTime]::UtcNow -lt $expiresAt) {
            $sequence++
            if (-not (Test-Path -LiteralPath $resolvedPlanPath -PathType Leaf) -or
                (Get-FileHash -LiteralPath $resolvedPlanPath -Algorithm SHA256).
                    Hash -ne $planHash) {
                Write-RecoveryLease `
                    -State 'closing' `
                    -Sequence $sequence `
                    -OpenedAtUtc $openedAt `
                    -ProcessStartTimeUtc $processStart `
                    -PlanExpiresAtUtc $expiresAt `
                    -PlanHash $planHash `
                    -SessionPlan $plan `
                    -ResolvedPlanPath $resolvedPlanPath
                throw 'The session plan changed while the recovery terminal was open.'
            }
            Write-RecoveryLease `
                -State 'ready' `
                -Sequence $sequence `
                -OpenedAtUtc $openedAt `
                -ProcessStartTimeUtc $processStart `
                -PlanExpiresAtUtc $expiresAt `
                -PlanHash $planHash `
                -SessionPlan $plan `
                -ResolvedPlanPath $resolvedPlanPath
            Start-Sleep -Milliseconds $heartbeatIntervalMilliseconds
        }

        $sequence++
        Write-RecoveryLease `
            -State 'expired' `
            -Sequence $sequence `
            -OpenedAtUtc $openedAt `
            -ProcessStartTimeUtc $processStart `
            -PlanExpiresAtUtc $expiresAt `
            -PlanHash $planHash `
            -SessionPlan $plan `
            -ResolvedPlanPath $resolvedPlanPath
        Write-Host ''
        Write-Host (
            'Session plan expired. Activation is blocked; close this window ' +
            'and prepare a fresh locked plan.'
        ) -ForegroundColor Yellow
    }
    finally {
        if ([DateTime]::UtcNow -lt $expiresAt) {
            try {
                $sequence++
                Write-RecoveryLease `
                    -State 'closing' `
                    -Sequence $sequence `
                    -OpenedAtUtc $openedAt `
                    -ProcessStartTimeUtc $processStart `
                    -PlanExpiresAtUtc $expiresAt `
                    -PlanHash $planHash `
                    -SessionPlan $plan `
                    -ResolvedPlanPath $resolvedPlanPath
            }
            catch {
                Write-Warning (
                    'Could not publish the closing lease state. The last ' +
                    'heartbeat will still become stale within four seconds.'
                )
            }
        }
    }
    return
}

$dryRun = -not $ConfirmOpen
if ($dryRun) {
    [ordered]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-m2-recovery-terminal-dry-run'
        checkedAtUtc = [DateTime]::UtcNow.ToString('o')
        result = 'passed'
        sessionPlanRunId = [string]$plan.runId
        planSha256 = $planHash
        recoveryCommand = $recoveryCommand
        leasePath = $leasePath
        heartbeatFreshnessSeconds = $heartbeatFreshnessSeconds
        launchPerformed = $false
        terminalAvailable = $false
        activationPermitted = $false
        liveExplorer = 'not-run'
        mutationPerformed = $false
        canExecuteNow = $false
    } | ConvertTo-Json -Depth 8
    return
}

$pwsh = Get-Command pwsh -CommandType Application -ErrorAction Stop |
    Select-Object -First 1
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $pwsh.Source
$startInfo.WorkingDirectory = $root
$startInfo.UseShellExecute = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Normal
foreach ($argument in @(
    '-NoLogo',
    '-NoProfile',
    '-NoExit',
    '-File',
    $PSCommandPath,
    '-SessionPlanPath',
    $resolvedPlanPath,
    '-RecoveryConsole'
)) {
    $null = $startInfo.ArgumentList.Add($argument)
}

$process = [Diagnostics.Process]::Start($startInfo)
if ($null -eq $process) {
    throw 'The recovery terminal process was not created.'
}
$processStart = $process.StartTime.ToUniversalTime()
$readyLease = $null
$deadline = [DateTime]::UtcNow.AddSeconds(8)
do {
    if ($process.HasExited) {
        throw 'The recovery terminal exited before its heartbeat was confirmed.'
    }
    $readyLease = Read-ReadyLease `
        -ExpectedProcessId $process.Id `
        -ExpectedProcessStartTimeUtc $processStart `
        -ExpectedPlanHash $planHash `
        -ExpectedPlanRunId ([string]$plan.runId)
    if ($null -ne $readyLease) {
        break
    }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)

if ($null -eq $readyLease) {
    throw 'The recovery terminal did not publish a fresh lease within 8 seconds.'
}

[ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-m2-recovery-terminal-open'
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    result = 'passed'
    sessionPlanRunId = [string]$plan.runId
    planSha256 = $planHash
    recoveryCommand = $recoveryCommand
    leasePath = $leasePath
    leaseSha256 =
        (Get-FileHash -LiteralPath $leasePath -Algorithm SHA256).Hash
    heartbeatAtUtc = [string]$readyLease.heartbeatAtUtc
    heartbeatSequence = [long]$readyLease.heartbeatSequence
    heartbeatFreshnessSeconds = $heartbeatFreshnessSeconds
    launchPerformed = $true
    terminalAvailable = $true
    processId = [int]$process.Id
    processStartTimeUtc = $processStart.ToString('o')
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    canExecuteNow = $false
} | ConvertTo-Json -Depth 8
