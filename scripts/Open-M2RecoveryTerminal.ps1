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
$planSchema =
    Join-Path $root 'config\m2-validation-session-plan.schema.json'
$readinessScript = Join-Path $root 'scripts\Test-M2LiveReadiness.ps1'
$allowedPlanRoot =
    Join-Path $root 'artifacts\m2-validation-session-plans\runs'
$recoveryCommand = (
    'dotnet run --project .\src\Jarvis.Supervisor ' +
    '--configuration Release --no-build -- arm-kill-switch'
)

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

$resolvedPlanPath = Resolve-RestrictedPlanPath $SessionPlanPath
$planJson = Get-Content -LiteralPath $resolvedPlanPath -Raw
if (-not ($planJson | Test-Json -SchemaFile $planSchema -ErrorAction Stop)) {
    throw 'The session plan failed schema validation.'
}
$plan = $planJson | ConvertFrom-Json -Depth 100

if ($plan.result -ne 'passed' -or
    $plan.state -ne 'awaiting-exact-approval' -or
    $plan.moduleId -ne 'jarvis-taskbar-icon-size' -or
    $plan.activationPermitted -or
    $plan.liveExplorer -ne 'not-run' -or
    $plan.mutationPerformed -or
    $plan.approval.exactCommandApproved -or
    $plan.approval.canExecuteNow -or
    $plan.recoveryTerminal.command -ne $recoveryCommand) {
    throw 'The session plan is not a locked pre-activation plan.'
}

$expiresAt = [DateTime]::Parse(
    [string]$plan.expiresAtUtc,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind)
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

if ($RecoveryConsole) {
    $host.UI.RawUI.WindowTitle =
        'JarvisV2 M2 recovery terminal - no command executed'
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
        'If the future live session shows any stop condition, run the ' +
        'command above first.'
    )
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
        planSha256 =
            (Get-FileHash -LiteralPath $resolvedPlanPath -Algorithm SHA256).Hash
        recoveryCommand = $recoveryCommand
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
Start-Sleep -Milliseconds 750
if ($process.HasExited) {
    throw 'The recovery terminal exited before availability was confirmed.'
}

[ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-m2-recovery-terminal-open'
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    result = 'passed'
    sessionPlanRunId = [string]$plan.runId
    planSha256 =
        (Get-FileHash -LiteralPath $resolvedPlanPath -Algorithm SHA256).Hash
    recoveryCommand = $recoveryCommand
    launchPerformed = $true
    terminalAvailable = $true
    processId = [int]$process.Id
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    canExecuteNow = $false
} | ConvertTo-Json -Depth 8
