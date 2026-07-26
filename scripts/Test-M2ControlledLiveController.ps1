[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$controllerPath =
    Join-Path $root 'scripts\Invoke-M2ControlledLiveValidation.ps1'
$plannerPath =
    Join-Path $root 'scripts\New-M2ValidationSessionPlan.ps1'
$planSchemaPath =
    Join-Path $root 'config\m2-validation-session-plan.schema.json'
$controllerReceiptSchemaPath =
    Join-Path $root 'config\m2-controlled-live-controller-receipt.schema.json'
$leaseValidatorPath =
    Join-Path $root 'src\Jarvis.Supervisor\RecoveryTerminalLease.cs'
$publicationManifestPath =
    Join-Path $root 'config\publication-manifest.json'

$checks = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

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

function Test-MarkersInOrder {
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string[]]$Markers
    )

    $offset = 0
    foreach ($marker in $Markers) {
        $index = $Text.IndexOf(
            $marker,
            $offset,
            [StringComparison]::Ordinal)
        if ($index -lt 0) {
            return $false
        }
        $offset = $index + $marker.Length
    }
    return $true
}

function Get-FunctionText {
    param(
        [Parameter(Mandatory)]
        [Management.Automation.Language.ScriptBlockAst]$Ast,
        [Parameter(Mandatory)] [string]$Name
    )

    $function = @(
        $Ast.FindAll({
            param($node)
            $node -is
                [Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq $Name
        }, $true)
    )
    if ($function.Count -ne 1) {
        return ''
    }
    return $function[0].Extent.Text
}

function Test-ControllerReceipt {
    param([Parameter(Mandatory)] [object]$Value)

    $json = $Value | ConvertTo-Json -Depth 30
    return [bool]($json |
        Test-Json `
            -SchemaFile $controllerReceiptSchemaPath `
            -ErrorAction SilentlyContinue)
}

$controller = [IO.File]::ReadAllText($controllerPath)
$planner = [IO.File]::ReadAllText($plannerPath)
$leaseValidator = [IO.File]::ReadAllText($leaseValidatorPath)
$planSchema =
    Get-Content -LiteralPath $planSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$controllerReceiptSchema =
    Get-Content -LiteralPath $controllerReceiptSchemaPath -Raw |
        ConvertFrom-Json -Depth 100
$publicationManifest =
    Get-Content -LiteralPath $publicationManifestPath -Raw |
        ConvertFrom-Json -Depth 100

$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $controllerPath,
    [ref]$tokens,
    [ref]$parseErrors)
$syntaxDetail = if ($parseErrors.Count -eq 0) {
    'Controller parsed without PowerShell syntax errors.'
}
else {
    ($parseErrors | ForEach-Object Message) -join '; '
}
Add-Check `
    -Name 'controller.syntax' `
    -Passed ($parseErrors.Count -eq 0) `
    -Detail $syntaxDetail

$parameterBlock = $ast.ParamBlock.Extent.Text
$defaultInert =
    $parameterBlock.Contains("[string]`$Action = 'Inspect'") -and
    $parameterBlock.Contains("'UpdateDisabledInstallation'") -and
    $parameterBlock.Contains("'StartDisabledHost'") -and
    $parameterBlock.Contains("'EnableOnce'") -and
    $parameterBlock.Contains("'Observe'") -and
    $parameterBlock.Contains("'Recover'")
Add-Check `
    -Name 'controller.default-inert-actions-separated' `
    -Passed $defaultInert `
    -Detail 'The default action must be read-only Inspect and every live phase must be separately selected.'

$updateText =
    Get-FunctionText $ast 'Invoke-UpdateDisabledInstallationAction'
$startText = Get-FunctionText $ast 'Invoke-StartDisabledHostAction'
$enableText = Get-FunctionText $ast 'Invoke-EnableOnceAction'
$observeText = Get-FunctionText $ast 'Invoke-ObserveAction'
$recoverText = Get-FunctionText $ast 'Invoke-RecoverAction'
$failClosedText =
    Get-FunctionText $ast 'Invoke-FailClosedAfterEnableError'

$confirmationsBeforeMutation =
    (Test-MarkersInOrder $updateText @(
        '-Condition $ConfirmUpdateDisabledInstallation',
        'Assert-SessionPlanIdentity',
        'Assert-LockedPlanDryRun',
        'Retire-StaleRecoveryLease',
        'Install-FileAtomically'
    )) -and
    (Test-MarkersInOrder $startText @(
        '-Condition $ConfirmStartDisabledHost',
        'Assert-PlanAndRecoveryLease',
        'Start-Service -Name $serviceName'
    )) -and
    (Test-MarkersInOrder $enableText @(
        '-Condition $ConfirmEnableOnce',
        'Assert-PlanAndRecoveryLease',
        'Assert-ExactPermit',
        'Set-ItemProperty'
    )) -and
    (Test-MarkersInOrder $recoverText @(
        '-Condition $ConfirmRecover',
        'Test-Path -LiteralPath $killSwitchPath',
        '-not (Test-Path -LiteralPath $permitPath)',
        'Set-ItemProperty',
        'Stop-Service -Name $serviceName'
    ))
Add-Check `
    -Name 'controller.confirmations-before-mutation' `
    -Passed $confirmationsBeforeMutation `
    -Detail 'Each mutating action must prove its own confirmation and state gate before its first mutation.'

