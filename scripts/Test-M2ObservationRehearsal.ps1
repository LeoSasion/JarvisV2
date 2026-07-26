[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SessionPlanPath,

    [ValidateSet(
        'none',
        'kill-switch-missing',
        'permit-present',
        'windhawk-running',
        'explorer-changed',
        'module-mapped',
        'elevated-cpu'
    )]
    [string]$FaultInjection = 'none',

    [ValidateRange(1, 15)]
    [int]$DurationSeconds = 2,

    [ValidateRange(0.1, 100)]
    [double]$CpuStopThresholdPercent = 5.0,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$planSchema =
    Join-Path $root 'config\m2-validation-session-plan.schema.json'
$receiptSchema =
    Join-Path $root 'config\m2-observation-rehearsal-receipt.schema.json'
$readinessScript = Join-Path $root 'scripts\Test-M2LiveReadiness.ps1'
$baselineScript = Join-Path $root 'scripts\Measure-M2HostBaseline.ps1'
$allowedPlanRoot =
    Join-Path $root 'artifacts\m2-validation-session-plans\runs'
$allowedOutputRoot =
    Join-Path $root 'artifacts\m2-observation-rehearsal\runs'

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

function Resolve-PathUnderRoot {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedRoot,
        [Parameter(Mandatory)] [string]$Label,
        [switch]$MustExist,
        [switch]$MustNotExist
    )

    $candidate = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $root $Path))
    }
    $allowed = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\')
    if (-not $candidate.StartsWith(
            $allowed + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must stay under $allowed."
    }
    if ($MustExist -and
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "$Label does not exist."
    }
    if ($MustNotExist -and (Test-Path -LiteralPath $candidate)) {
        throw "Refusing to overwrite an existing $Label."
    }
    return $candidate
}

function Invoke-JsonScript {
    param(
        [Parameter(Mandatory)] [string]$ScriptPath,
        [string[]]$Arguments = @()
    )

    $text =
        (& pwsh -NoLogo -NoProfile -File $ScriptPath @Arguments 2>&1 |
            ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) {
        throw "Read-only helper failed: $ScriptPath"
    }
    return $text | ConvertFrom-Json -Depth 100
}

