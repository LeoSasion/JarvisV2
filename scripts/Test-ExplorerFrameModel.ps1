[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerFrameModel\Jarvis.ExplorerFrameModel.csproj'
$sourceRoot =
    Join-Path $root 'src\platforms\windows11\Jarvis.ExplorerFrameModel'
$taskPath =
    Join-Path $root 'docs\PHASE-9-EXPLORER-FRAME-STYLER-TASK.md'

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

$sourceText = @(
    Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File |
        Sort-Object Name |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$taskText = [IO.File]::ReadAllText($taskPath)

$forbiddenRuntimePattern = (
    '(?i)\b(?:DllImport|LibraryImport|ComImport|Marshal\.|' +
    'InitializeXamlDiagnosticsEx|IXamlDiagnostics|IVisualTreeService|' +
    'OpenProcess|CreateRemoteThread|VirtualAllocEx|WriteProcessMemory|' +
    'SetWindowsHookEx|UnhookWindowsHookEx|LoadLibrary|GetProcAddress|' +
    'NtQueueApcThread|StartService|ServiceController|' +
    'Microsoft\.Win32\.Registry|System\.Diagnostics\.Process|' +
    'TerminateProcess|SetPropertyValue)\b'
)
Add-Check `
    -Name 'source.no-live-loader-xaml-or-process-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenRuntimePattern)) `
    -Detail (
        'The offline frame model must contain no loader, XAML Diagnostics, ' +
        'COM activation, process, service, registry, hook, remote-memory or ' +
        'P/Invoke API.'
    )

Add-Check `
    -Name 'source.fixture-only-entrypoint-and-receipt' `
    -Passed (
        $sourceText.Contains('model-test') -and
        $sourceText.Contains('ExecutionSupported: false') -and
        $sourceText.Contains('ActivationPermitted: false') -and
        $sourceText.Contains('LiveExplorer: "not-run"') -and
        $sourceText.Contains('MutationPerformed: false') -and
        -not $sourceText.Contains('evaluate-live') -and
        -not $sourceText.Contains('apply-live')
    ) `
    -Detail (
        'The only executable operation must be the in-memory model test and ' +
        'every receipt must deny execution, activation and live mutation.'
    )

Add-Check `
    -Name 'contract.exact-surfaces-and-property-allowlist' `
    -Passed (
        $sourceText.Contains('public const string TabStrip = "tab-strip"') -and
        $sourceText.Contains('public const string CommandBar = "command-bar"') -and
        $sourceText.Contains(
            'public const string NavigationPane = "navigation-pane"') -and
        $sourceText.Contains('public const string Background = "Background"') -and
        $sourceText.Contains('public const string Foreground = "Foreground"') -and
        $sourceText.Contains(
            'public const string BorderBrush = "BorderBrush"') -and
        $sourceText.Contains(
            'offline-fixture-candidate-pending-live-discovery')
    ) `
    -Detail (
        'The contract must name exactly the three intended frame surfaces, ' +
        'the three allowed properties, and unverified fixture selectors.'
    )

Add-Check `
    -Name 'contract.snapshot-before-apply-and-reverse-restore' `
    -Passed (
        $sourceText.Contains('FrameTransactionState.Prepared') -and
        $sourceText.Contains('new PropertySnapshot(') -and
        $sourceText.Contains('int last = _applied.Count - 1;') -and
        $sourceText.Contains('FrameTransactionState.RestoreRequired') -and
        $sourceText.Contains('FrameTransactionState.Restored') -and
        $sourceText.Contains('visual-tree-generation-drift')
    ) `
    -Detail (
        'The model must snapshot originals before apply, recover in reverse ' +
        'order, and distinguish unresolved recovery from restored state.'
    )

Add-Check `
    -Name 'docs.live-connection-remains-unauthorized' `
    -Passed (
        $taskText.Contains(
            'OFFLINE MODEL COMPLETE — LIVE XAML CONNECTION NOT AUTHORIZED') -and
        $taskText.Contains('No live XAML connection exists.') -and
        $taskText.Contains('InitializeXamlDiagnosticsEx') -and
        $taskText.Contains('explicit rollback timer')
    ) `
    -Detail (
        'Phase 9 documentation must state the offline result, GPL boundary, ' +
        'missing live connection and next read-only gate.'
    )

$buildOutput = @(
    & dotnet build `
        $projectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release-warning-free' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)

$modelReceipt = $null
$modelExitCode = $null
if ($buildExitCode -eq 0) {
    $modelOutput = @(
        & dotnet run `
            --project $projectPath `
            --configuration Release `
            --no-build `
            -- model-test 2>&1
    )
    $modelExitCode = $LASTEXITCODE
    try {
        $modelReceipt =
            ($modelOutput -join [Environment]::NewLine) |
            ConvertFrom-Json -Depth 30
    }
    catch {
        $modelReceipt = $null
    }
}

$modelMatrixPassed =
    $modelExitCode -eq 0 -and
    $null -ne $modelReceipt -and
    $modelReceipt.result -eq 'passed' -and
    $modelReceipt.scenarioCount -eq 29 -and
    $modelReceipt.passedCount -eq 29 -and
    -not $modelReceipt.executionSupported -and
    -not $modelReceipt.activationPermitted -and
    $modelReceipt.liveExplorer -eq 'not-run' -and
    -not $modelReceipt.mutationPerformed
$modelDetail = if ($null -eq $modelReceipt) {
    "Model receipt unavailable; exit code $modelExitCode."
}
else {
    "Model exit $modelExitCode; passed " +
        "$($modelReceipt.passedCount)/$($modelReceipt.scenarioCount)."
}
Add-Check `
    -Name 'model.deterministic-fault-matrix' `
    -Passed $modelMatrixPassed `
    -Detail $modelDetail

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-frame-model-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    executionSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 8

if (-not $passed) {
    exit 1
}