$disabledInstallationContract =
    $updateText.Contains('$oldInstalledSourceSha256') -and
    $updateText.Contains('$oldInstalledDllSha256') -and
    $updateText.Contains('$expectedSourceSha256') -and
    $updateText.Contains('$expectedDllSha256') -and
    $updateText.Contains("'Stopped'") -and
    $updateText.Contains("'Manual'") -and
    $updateText.Contains('ExpectedCount 0') -and
    $updateText.Contains('Assert-LockedPlanDryRun') -and
    $updateText.Contains('Retire-StaleRecoveryLease') -and
    $updateText.Contains('Install-FileAtomically') -and
    $updateText.Contains('rollback was incomplete') -and
    -not [regex]::IsMatch(
        $updateText,
        '(?i)\b(?:Start-Service|Stop-Service|Set-ItemProperty|Stop-Process)\b')
Add-Check `
    -Name 'controller.disabled-installation-atomic' `
    -Passed $disabledInstallationContract `
    -Detail 'The plan-bound disabled installer must require exact old/new hashes, zero mappings, no service start and verified rollback.'

$startDisabledContract =
    $startText.Contains("'Stopped'") -and
    $startText.Contains("'Manual'") -and
    $startText.Contains('M2 must remain disabled') -and
    $startText.Contains('Start-Service -Name $serviceName') -and
    $startText.Contains('Stop-Service -Name $serviceName') -and
    $startText.Contains('ExpectedCount 0') -and
    -not $startText.Contains('Set-Service')
Add-Check `
    -Name 'controller.start-disabled-scm-only' `
    -Passed $startDisabledContract `
    -Detail 'Windhawk may start only through SCM with M2 disabled and Manual mode preserved; a failed start is normally stopped.'

$enableFailClosedContract =
    $enableText.Contains('$script:stopRequired = $true') -and
    $enableText.Contains('Invoke-FailClosedAfterEnableError') -and
    $enableText.Contains('ExpectedCount 0') -and
    $enableText.Contains('-Value 0') -and
    $enableText.Contains('Assert-ActiveState') -and
    $enableText.Contains('$script:stopRequired = $false') -and
    (Test-MarkersInOrder $failClosedText @(
        'Invoke-ArmKillSwitch',
        'Set-ItemProperty',
        '-Value 1',
        'Stop-Service -Name $serviceName'
    ))
Add-Check `
    -Name 'controller.enable-once-fails-closed' `
    -Passed $enableFailClosedContract `
    -Detail 'EnableOnce must require a zero-mapping prestate and recover in arm-disable-stop order after any failed preflight or load.'

$observationContract =
    $observeText.Contains('Assert-ActiveState') -and
    $observeText.Contains('$ObservationSeconds') -and
    $observeText.Contains('$MaxSingleCoreCpuPercent') -and
    $observeText.Contains('$consecutiveElevatedSamples -lt 3') -and
    $observeText.Contains('$script:stopRequired = $true') -and
    $observeText.Contains('$script:stopRequired = $false') -and
    -not [regex]::IsMatch(
        $observeText,
        '(?i)\b(?:Start-Service|Stop-Service|Set-ItemProperty|Stop-Process)\b')
Add-Check `
    -Name 'controller.observer-bounded-readonly' `
    -Passed $observationContract `
    -Detail 'Observe must be bounded and read-only while checking the lease, exact mapping, Explorer liveness and sustained CPU.'

