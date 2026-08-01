[CmdletBinding()]
param(
    [switch]$StaticOnly,
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ExplorerCaptionPlan')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.ExplorerCaptionPlan.csproj')
$profilePath = Join-Path $root 'config\windows10-host-profiles.json'

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

$sourceFiles = @(
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        Sort-Object FullName
)
$sourceText = @(
    $sourceFiles | ForEach-Object {
        [IO.File]::ReadAllText($_.FullName)
    }
) -join [Environment]::NewLine
$readerText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'DwmCaptionReader.cs'))
$gateText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'ExplorerCaptionGate.cs'))
$plannerText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'ExplorerCaptionPlanner.cs'))
$profiles =
    Get-Content -LiteralPath $profilePath -Raw |
        ConvertFrom-Json
$profile = @($profiles.profiles)

Add-Check `
    -Name 'project.exact-readonly-dependencies' `
    -Passed (
        $sourceText.Contains(
            'Jarvis.Win10.HostAdmission.csproj') -and
        $sourceText.Contains(
            'Jarvis.Win10.ShellSurfaceProbe.csproj')) `
    -Detail (
        'The caption planner must reuse exact host admission and the reviewed ' +
        'read-only Shell topology probe.')

$forbiddenPattern = (
    '(?i)\b(?:DwmSetWindowAttribute|SetWindowCompositionAttribute|' +
    'SendMessage|PostMessage|OpenProcess|CreateRemoteThread|' +
    'VirtualAllocEx|WriteProcessMemory|ReadProcessMemory|' +
    'SetWindowsHookEx|SetWindowLong|SetWindowPos|MoveWindow|' +
    'ShowWindow|DestroyWindow|TerminateProcess|Process\.Kill|' +
    'CloseMainWindow|ServiceController|RegistryKey\.SetValue|' +
    'Registry\.SetValue|RegistryKey\.Delete|Start-Process|' +
    'Stop-Process|explorer\.exe|windhawk\.exe)\b'
)
Add-Check `
    -Name 'source.readonly-dwm-api-only' `
    -Passed (
        $readerText.Contains('DwmGetWindowAttribute(') -and
        $readerText.Contains('UseImmersiveDarkMode = 20') -and
        -not [regex]::IsMatch($sourceText, $forbiddenPattern) -and
        @([regex]::Matches($sourceText, '\[DllImport\(')).Count -eq 1) `
    -Detail (
        'The project may import only DwmGetWindowAttribute and must contain no ' +
        'window write, injection, process, service or registry transport.')

Add-Check `
    -Name 'gate.single-exact-explorer-root' `
    -Passed (
        $gateText.Contains('SelectExplorerWindow(') -and
        $gateText.Contains('expectedWindowHandle') -and
        $gateText.Contains('matches.Length == 1') -and
        $gateText.Contains('"CabinetWClass"') -and
        $gateText.Contains('rootNode.Visible') -and
        $gateText.Contains(
            'inventory.DesktopShellProcessId') -and
        $gateText.Contains('RootThreadId') -and
        $gateText.Contains('TopologySha256')) `
    -Detail (
        'Inspection must bind one explicitly selected visible CabinetWClass ' +
        'root to the admitted desktop Shell PID and record ' +
        'HWND/PID/TID/topology identity.')

Add-Check `
    -Name 'gate.locked-state-required' `
    -Passed (
        $gateText.Contains('"disabled.flag"') -and
        $gateText.Contains('"active-module.txt"') -and
        $gateText.Contains('"kill-switch-not-armed"') -and
        $gateText.Contains('"one-shot-module-permit-present"') -and
        $gateText.Contains('EnsureOrdinaryPath(') -and
        -not $gateText.Contains('File.Delete(') -and
        -not $gateText.Contains('File.Move(') -and
        -not $gateText.Contains('File.Write')) `
    -Detail (
        'The read/plan gate must require the armed kill switch and absent ' +
        'one-shot module permit without changing either file.')

Add-Check `
    -Name 'plan.original-value-and-future-rollback-contract' `
    -Passed (
        $plannerText.Contains('MinimumTtlSeconds = 10') -and
        $plannerText.Contains('MaximumTtlSeconds = 60') -and
        $plannerText.Contains('gate.CurrentCaption.Value') -and
        $plannerText.Contains(
            'durable original-value and HWND/PID/TID journal') -and
        $plannerText.Contains(
            'finally rollback to the recorded original value') -and
        $plannerText.Contains(
            'current-task approval of the exact apply command') -and
        $plannerText.Contains('previewExecutionSupported = false') -and
        $plannerText.Contains('mutationSupported = false')) `
    -Detail (
        'Planning must bind the readable original value and document every ' +
        'future recovery gate while keeping execution and mutation disabled.')

Add-Check `
    -Name 'profile.read-capability-only' `
    -Passed (
        $profile.Count -eq 1 -and
        $profile[0].profileId -eq
            'win10-22h2-19045.6466-x64' -and
        $profile[0].status -eq
            'observed-caption-write-disabled-owned-overlay-visually-verified' -and
        @($profile[0].allowedCapabilities) -contains
            'read-single-explorer-caption-state' -and
        @($profile[0].allowedCapabilities) -notcontains
            'run-bounded-single-explorer-dark-caption-preview' -and
        -not $profile[0].activationPermitted -and
        $profile[0].liveExplorer -eq 'not-run') `
    -Detail (
        'The exact profile retains caption reads but revokes another write ' +
        'after two no-change visual observations; this planner has no write ' +
        'API or module activation.')

