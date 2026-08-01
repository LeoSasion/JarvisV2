[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ExplorerCaptionSession')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.ExplorerCaptionSession.csproj')
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
$controllerText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'ExplorerCaptionSessionController.cs'))
$journalText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'ExplorerCaptionSessionJournal.cs'))
$targetText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'NativeExplorerCaptionTarget.cs'))
$writerText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'DwmCaptionWriter.cs'))
$buildReceiptScriptText = [IO.File]::ReadAllText(
    (Join-Path $root (
        'scripts\New-Windows10ExplorerCaptionBuildReceipt.ps1')))
$profiles =
    Get-Content -LiteralPath $profilePath -Raw |
        ConvertFrom-Json
$profile = @($profiles.profiles)

Add-Check `
    -Name 'project.profile-bound-read-plan-dependency' `
    -Passed (
        $sourceText.Contains(
            'Jarvis.Win10.ExplorerCaptionPlan.csproj') -and
        $controllerText.Contains(
            'ExplorerCaptionGate.Inspect(expectedWindowHandle)') -and
        $controllerText.Contains(
            'ExplorerCaptionSessionPolicy.RequiredCapability')) `
    -Detail (
        'Apply must reuse the exact read gate and require the separate ' +
        'profile-bound write capability.')

$forbiddenPattern = (
    '(?i)\b(?:SetWindowCompositionAttribute|SendMessage|PostMessage|' +
    'OpenProcess|CreateRemoteThread|VirtualAllocEx|WriteProcessMemory|' +
    'ReadProcessMemory|SetWindowsHookEx|SetWindowLong|SetWindowPos|' +
    'MoveWindow|ShowWindow|DestroyWindow|TerminateProcess|Process\.Kill|' +
    'CloseMainWindow|ServiceController|RegistryKey\.SetValue|' +
    'Registry\.SetValue|RegistryKey\.Delete|Start-Process|Stop-Process|' +
    'explorer\.exe|windhawk\.exe)\b'
)
Add-Check `
    -Name 'source.no-injection-lifecycle-or-system-mutation' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenPattern)) `
    -Detail (
        'The session may change only one DWM caption boolean; injection, ' +
        'hooks, process access, Explorer lifecycle and registry APIs are ' +
        'forbidden.')

$allowedImports = @(
    'DwmSetWindowAttribute',
    'DwmFlush',
    'RedrawWindow',
    'IsWindow',
    'GetClassNameW',
    'GetWindowThreadProcessId'
)
$importMatches = @(
    [regex]::Matches(
        $sourceText,
        '(?s)\[DllImport\((?<body>.*?)\)\]\s*(?:\[return:.*?\]\s*)?' +
        'private static extern \w+\s+(?<name>\w+)')
)
$actualImports = @(
    $importMatches | ForEach-Object {
        $entryPointMatch = [regex]::Match(
            $_.Groups['body'].Value,
            'EntryPoint\s*=\s*"(?<entry>[^"]+)"')
        if ($entryPointMatch.Success) {
            $entryPointMatch.Groups['entry'].Value
        }
        else {
            $_.Groups['name'].Value
        }
    }
)
Add-Check `
    -Name 'source.exact-native-allowlist' `
    -Passed (
        $actualImports.Count -eq $allowedImports.Count -and
        @($actualImports | Where-Object {
            $_ -notin $allowedImports
        }).Count -eq 0 -and
        @($allowedImports | Where-Object {
            $_ -notin $actualImports
        }).Count -eq 0 -and
        $writerText.Contains('UseImmersiveDarkMode = 20')) `
    -Detail (
        'Imports must be exactly one documented DWM boolean writer, DwmFlush, ' +
        'one nonclient redraw request and three exact-window identity readers. ' +
        'Actual: ' +
        "$($actualImports -join ', ').")

Add-Check `
    -Name 'session.original-first-exact-target-before-set' `
    -Passed (
        (Test-MarkersInOrder `
            -Text $controllerText `
            -Markers @(
                'store.Prepare(journal);',
                'ExplorerCaptionGate.Inspect(target.WindowHandle)',
                'journal.ApplyAttempted = true;',
                'journal.MutationMayHaveOccurred = true;',
                'store.Update(journal);',
                'DwmCaptionWriter.SetDarkCaption('
            )) -and
        $controllerText.Contains(
            'preApply.Receipt.CurrentCaption.Value !=') -and
        $controllerText.Contains(
            'NativeExplorerCaptionTarget.IsSameTarget(') -and
        $targetText.Contains('expected.ProcessId') -and
        $targetText.Contains('expected.ThreadId') -and
        $targetText.Contains('expected.RootClass')) `
    -Detail (
        'The original boolean and HWND/PID/TID/class must be durable and ' +
        'freshly revalidated before the only SET call.')

Add-Check `
    -Name 'session.finally-rollback-and-independent-recovery' `
    -Passed (
        $controllerText.Contains('finally') -and
        $controllerText.Contains('RollBackExactTarget(journal);') -and
        $controllerText.Contains(
            'restored.Value == journal.OriginalValue') -and
        $controllerText.Contains(
            'journal.ApplyNonClientRefreshRequested = true;') -and
        $controllerText.Contains(
            'journal.RollbackNonClientRefreshRequested = true;') -and
        $writerText.Contains('RedrawInvalidate = 0x0001') -and
        $writerText.Contains('RedrawNoChildren = 0x0040') -and
        $writerText.Contains('RedrawFrame = 0x0400') -and
        $controllerText.Contains(
            'passed-target-retired-no-send') -and
        $controllerText.Contains(
            'public int Rollback(string sessionPath, bool confirmed)') -and
        $controllerText.Contains(
            'store.Load(sessionPath)') -and
        $targetText.Contains('TryValidateExact(')) `
    -Detail (
        'Apply and rollback must request a nonclient-only repaint; TTL, Ctrl+C ' +
        'and exceptions must converge on readback-verified rollback, while the ' +
        'emergency path validates the stored target without fresh admission.')