$recoveryContract =
    $recoverText.Contains(
        'Recovery deliberately does not require a live session plan or lease') -and
    $recoverText.Contains('disabled.flag') -eq $false -and
    $recoverText.Contains('Test-Path -LiteralPath $killSwitchPath') -and
    $recoverText.Contains('-not (Test-Path -LiteralPath $permitPath)') -and
    $recoverText.Contains('-Value 1') -and
    $recoverText.Contains('Stop-Service -Name $serviceName') -and
    $recoverText.Contains('targetExplorerMappingCount') -and
    $recoverText.Contains('allWindhawkAndJarvisMappings') -and
    $recoverText.Contains('forceStopRequested = $false')
Add-Check `
    -Name 'controller.recovery-usable-after-terminal-loss' `
    -Passed $recoveryContract `
    -Detail 'Recovery must require the already-armed state, remain usable after lease loss, stop normally and record residual mappings.'

$forbiddenExecutionSurface =
    -not [regex]::IsMatch(
        $controller,
        '(?i)\b(?:Set-Service|Stop-Process|Start-Process|taskkill|Restart-Computer|shutdown\.exe)\b') -and
    -not $controller.Contains('restart-explorer') -and
    -not $controller.Contains('Windhawk\windhawk.exe') -and
    -not [regex]::IsMatch(
        $controller,
        "(?s)Invoke-Supervisor\\s+.*?['""]clear-kill-switch['""]")
Add-Check `
    -Name 'controller.no-force-restart-or-clear' `
    -Passed $forbiddenExecutionSurface `
    -Detail 'The controller must never change start mode, launch Windhawk UI, terminate processes, restart Explorer or execute clear-kill-switch.'

$receiptSchemaContract =
    $controllerReceiptSchema.properties.schemaVersion.const -eq 1 -and
    $controllerReceiptSchema.properties.receiptType.const -eq
        'jarvisv2-m2-controlled-live-controller' -and
    $controllerReceiptSchema.properties.activationPermitted.const -eq
        $false -and
    $controllerReceiptSchema.properties.explorerRestartRequested.const -eq
        $false -and
    $controllerReceiptSchema.properties.processTerminationRequested.const -eq
        $false -and
    $controllerReceiptSchema.properties.serviceStartModeMutationRequested.const -eq
        $false -and
    @($controllerReceiptSchema.properties.action.enum).Count -eq 6 -and
    @($controllerReceiptSchema.oneOf).Count -eq 9 -and
    $controller.Contains('$controllerReceiptSchemaPath') -and
    $controller.Contains(
        'Controller result failed its committed receipt schema.') -and
    $controller.Contains('activationPermitted = $false') -and
    $controller.Contains('controllerSha256 = Get-Sha256 $PSCommandPath') -and
    $controller.Contains(
        'receiptSchemaSha256 = Get-Sha256 $controllerReceiptSchemaPath') -and
    $controller.Contains("'controlled-session'")