$buildOutput = @(
    & $DotnetPath build `
        $projectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release-warning-free' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 10) -join
        [Environment]::NewLine)

$modelReceipt = $null
if ($buildExitCode -eq 0) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-explorer-caption-plan.dll')
    $modelOutput = @(
        & $DotnetPath $assemblyPath model-test 2>&1
    )
    $modelExitCode = $LASTEXITCODE
    try {
        $modelReceipt =
            ($modelOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $modelReceipt = $null
    }
    Add-Check `
        -Name 'model.fail-closed-scenarios' `
        -Passed (
            $modelExitCode -eq 0 -and
            $null -ne $modelReceipt -and
            $modelReceipt.result -eq 'passed' -and
            $modelReceipt.scenarioCount -eq 16 -and
            $modelReceipt.passedCount -eq 16 -and
            -not $modelReceipt.previewExecutionSupported -and
            -not $modelReceipt.mutationSupported -and
            -not $modelReceipt.activationPermitted -and
            -not $modelReceipt.mutationPerformed -and
            $modelReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Model exit $modelExitCode; scenarios " +
            "$($modelReceipt.passedCount)/$($modelReceipt.scenarioCount).")
}

if ($buildExitCode -eq 0 -and -not $StaticOnly) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-explorer-caption-plan.dll')
    $explorerBefore = @(
        Get-Process -Name explorer -ErrorAction Stop |
            ForEach-Object {
                "$($_.Id):$($_.StartTime.ToUniversalTime().Ticks)"
            } |
            Sort-Object
    )
    $surfaceAssembly = Join-Path $root (
        'src\platforms\windows10\Jarvis.Win10.ShellSurfaceProbe\' +
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-shell-surface-probe.dll')
    $surfaceOutput = @(
        & $DotnetPath $surfaceAssembly inspect 2>&1
    )
    $surfaceReceipt = $null
    try {
        $surfaceReceipt =
            ($surfaceOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $surfaceReceipt = $null
    }
    $candidate = @(
        $surfaceReceipt.inventory.explorerWindows |
            Sort-Object rootWindow |
            Select-Object -First 1
    )
    $candidateHandle = if ($candidate.Count -eq 1) {
        [string]$candidate[0].rootWindow
    }
    else {
        'no-candidate'
    }
    $inspectOutput = @(
        & $DotnetPath $assemblyPath inspect `
            --expected-window-handle $candidateHandle 2>&1
    )
    $inspectExitCode = $LASTEXITCODE
    $planOutput = @(
        & $DotnetPath $assemblyPath plan-preview `
            --expected-window-handle $candidateHandle `
            --ttl-seconds 30 2>&1
    )
    $planExitCode = $LASTEXITCODE
    $explorerAfter = @(
        Get-Process -Name explorer -ErrorAction Stop |
            ForEach-Object {
                "$($_.Id):$($_.StartTime.ToUniversalTime().Ticks)"
            } |
            Sort-Object
    )
    $inspectReceipt = $null
    $planReceipt = $null
    try {
        $inspectReceipt =
            ($inspectOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
        $planReceipt =
            ($planOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $inspectReceipt = $null
        $planReceipt = $null
    }
    Add-Check `
        -Name 'live.readonly-single-explorer-caption-plan' `
        -Passed (
            $inspectExitCode -eq 0 -and
            $planExitCode -eq 0 -and
            $candidate.Count -eq 1 -and
            $null -ne $inspectReceipt -and
            $null -ne $planReceipt -and
            $inspectReceipt.result -eq
                'passed-single-explorer-caption-read' -and
            $inspectReceipt.target.rootClass -eq 'CabinetWClass' -and
            $inspectReceipt.target.windowHandle -eq $candidateHandle -and
            $inspectReceipt.currentCaption.attribute -eq 20 -and
            $inspectReceipt.currentCaption.value -in @(0, 1) -and
            -not $inspectReceipt.previewExecutionSupported -and
            -not $inspectReceipt.mutationSupported -and
            -not $inspectReceipt.mutationPerformed -and
            $planReceipt.result -eq 'passed-read-only-plan' -and
            $planReceipt.ttlSeconds -eq 30 -and
            -not $planReceipt.previewExecutionSupported -and
            -not $planReceipt.mutationSupported -and
            -not $planReceipt.mutationPerformed -and
            (($explorerBefore -join '|') -eq
                ($explorerAfter -join '|'))) `
        -Detail (
            "Inspect exit $inspectExitCode; plan exit $planExitCode; " +
            "Explorer identity unchanged.")
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-explorer-caption-plan-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scenarioCount = if ($null -eq $modelReceipt) {
        0
    }
    else {
        $modelReceipt.scenarioCount
    }
    scenarioPassedCount = if ($null -eq $modelReceipt) {
        0
    }
    else {
        $modelReceipt.passedCount
    }
    previewExecutionSupported = $false
    mutationSupported = $false
    activationPermitted = $false
    liveMutationRun = $false
    mutationPerformed = $false
    liveExplorer = if ($StaticOnly) {
        'not-run'
    }
    else {
        'read-only-inspection'
    }
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