Add-Check `
    -Name 'policy.explicit-confirmations-and-sixty-second-cap' `
    -Passed (
        $sourceText.Contains(
            '--confirm-live-single-explorer-dark-caption') -and
        $sourceText.Contains(
            '--confirm-live-single-explorer-dark-caption-rollback') -and
        $sourceText.Contains('MinimumTtlSeconds = 10') -and
        $sourceText.Contains('MaximumTtlSeconds = 60')) `
    -Detail (
        'Apply and emergency rollback require separate exact confirmations ' +
        'and every preview is bounded to 10-60 seconds.')

Add-Check `
    -Name 'journal.path-and-broad-action-denials' `
    -Passed (
        $journalText.Contains('"JARVIS2"') -and
        $journalText.Contains('"ExplorerCaption"') -and
        $journalText.Contains('EnsureNoReparsePoints(') -and
        $journalText.Contains('ResolveJournalSessionPath(') -and
        $journalText.Contains(
            'string sessionPath = ResolveJournalSessionPath(journal);') -and
        $journalText.Contains(
            'string activePath = ResolveSessionPath(ActiveSessionPath);') -and
        $journalText.Contains('FileOptions.WriteThrough') -and
        $controllerText.Contains('InjectionRequested = false') -and
        $controllerText.Contains('ExplorerRestartRequested = false') -and
        $controllerText.Contains('ProcessTerminationRequested = false') -and
        $controllerText.Contains('RegistryMutationRequested = false') -and
        $controllerText.Contains('ModuleActivationPermitted = false')) `
    -Detail (
        'The journal must be durable and rebound to its canonical run path, ' +
        'and every receipt must deny injection, restart, termination, ' +
        'registry and module activation.')

Add-Check `
    -Name 'profile.failed-visual-preview-disabled-with-module-activation-denied' `
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
        -not [bool]$profile[0].boundedCaptionObservation.visualVerificationPassed -and
        -not [bool]$profile[0].boundedCaptionObservation.executionCapabilityEnabled -and
        [bool]$profile[0].boundedCaptionObservation.rollbackVerified -and
        -not $profile[0].activationPermitted -and
        $profile[0].liveExplorer -eq 'not-run') `
    -Detail (
        'The exact Win10 profile must keep read access but revoke another ' +
        'caption write after two no-change visual observations; module ' +
        'activation remains off.')

Add-Check `
    -Name 'receipt.fixed-toolchain-source-and-binary-binding' `
    -Passed (
        $buildReceiptScriptText.Contains('--no-incremental') -and
        $buildReceiptScriptText.Contains('--warnaserror') -and
        $buildReceiptScriptText.Contains('aggregateSha256') -and
        $buildReceiptScriptText.Contains('sourceEvidence') -and
        $buildReceiptScriptText.Contains('Get-FileHash') -and
        $buildReceiptScriptText.Contains(
            'jarvis-win10-explorer-caption-session.dll') -and
        $buildReceiptScriptText.Contains(
            'visual-diff-failed-light-app-theme') -and
        $buildReceiptScriptText.Contains(
            'liveObservationEvidenceValid') -and
        $buildReceiptScriptText.Contains(
            'liveMutationRun = $false') -and
        $buildReceiptScriptText.Contains(
            'moduleActivationPermitted = $false') -and
        $buildReceiptScriptText.Contains(
            'mutationPerformed = $false')) `
    -Detail (
        'The fixed-toolchain receipt must bind transitive source/config, the ' +
        'toolchain and all managed outputs while recording no live mutation.')

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
        'jarvis-win10-explorer-caption-session.dll')
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
        -Name 'model.fail-closed-policy-scenarios' `
        -Passed (
            $modelExitCode -eq 0 -and
            $null -ne $modelReceipt -and
            $modelReceipt.result -eq 'passed' -and
            $modelReceipt.scenarioCount -eq 9 -and
            $modelReceipt.passedCount -eq 9 -and
            -not $modelReceipt.moduleActivationPermitted -and
            -not $modelReceipt.mutationPerformed -and
            $modelReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Model exit $modelExitCode; scenarios " +
            "$($modelReceipt.passedCount)/$($modelReceipt.scenarioCount).")
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-explorer-caption-session-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
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
    liveMutationRun = $false
    moduleActivationPermitted = $false
    mutationPerformed = $false
    liveExplorer = 'not-run'
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