Add-Check `
    -Name 'controller.receipt-schema-enforced' `
    -Passed $receiptSchemaContract `
    -Detail 'Every controller result must validate against a committed action-specific schema with non-activation, no-restart and no-termination boundaries.'

$fixtureRunId = '20260726T191234567Z-deadbeef'
$fixtureSha = 'A' * 64
$fixtureMapping = [ordered]@{
    process = 'explorer'
    processId = 11640
    module = 'windhawk.dll'
    path = 'C:\Program Files\Windhawk\Engine\1.7.3\64\windhawk.dll'
    isJarvis = $false
}
function New-ReceiptFixture {
    param(
        [Parameter(Mandatory)] [string]$Action,
        [Parameter(Mandatory)] [string]$Result,
        [Parameter(Mandatory)] [string]$LiveExplorer,
        [Parameter(Mandatory)] [bool]$MutationPerformed,
        [Parameter(Mandatory)] [bool]$StopRequired,
        [Parameter(Mandatory)] [object]$Detail
    )

    return [ordered]@{
        schemaVersion = 1
        receiptType = 'jarvisv2-m2-controlled-live-controller'
        action = $Action
        result = $Result
        moduleId = 'jarvis-taskbar-icon-size'
        controllerSha256 = $fixtureSha
        receiptSchemaSha256 = $fixtureSha
        observedAtUtc = '2026-07-26T19:12:34.567Z'
        expectedExplorerProcessId = 11640
        activationPermitted = $false
        liveExplorer = $LiveExplorer
        mutationPerformed = $MutationPerformed
        stopRequired = $StopRequired
        explorerRestartRequested = $false
        processTerminationRequested = $false
        serviceStartModeMutationRequested = $false
        exactClearCommand = (
            'dotnet run --project .\src\Jarvis.Supervisor ' +
            '--configuration Release --no-build -- clear-kill-switch ' +
            '--module jarvis-taskbar-icon-size --confirm'
        )
        emergencyArmCommand = (
            'dotnet run --project .\src\Jarvis.Supervisor ' +
            '--configuration Release --no-build -- arm-kill-switch'
        )
        detail = $Detail
    }
}

$inspectDetail = [ordered]@{
    compatibilityPassed = $true
    compatibilityCheckCount = 23
    explorerProcessId = 11640
    killSwitchState = 'armed'
    permitState = 'absent'
    permitModuleId = $null
    serviceState = 'Stopped'
    serviceStartMode = 'Manual'
    serviceProcessId = 0
    configuredModule = 'local@jarvis-taskbar-icon-size'
    installedGeneration = 'phase4-reviewed-old'
    targetDisabled = $true
    configError = 'Phase 5 canonical files are not installed.'
    recoveryTerminalReady = $false
    recoveryTerminalStatus = 'blocked'
    recoveryTerminalError = 'lease-heartbeat-stale'
    explorerJarvisMappings = @()
    allJarvisMappings = @()
    allWindhawkAndJarvisMappings = @()
    planInspection = $null
}
$updateDetail = [ordered]@{
    sessionPlanRunId = $fixtureRunId
    sessionPlanSha256 = $fixtureSha
    explorerProcessId = 11640
    staleRecoveryLeaseRetired = $true
    targetDisabled = $true
    serviceState = 'Stopped'
    serviceStartMode = 'Manual'
    serviceProcessId = 0
    killSwitchArmed = $true
    permitPresent = $false
    sourceSha256 =
        'CD7760DBC1111B0608599D49F8694AAA1C53D2A828AF362D3383995853D38CFB'
    dllSha256 =
        'C2DB007E2FDCDA145463E2D0355BD4F7E18ACC9CE414D77652EED33DD5532865'
    backupDirectory = 'C:\repo\artifacts\backup'
    targetMappingCount = 0
}
$startDetail = [ordered]@{
    sessionPlanRunId = $fixtureRunId
    sessionPlanSha256 = $fixtureSha
    recoveryTerminalProcessId = 1234
    explorerProcessId = 11640
    serviceState = 'Running'
    serviceStartMode = 'Manual'
    serviceProcessId = 4321
    targetDisabled = $true
    permitPresent = $false
    killSwitchArmed = $true
    targetMappingCount = 0
}
$enableDetail = [ordered]@{
    sessionPlanRunId = $fixtureRunId
    sessionPlanSha256 = $fixtureSha
    recoveryTerminalProcessId = 1234
    explorerProcessId = 11640
    serviceState = 'Running'
    serviceStartMode = 'Manual'
    serviceProcessId = 4321
    targetDisabled = $false
    permitConsumed = $true
    killSwitchArmed = $false
    targetMappingCount = 1
    targetDllSha256 =
        'C2DB007E2FDCDA145463E2D0355BD4F7E18ACC9CE414D77652EED33DD5532865'
}
$observeDetail = [ordered]@{
    sessionPlanRunId = $fixtureRunId
    sessionPlanSha256 = $fixtureSha
    explorerProcessId = 11640
    observationSeconds = 3
    maxAllowedSingleCoreCpuPercent = 25.0
    maximumObservedSingleCoreCpuPercent = 0.0
    sampleCount = 1
    samples = @(
        [ordered]@{
            sampledAtUtc = '2026-07-26T19:12:35.567Z'
            approximateSingleCoreCpuPercent = 0.0
            workingSetBytes = 1024
            handleCount = 1
            threadCount = 1
            recoveryTerminalProcessId = 1234
        }
    )
}
$recoverDetail = [ordered]@{
    errors = @()
    expectedExplorerProcessId = 11640
    actualExplorerProcessId = 11640
    explorerProcessIdStable = $true
    targetDisabled = $true
    serviceState = 'Stopped'
    serviceStartMode = 'Manual'
    serviceProcessId = 0
    killSwitchArmed = $true
    permitPresent = $false
    targetExplorerMappingCount = 0
    allWindhawkAndJarvisMappings = @($fixtureMapping)
    explorerRestartRequested = $false
    forceStopRequested = $false
}
$failureDetail = [ordered]@{
    error = 'confirmation missing'
    exceptionType = 'System.InvalidOperationException'
}
$recoverZeroDetail =
    $recoverDetail |
        ConvertTo-Json -Depth 30 |
        ConvertFrom-Json -Depth 30
$recoverZeroDetail.allWindhawkAndJarvisMappings = @()
$recoverNeedsDetail =
    $recoverDetail |
        ConvertTo-Json -Depth 30 |
        ConvertFrom-Json -Depth 30
$recoverNeedsDetail.errors = @('mapping residual')
$positiveReceiptFixtures = @(
    (New-ReceiptFixture 'Inspect' 'passed-read-only' 'not-run' $false $false $inspectDetail),
    (New-ReceiptFixture 'UpdateDisabledInstallation' 'passed-disabled-installation-updated' 'not-run' $true $false $updateDetail),
    (New-ReceiptFixture 'StartDisabledHost' 'passed-disabled-host-running' 'not-run' $true $false $startDetail),
    (New-ReceiptFixture 'EnableOnce' 'passed-one-shot-m2-active' 'controlled-session' $true $false $enableDetail),
    (New-ReceiptFixture 'Observe' 'passed-bounded-idle-observation' 'controlled-session' $false $false $observeDetail),
    (New-ReceiptFixture 'Recover' 'passed-locked-zero-mappings' 'controlled-session' $true $false $recoverZeroDetail),
    (New-ReceiptFixture 'Recover' 'passed-locked-runtime-residual-recorded' 'controlled-session' $true $false $recoverDetail),
    (New-ReceiptFixture 'Recover' 'failed-locked-state-needs-attention' 'controlled-session' $true $true $recoverNeedsDetail),
    (New-ReceiptFixture 'EnableOnce' 'failed' 'not-run' $false $true $failureDetail)
)
$positiveReceiptResults =
    @($positiveReceiptFixtures | ForEach-Object {
        Test-ControllerReceipt $_
    })
Add-Check `
    -Name 'controller.receipt-schema-positive-matrix' `
    -Passed (
        $positiveReceiptResults.Count -eq
            $positiveReceiptFixtures.Count -and
        @($positiveReceiptResults | Where-Object { -not $_ }).Count -eq 0
    ) `
    -Detail 'Representative Inspect, update, start, enable, observe, recovery, residual and failure receipts must all validate.'

