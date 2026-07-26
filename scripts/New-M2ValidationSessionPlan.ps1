[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath,

    [ValidateRange(5, 30)]
    [int]$ValidityMinutes = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$moduleId = 'jarvis-taskbar-icon-size'
$schemaPath =
    Join-Path $root 'config\m2-validation-session-plan.schema.json'
$readinessScript = Join-Path $root 'scripts\Test-M2LiveReadiness.ps1'
$readinessSchema =
    Join-Path $root 'config\m2-live-readiness-receipt.schema.json'
$recoveryTerminalScript =
    Join-Path $root 'scripts\Open-M2RecoveryTerminal.ps1'
$recoveryLeaseSchema =
    Join-Path $root 'config\m2-recovery-terminal-lease.schema.json'
$observerScript =
    Join-Path $root 'scripts\Test-M2ObservationRehearsal.ps1'
$observerSchema =
    Join-Path $root 'config\m2-observation-rehearsal-receipt.schema.json'
$nativeReceipt =
    Join-Path $root 'docs\receipts\native-build-2026-07-22.json'
$m2Source = Join-Path $root 'mods\jarvis-taskbar-icon-size.wh.cpp'
$supervisorAssembly = Join-Path $root (
    'src\Jarvis.Supervisor\bin\Release\net8.0-windows\' +
    'jarvis-supervisor.dll'
)
$allowedOutputRoot =
    Join-Path $root 'artifacts\m2-validation-session-plans\runs'
$exactCommand = (
    'dotnet run --project .\src\Jarvis.Supervisor ' +
    '--configuration Release --no-build -- clear-kill-switch ' +
    '--module jarvis-taskbar-icon-size --confirm'
)
$recoveryCommand = (
    'dotnet run --project .\src\Jarvis.Supervisor ' +
    '--configuration Release --no-build -- arm-kill-switch'
)

function Resolve-RestrictedOutputPath {
    param([Parameter(Mandatory)] [string]$Path)

    $candidate = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $root $Path))
    }
    $allowed = [IO.Path]::GetFullPath($allowedOutputRoot).TrimEnd('\')
    if (-not $candidate.StartsWith(
            $allowed + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputPath must stay under $allowed."
    }
    if (Test-Path -LiteralPath $candidate) {
        throw 'Refusing to overwrite an existing session plan.'
    }
    return $candidate
}

function Get-FileIdentity {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$RelativePath
    )

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    return [ordered]@{
        relativePath = $RelativePath.Replace('\', '/')
        size = [int64]$item.Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
}

$resolvedOutputPath = Resolve-RestrictedOutputPath $OutputPath
$errors = [System.Collections.Generic.List[string]]::new()

function Add-Failure {
    param([Parameter(Mandatory)] [string]$Code)
    if (-not $errors.Contains($Code)) {
        $errors.Add($Code)
    }
}

$sourceIdentity = [ordered]@{}
foreach ($source in @(
    [pscustomobject]@{
        Key = 'planner'
        Path = $PSCommandPath
        RelativePath = 'scripts/New-M2ValidationSessionPlan.ps1'
    },
    [pscustomobject]@{
        Key = 'planSchema'
        Path = $schemaPath
        RelativePath = 'config/m2-validation-session-plan.schema.json'
    },
    [pscustomobject]@{
        Key = 'readinessScript'
        Path = $readinessScript
        RelativePath = 'scripts/Test-M2LiveReadiness.ps1'
    },
    [pscustomobject]@{
        Key = 'readinessSchema'
        Path = $readinessSchema
        RelativePath = 'config/m2-live-readiness-receipt.schema.json'
    },
    [pscustomobject]@{
        Key = 'recoveryTerminalScript'
        Path = $recoveryTerminalScript
        RelativePath = 'scripts/Open-M2RecoveryTerminal.ps1'
    },
    [pscustomobject]@{
        Key = 'recoveryLeaseSchema'
        Path = $recoveryLeaseSchema
        RelativePath = 'config/m2-recovery-terminal-lease.schema.json'
    },
    [pscustomobject]@{
        Key = 'observerScript'
        Path = $observerScript
        RelativePath = 'scripts/Test-M2ObservationRehearsal.ps1'
    },
    [pscustomobject]@{
        Key = 'observerSchema'
        Path = $observerSchema
        RelativePath =
            'config/m2-observation-rehearsal-receipt.schema.json'
    },
    [pscustomobject]@{
        Key = 'nativeBuildReceipt'
        Path = $nativeReceipt
        RelativePath = 'docs/receipts/native-build-2026-07-22.json'
    },
    [pscustomobject]@{
        Key = 'm2Source'
        Path = $m2Source
        RelativePath = 'mods/jarvis-taskbar-icon-size.wh.cpp'
    },
    [pscustomobject]@{
        Key = 'supervisorAssembly'
        Path = $supervisorAssembly
        RelativePath = (
            'src/Jarvis.Supervisor/bin/Release/net8.0-windows/' +
            'jarvis-supervisor.dll'
        )
    }
)) {
    try {
        $sourceIdentity[$source.Key] =
            Get-FileIdentity $source.Path $source.RelativePath
    }
    catch {
        Add-Failure "source-identity-$($source.Key)-unavailable"
    }
}

$readiness = $null
try {
    $readinessJson =
        (& pwsh -NoLogo -NoProfile -File $readinessScript 2>&1 |
            ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) {
        Add-Failure 'readiness-command-failed'
    }
    else {
        $readiness = $readinessJson | ConvertFrom-Json -Depth 100
    }
}
catch {
    Add-Failure 'readiness-unavailable'
}

if ($null -eq $readiness) {
    Add-Failure 'readiness-missing'
}
else {
    if ($readiness.result -ne 'passed' -or
        -not $readiness.readyForExactApproval) {
        Add-Failure 'readiness-not-passed'
    }
    if ($readiness.activationPermitted -or
        $readiness.liveExplorer -ne 'not-run' -or
        $readiness.mutationPerformed) {
        Add-Failure 'readiness-boundary-invalid'
    }
    if ($readiness.requestedModule -ne $moduleId) {
        Add-Failure 'readiness-module-mismatch'
    }
    if ($readiness.killSwitch.state -ne 'armed' -or
        $readiness.activeModulePermit.state -ne 'absent') {
        Add-Failure 'host-not-locked'
    }
    if ($readiness.windhawkService.state -ne 'Stopped' -or
        $readiness.windhawkService.startMode -ne 'Manual' -or
        $readiness.windhawkService.processId -ne 0) {
        Add-Failure 'windhawk-not-stopped-manual'
    }
    if (-not $readiness.compatibility.compatible -or
        $readiness.compatibility.checksPassed -ne
            $readiness.compatibility.checkCount) {
        Add-Failure 'compatibility-not-passed'
    }
    if (@($readiness.compatibility.explorerProcessIds).Count -ne 1) {
        Add-Failure 'explorer-process-count-invalid'
    }
    if ($readiness.canonicalBuild.warningCount -ne 0 -or
        $readiness.canonicalBuild.errorCount -ne 0 -or
        $readiness.canonicalBuild.activationPermitted -or
        $readiness.canonicalBuild.liveExplorer -ne 'not-run') {
        Add-Failure 'canonical-build-boundary-invalid'
    }
    if ($readiness.runtime.moduleMappingCount -ne 0 -or
        -not $readiness.runtime.explorerModuleInspectionSucceeded) {
        Add-Failure 'runtime-mapping-boundary-invalid'
    }
    if ($readiness.approval.exactCommand -ne $exactCommand -or
        $readiness.approval.recoveryCommand -ne $recoveryCommand -or
        $readiness.approval.exactCommandApproved -or
        $readiness.approval.recoveryTerminalAvailable -or
        $readiness.approval.canExecuteNow) {
        Add-Failure 'approval-boundary-invalid'
    }
}

if ($errors.Count -ne 0) {
    throw (
        'Session plan preflight failed closed: ' +
        (@($errors) -join ', ')
    )
}

$createdAt = [DateTime]::UtcNow
$runId = (
    $createdAt.ToString('yyyyMMddTHHmmssfffZ') +
    '-' +
    [Guid]::NewGuid().ToString('N').Substring(0, 8)
)
$relativeOutputPath =
    [IO.Path]::GetRelativePath($root, $resolvedOutputPath).Replace('\', '/')
$openCommand = (
    'pwsh -NoLogo -NoProfile -File .\scripts\Open-M2RecoveryTerminal.ps1 ' +
    "-SessionPlanPath .\$($relativeOutputPath.Replace('/', '\')) " +
    '-ConfirmOpen'
)
$passed = $true

$readinessReceipt = [ordered]@{
    runId = [string]$readiness.runId
    result = [string]$readiness.result
    readyForExactApproval =
        [bool]$readiness.readyForExactApproval
    compatibilityChecksPassed =
        [int]$readiness.compatibility.checksPassed
    compatibilityCheckCount =
        [int]$readiness.compatibility.checkCount
    canonicalRunId = [string]$readiness.canonicalBuild.runId
    m2SourceSha256 =
        [string]$readiness.canonicalBuild.m2SourceSha256
    warningCount = [int]$readiness.canonicalBuild.warningCount
    errorCount = [int]$readiness.canonicalBuild.errorCount
    killSwitchState = [string]$readiness.killSwitch.state
    permitState = [string]$readiness.activeModulePermit.state
    windhawkState = [string]$readiness.windhawkService.state
    windhawkStartMode =
        [string]$readiness.windhawkService.startMode
    explorerProcessId =
        [int]@($readiness.compatibility.explorerProcessIds)[0]
    moduleMappingCount =
        [int]$readiness.runtime.moduleMappingCount
    explorerModuleInspectionSucceeded =
        [bool]$readiness.runtime.explorerModuleInspectionSucceeded
}

$plan = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-m2-validation-session-plan'
    runId = $runId
    createdAtUtc = $createdAt.ToString('o')
    expiresAtUtc = $createdAt.AddMinutes($ValidityMinutes).ToString('o')
    result = if ($passed) { 'passed' } else { 'failed' }
    state = 'awaiting-exact-approval'
    moduleId = $moduleId
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    sourceIdentity = $sourceIdentity
    readiness = $readinessReceipt
    recoveryTerminal = [ordered]@{
        command = $recoveryCommand
        openCommand = $openCommand
        launchPerformed = $false
        terminalAvailable = $false
    }
    approval = [ordered]@{
        exactCommand = $exactCommand
        exactCommandApproved = $false
        canExecuteNow = $false
    }
    errors = @($errors)
}
$json = $plan | ConvertTo-Json -Depth 20

if (-not ($json | Test-Json -SchemaFile $schemaPath -ErrorAction Stop)) {
    throw 'Generated session plan failed schema validation.'
}

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

Write-Output $json
if (-not $passed) {
    exit 1
}