function Test-CurrentSourceIdentity {
    param([Parameter(Mandatory)] [object]$Identity)

    $candidate = [IO.Path]::GetFullPath(
        (Join-Path $root ([string]$Identity.relativePath)))
    $rootPrefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    if (-not $candidate.StartsWith(
            $rootPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        return $false
    }
    $item = Get-Item -LiteralPath $candidate -Force
    return (
        [int64]$item.Length -eq [int64]$Identity.size -and
        (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -eq
            [string]$Identity.sha256
    )
}

$resolvedPlanPath = Resolve-PathUnderRoot `
    -Path $SessionPlanPath `
    -AllowedRoot $allowedPlanRoot `
    -Label 'SessionPlanPath' `
    -MustExist
$resolvedOutputPath = $null
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = Resolve-PathUnderRoot `
        -Path $OutputPath `
        -AllowedRoot $allowedOutputRoot `
        -Label 'observation receipt' `
        -MustNotExist
}

$planJson = Get-Content -LiteralPath $resolvedPlanPath -Raw
if (-not ($planJson | Test-Json -SchemaFile $planSchema -ErrorAction Stop)) {
    throw 'The session plan failed schema validation.'
}
$plan = $planJson | ConvertFrom-Json -Depth 100
if ($plan.result -ne 'passed' -or
    $plan.state -ne 'awaiting-exact-approval' -or
    $plan.activationPermitted -or
    $plan.liveExplorer -ne 'not-run' -or
    $plan.mutationPerformed -or
    $plan.approval.exactCommandApproved -or
    $plan.approval.canExecuteNow) {
    throw 'Observation rehearsal requires a locked pre-activation plan.'
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

$before = Invoke-JsonScript -ScriptPath $readinessScript
if ($before.result -ne 'passed' -or
    -not $before.readyForExactApproval -or
    $before.canonicalBuild.runId -ne $plan.readiness.canonicalRunId -or
    $before.canonicalBuild.m2SourceSha256 -ne
        $plan.readiness.m2SourceSha256) {
    throw 'Fresh readiness no longer matches the session plan.'
}

$baseline = Invoke-JsonScript `
    -ScriptPath $baselineScript `
    -Arguments @(
        '-DurationSeconds',
        [string]$DurationSeconds,
        '-IntervalMilliseconds',
        '250'
    )
$after = Invoke-JsonScript -ScriptPath $readinessScript

$afterExplorerIds = @($after.compatibility.explorerProcessIds)
if ($afterExplorerIds.Count -ne 1) {
    throw 'Expected one verified Explorer process after observation.'
}
$actualHost = [ordered]@{
    killSwitchState = [string]$after.killSwitch.state
    permitState = [string]$after.activeModulePermit.state
    windhawkState = [string]$after.windhawkService.state
    windhawkStartMode = [string]$after.windhawkService.startMode
    explorerProcessId = [int]$afterExplorerIds[0]
    moduleMappingCount = [int]$after.runtime.moduleMappingCount
    explorerModuleInspectionSucceeded =
        [bool]$after.runtime.explorerModuleInspectionSucceeded
}
$evaluationState = [ordered]@{
    killSwitchState = [string]$actualHost.killSwitchState
    permitState = [string]$actualHost.permitState
    windhawkState = [string]$actualHost.windhawkState
    windhawkStartMode = [string]$actualHost.windhawkStartMode
    explorerProcessId = [int]$actualHost.explorerProcessId
    moduleMappingCount = [int]$actualHost.moduleMappingCount
    explorerModuleInspectionSucceeded =
        [bool]$actualHost.explorerModuleInspectionSucceeded
}
$evaluatedPeakCpuPercent = [double]$baseline.summary.peakCpuPercent

switch ($FaultInjection) {
    'kill-switch-missing' {
        $evaluationState.killSwitchState = 'missing'
    }
    'permit-present' {
        $evaluationState.permitState = 'present'
    }
    'windhawk-running' {
        $evaluationState.windhawkState = 'Running'
    }
    'explorer-changed' {
        $evaluationState.explorerProcessId =
            [int]$plan.readiness.explorerProcessId + 1
    }
    'module-mapped' {
        $evaluationState.moduleMappingCount = 1
    }
    'elevated-cpu' {
        $evaluatedPeakCpuPercent = $CpuStopThresholdPercent + 1.0
    }
}

$stopReasons = [System.Collections.Generic.List[string]]::new()
if ($evaluationState.killSwitchState -ne 'armed') {
    $stopReasons.Add('kill-switch-not-armed')
}
if ($evaluationState.permitState -ne 'absent') {
    $stopReasons.Add('permit-not-absent')
}
if ($evaluationState.windhawkState -ne 'Stopped' -or
    $evaluationState.windhawkStartMode -ne 'Manual') {
    $stopReasons.Add('windhawk-service-drift')
}
if ($evaluationState.explorerProcessId -ne
    [int]$plan.readiness.explorerProcessId -or
    [int]$baseline.explorerProcessId -ne
    [int]$plan.readiness.explorerProcessId) {
    $stopReasons.Add('explorer-process-changed')
}
if ($evaluationState.moduleMappingCount -ne 0) {
    $stopReasons.Add('unexpected-module-mapping')
}
if (-not $evaluationState.explorerModuleInspectionSucceeded) {
    $stopReasons.Add('explorer-module-inspection-incomplete')
}
if ($evaluatedPeakCpuPercent -gt $CpuStopThresholdPercent) {
    $stopReasons.Add('explorer-cpu-threshold-exceeded')
}
if ($after.result -ne 'passed' -or
    $after.activationPermitted -or
    $after.liveExplorer -ne 'not-run' -or
    $after.mutationPerformed) {
    $stopReasons.Add('readiness-boundary-drift')
}

$expectedReason = switch ($FaultInjection) {
    'none' { $null }
    'kill-switch-missing' { 'kill-switch-not-armed' }
    'permit-present' { 'permit-not-absent' }
    'windhawk-running' { 'windhawk-service-drift' }
    'explorer-changed' { 'explorer-process-changed' }
    'module-mapped' { 'unexpected-module-mapping' }
    'elevated-cpu' { 'explorer-cpu-threshold-exceeded' }
}
$errors = [System.Collections.Generic.List[string]]::new()
if ($FaultInjection -eq 'none' -and $stopReasons.Count -ne 0) {
    $errors.Add('unexpected-stop-in-normal-rehearsal')
}
if ($FaultInjection -ne 'none' -and
    -not $stopReasons.Contains($expectedReason)) {
    $errors.Add('injected-fault-did-not-trigger-expected-stop')
}

$stopRequired = $stopReasons.Count -ne 0
$result = if ($errors.Count -ne 0) {
    'failed'
}
elseif ($stopRequired) {
    'stop-required'
}
else {
    'passed'
}
$observedAt = [DateTime]::UtcNow
$runId = (
    $observedAt.ToString('yyyyMMddTHHmmssfffZ') +
    '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
)
$relativePlanPath =
    [IO.Path]::GetRelativePath($root, $resolvedPlanPath).Replace('\', '/')
$receipt = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-m2-observation-rehearsal'
    runId = $runId
    observedAtUtc = $observedAt.ToString('o')
    result = $result
    mode = 'locked-rehearsal'
    faultInjection = $FaultInjection
    faultInjected = $FaultInjection -ne 'none'
    stopRequired = $stopRequired
    stopReasons = @($stopReasons)
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    sessionPlan = [ordered]@{
        relativePath = $relativePlanPath
        runId = [string]$plan.runId
        sha256 =
            (Get-FileHash -LiteralPath $resolvedPlanPath -Algorithm SHA256).Hash
    }
    actualHost = $actualHost
    evaluationState = $evaluationState
    baseline = [ordered]@{
        durationSeconds = [double]$baseline.durationSeconds
        sampleCount = [int]$baseline.sampleCount
        averageCpuPercent =
            [double]$baseline.summary.averageCpuPercent
        peakCpuPercent = [double]$baseline.summary.peakCpuPercent
        peakPrivateMemoryBytes =
            [int64]$baseline.summary.peakPrivateMemoryBytes
        peakHandleCount = [int]$baseline.summary.peakHandleCount
        peakThreadCount = [int]$baseline.summary.peakThreadCount
    }
    errors = @($errors)
}
$json = $receipt | ConvertTo-Json -Depth 20
if (-not ($json | Test-Json -SchemaFile $receiptSchema -ErrorAction Stop)) {
    throw 'Generated observation receipt failed schema validation.'
}

if ($null -ne $resolvedOutputPath) {
    $outputDirectory = Split-Path -Parent $resolvedOutputPath
    $null = [IO.Directory]::CreateDirectory($outputDirectory)
    $temporaryPath =
        Join-Path $outputDirectory ('.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            $json + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporaryPath -Destination $resolvedOutputPath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

Write-Output $json
if ($result -eq 'failed') {
    exit 1
}