$negativeReceiptFixtures = [System.Collections.Generic.List[object]]::new()
foreach ($mutation in @(
    [pscustomobject]@{
        Property = 'activationPermitted'
        Value = $true
    },
    [pscustomobject]@{
        Property = 'explorerRestartRequested'
        Value = $true
    },
    [pscustomobject]@{
        Property = 'processTerminationRequested'
        Value = $true
    },
    [pscustomobject]@{
        Property = 'serviceStartModeMutationRequested'
        Value = $true
    },
    [pscustomobject]@{
        Property = 'liveExplorer'
        Value = 'controlled-session'
    },
    [pscustomobject]@{
        Property = 'mutationPerformed'
        Value = $true
    }
)) {
    $fixture =
        $positiveReceiptFixtures[0] |
            ConvertTo-Json -Depth 30 |
            ConvertFrom-Json -Depth 30
    $fixture.($mutation.Property) = $mutation.Value
    $negativeReceiptFixtures.Add($fixture)
}
$wrongActionResult =
    $positiveReceiptFixtures[2] |
        ConvertTo-Json -Depth 30 |
        ConvertFrom-Json -Depth 30
$wrongActionResult.result = 'passed-one-shot-m2-active'
$negativeReceiptFixtures.Add($wrongActionResult)
$unexpectedProperty =
    $positiveReceiptFixtures[0] |
        ConvertTo-Json -Depth 30 |
        ConvertFrom-Json -Depth 30
$unexpectedProperty | Add-Member -NotePropertyName unexpected -NotePropertyValue $true
$negativeReceiptFixtures.Add($unexpectedProperty)
$zeroWithResidual =
    $positiveReceiptFixtures[5] |
        ConvertTo-Json -Depth 30 |
        ConvertFrom-Json -Depth 30
$zeroWithResidual.detail.allWindhawkAndJarvisMappings = @($fixtureMapping)
$negativeReceiptFixtures.Add($zeroWithResidual)
$wrongUpdateHash =
    $positiveReceiptFixtures[1] |
        ConvertTo-Json -Depth 30 |
        ConvertFrom-Json -Depth 30
$wrongUpdateHash.detail.sourceSha256 = $fixtureSha
$negativeReceiptFixtures.Add($wrongUpdateHash)
$wrongEnableHash =
    $positiveReceiptFixtures[3] |
        ConvertTo-Json -Depth 30 |
        ConvertFrom-Json -Depth 30
