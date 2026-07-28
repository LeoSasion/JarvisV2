[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath =
    Join-Path $root 'src\Jarvis.ExplorerSurfaceProbe\Jarvis.ExplorerSurfaceProbe.csproj'
$sourceRoot =
    Join-Path $root 'src\Jarvis.ExplorerSurfaceProbe'

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
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine

$forbiddenMutationPattern = (
    '(?i)\b(?:SendMessage|PostMessage|SetWindowLong|SetWindowPos|' +
    'MoveWindow|ShowWindow|DestroyWindow|CloseWindow|SetFocus|' +
    'OpenProcess|CreateRemoteThread|VirtualAllocEx|WriteProcessMemory|' +
    'SetWindowsHookEx|LoadLibrary|InitializeXamlDiagnosticsEx|' +
    'IVisualTreeService|AutomationPattern|InvokePattern|ValuePattern|' +
    'ExpandCollapsePattern|SelectionItemPattern|Process\.Start|' +
    '\.Kill\s*\(|CloseMainWindow|TerminateProcess|ServiceController|' +
    'Microsoft\.Win32\.Registry)\b'
)
Add-Check `
    -Name 'source.no-window-uia-process-or-system-mutation' `
    -Passed (-not [regex]::IsMatch(
        $sourceText,
        $forbiddenMutationPattern)) `
    -Detail (
        'The probe may read one exact HWND and its UIA raw tree but may not ' +
        'send messages, invoke patterns, launch/kill processes, hook, inject ' +
        'or mutate system state.'
    )

$allowedImports = @(
    'IsWindow',
    'IsWindowVisible',
    'GetClassNameW',
    'GetWindowTextLengthW',
    'GetWindowTextW',
    'GetWindowThreadProcessId',
    'GetShellWindow'
)
$observedImports = @(
    [regex]::Matches(
        $sourceText,
        '(?s)\[DllImport\((?<body>.*?)\)\]\s*' +
        '(?:\[return:.*?\]\s*)?' +
        'private static extern \w+\s+(?<name>\w+)') |
        ForEach-Object {
            $entryPoint = [regex]::Match(
                $_.Groups['body'].Value,
                'EntryPoint\s*=\s*"(?<entry>[^"]+)"')
            if ($entryPoint.Success) {
                $entryPoint.Groups['entry'].Value
            }
            else {
                $_.Groups['name'].Value
            }
        }
)
Add-Check `
    -Name 'source.readonly-user32-import-allowlist' `
    -Passed (
        $observedImports.Count -eq $allowedImports.Count -and
        @($observedImports |
            Where-Object { $_ -notin $allowedImports }).Count -eq 0 -and
        @($allowedImports |
            Where-Object { $_ -notin $observedImports }).Count -eq 0
    ) `
    -Detail (
        'The only native imports must validate the supplied HWND, class, ' +
        'title, PID/TID, visibility and desktop Shell identity. Observed: ' +
        "$($observedImports -join ', ')."
    )

Add-Check `
    -Name 'source.exact-target-no-enumeration' `
    -Passed (
        $sourceText.Contains('inspect-exact') -and
        $sourceText.Contains('--hwnd') -and
        $sourceText.Contains('--pid') -and
        $sourceText.Contains('--tid') -and
        $sourceText.Contains('--process-start-utc') -and
        $sourceText.Contains('--desktop-shell-pid') -and
        $sourceText.Contains('"C:\\",') -and
        $sourceText.Contains('actualProcessId != shellProcessId') -and
        $sourceText.Contains('windowClass == "CabinetWClass"') -and
        -not $sourceText.Contains('EnumWindows')
    ) `
    -Detail (
        'The probe must accept one already reviewed C:\ identity and reject ' +
        'the desktop Shell; it must not enumerate windows or select by guess.'
    )

Add-Check `
    -Name 'source.bounded-private-uia-topology' `
    -Passed (
        $sourceText.Contains('TreeWalker.RawViewWalker') -and
        $sourceText.Contains('MaximumNodes = 2048') -and
        $sourceText.Contains('MaximumDepth = 14') -and
        $sourceText.Contains('uia-topology-hint-not-xaml-proof') -and
        $sourceText.Contains('TopologySha256') -and
        -not $sourceText.Contains('current.Name') -and
        -not $sourceText.Contains('NameProperty') -and
        -not $sourceText.Contains('BoundingRectangle')
    ) `
    -Detail (
        'The UIA snapshot must be bounded, hashable and omit user-visible ' +
        'names, file paths and geometry; hints are never XAML proof.'
    )

Add-Check `
    -Name 'receipt.hard-readonly-boundary' `
    -Passed (
        $sourceText.Contains(
            'ReadyForXamlSelectorVerification: false') -and
        $sourceText.Contains('ReadyForPreview: false') -and
        $sourceText.Contains('ExecutionSupported: false') -and
        $sourceText.Contains('MutationSupported: false') -and
        $sourceText.Contains('ActivationPermitted: false') -and
        $sourceText.Contains('MutationPerformed: false') -and
        $sourceText.Contains(
            'LiveExplorer: "read-only-inspection"')
    ) `
    -Detail (
        'Every success or failure receipt must deny style execution, ' +
        'mutation, activation, XAML verification and preview readiness.'
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
    -Name 'build.release-warning-free-static-only' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-explorer-surface-probe-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    liveInspectionRun = $false
    executionSupported = $false
    mutationSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 8

if (-not $passed) {
    exit 1
}