$wrongEnableHash.detail.targetDllSha256 = $fixtureSha
$negativeReceiptFixtures.Add($wrongEnableHash)
$residualClaimWithJarvis =
    $positiveReceiptFixtures[6] |
        ConvertTo-Json -Depth 30 |
        ConvertFrom-Json -Depth 30
$residualClaimWithJarvis.detail.allWindhawkAndJarvisMappings[0].isJarvis =
    $true
$negativeReceiptFixtures.Add($residualClaimWithJarvis)
$negativeReceiptResults =
    @($negativeReceiptFixtures | ForEach-Object {
        Test-ControllerReceipt $_
    })
Add-Check `
    -Name 'controller.receipt-schema-negative-guardrails' `
    -Passed (
        $negativeReceiptResults.Count -eq
            $negativeReceiptFixtures.Count -and
        @($negativeReceiptResults | Where-Object { $_ }).Count -eq 0
    ) `
    -Detail 'The schema must reject activation authority, restart, termination, start-mode mutation, false live or mutation claims, action/result mismatch, extra properties, zero-mapping contradictions, Jarvis residuals and non-canonical installation or active DLL hashes.'

$planRequired = @($planSchema.properties.sourceIdentity.required)
$planBinding =
    $planRequired -contains 'controlledLiveController' -and
    $planRequired -contains 'controlledLiveReceiptSchema' -and
    $null -ne
        $planSchema.properties.sourceIdentity.properties.controlledLiveController -and
    $null -ne
        $planSchema.properties.sourceIdentity.properties.controlledLiveReceiptSchema -and
    $planner.Contains("Key = 'controlledLiveController'") -and
    $planner.Contains("Key = 'controlledLiveReceiptSchema'") -and
    $planner.Contains(
        "RelativePath = 'scripts/Invoke-M2ControlledLiveValidation.ps1'") -and
    $planner.Contains(
        "'config/m2-controlled-live-controller-receipt.schema.json'") -and
    $leaseValidator.Contains(
        '["controlledLiveController"] = "scripts/Invoke-M2ControlledLiveValidation.ps1"') -and
    $leaseValidator.Contains(
        '["controlledLiveReceiptSchema"] = "config/m2-controlled-live-controller-receipt.schema.json"')
Add-Check `
    -Name 'controller.session-plan-sha-bound' `
    -Passed $planBinding `
    -Detail 'The schema, planner and Supervisor must bind the exact controller path, size and SHA-256.'

$publicationFiles = @($publicationManifest.requiredFiles)
$publicationBoundary =
    $publicationFiles -contains
        'scripts/Invoke-M2ControlledLiveValidation.ps1' -and
    $publicationFiles -contains
        'config/m2-controlled-live-controller-receipt.schema.json' -and
    $publicationFiles -contains
        'scripts/Test-M2ControlledLiveController.ps1'
Add-Check `
    -Name 'controller.publication-boundary' `
    -Passed $publicationBoundary `
    -Detail 'The controller and its read-only audit must be explicit public repository files.'

$self = [IO.File]::ReadAllText($PSCommandPath)
$selfTokens = $null
$selfParseErrors = $null
$selfAst = [Management.Automation.Language.Parser]::ParseFile(
    $PSCommandPath,
    [ref]$selfTokens,
    [ref]$selfParseErrors)
$selfCommandNames = @(
    $selfAst.FindAll({
        param($node)
        $node -is [Management.Automation.Language.CommandAst]
    }, $true) |
        ForEach-Object { $_.GetCommandName() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
$selfReadOnly =
    $selfParseErrors.Count -eq 0 -and
    @($selfCommandNames | Where-Object {
        $_ -match '(?i)^(?:Start-Service|Stop-Service|Set-Service|Set-ItemProperty|Stop-Process|Start-Process|taskkill|dotnet)$'
    }).Count -eq 0
Add-Check `
    -Name 'controller.audit-readonly' `
    -Passed $selfReadOnly `
    -Detail 'This audit may parse source and configuration only; it must not mutate the host.'

$passed = $failures.Count -eq 0
$result = [ordered]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-m2-controlled-live-controller-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    controllerSha256 =
        (Get-FileHash -LiteralPath $controllerPath -Algorithm SHA256).Hash
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
}
$result | ConvertTo-Json -Depth 12
if (-not $passed) {
    exit 1
}
