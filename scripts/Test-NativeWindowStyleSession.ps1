[CmdletBinding()]
param(
    [switch]$StaticOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root (
    'src\Jarvis.NativeWindowStyleSession\' +
    'Jarvis.NativeWindowStyleSession.csproj')
$sourceRoot = Join-Path $root 'src\Jarvis.NativeWindowStyleSession'

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

$sourceText = @(
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.csproj') |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$transportText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'DwmWindowColorTransport.cs'))
$targetText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'NativeExplorerWindowTarget.cs'))
$controllerText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'NativeWindowStyleSessionController.cs'))

$forbiddenApiPattern = (
    '(?i)\b(?:OpenProcess|CreateRemoteThread|VirtualAllocEx|' +
    'WriteProcessMemory|ReadProcessMemory|SetWindowsHookEx|' +
    'TerminateProcess|SendMessage|PostMessage|SetWindowLong|' +
    'SetWindowPos|MoveWindow|ShowWindow|DestroyWindow|' +
    'System\.Diagnostics\.Process|ServiceController|' +
    'Microsoft\.Win32\.Registry|SetWindowCompositionAttribute|' +
    'DwmEnableBlurBehindWindow)\b'
)
Add-Check `
    -Name 'source.no-injection-process-message-or-system-mutation-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenApiPattern)) `
    -Detail (
        'No injection, process, message, geometry, service, registry or ' +
        'undocumented composition API may exist in the controller.'
    )

$allowedImports = @(
    'IsWindow',
    'IsWindowVisible',
    'GetClassNameW',
    'GetWindowTextW',
    'GetWindowThreadProcessId',
    'DwmSetWindowAttribute',
    'DwmFlush'
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
    -Name 'source.exact-user32-and-dwm-allowlist' `
    -Passed (
        $actualImports.Count -eq 7 -and
        @($actualImports | Where-Object { $_ -notin $allowedImports }).Count `
            -eq 0 -and
        @($allowedImports | Where-Object { $_ -notin $actualImports }).Count `
            -eq 0
    ) `
    -Detail (
        'Imports must be exactly five window-identity readers plus ' +
        "DwmSetWindowAttribute and DwmFlush. Actual: " +
        "$($actualImports -join ', ')."
    )

Add-Check `
    -Name 'dwm.only-visible-nonclient-colors' `
    -Passed (
        $transportText.Contains('BorderColor = 34') -and
        $transportText.Contains('CaptionColor = 35') -and
        $transportText.Contains('TextColor = 36') -and
        $transportText.Contains('ColorDefault = 0xFFFFFFFFU') -and
        -not [regex]::IsMatch(
            $transportText,
            '(?i)(?:Backdrop|Corner|DarkMode|ClientArea)')
    ) `
    -Detail (
        'The experiment may change only border, caption and caption-text ' +
        'COLORREF values and must expose the documented default reset.'
    )

Add-Check `
    -Name 'target.exact-temporary-explorer-window' `
    -Passed (
        $targetText.Contains('"CabinetWClass"') -and
        $targetText.Contains('Window title mismatch') -and
        $targetText.Contains('Window PID mismatch') -and
        $targetText.Contains('The exact Explorer window is not visible') -and
        $targetText.Contains('IsSameTarget(')
    ) `
    -Detail (
        'Every call must remain bound to one visible CabinetWClass HWND with ' +
        'the reviewed PID and full title.'
    )

Add-Check `
    -Name 'policy.new-window-baseline-and-explicit-confirmation' `
    -Passed (
        $sourceText.Contains('--baseline-system-default') -and
        $sourceText.Contains('--confirm-live-native-window-style') -and
        $sourceText.Contains(
            '--confirm-live-native-window-style-rollback') -and
        $sourceText.Contains('new-window-system-default-colors') -and
        $sourceText.Contains('MaximumTtlSeconds = 60')
    ) `
    -Detail (
        'Live use must acknowledge a newly opened system-default baseline, ' +
        'separate apply/reset confirmations and a 60-second maximum.'
    )

Add-Check `
    -Name 'session.journal-before-apply-finally-reset' `
    -Passed (
        (Test-MarkersInOrder `
            -Text $controllerText `
            -Markers @(
                'store.Prepare(journal);',
                'journal.ApplyAttempted = true;',
                'DwmWindowColorTransport.Apply('
            )) -and
        $controllerText.Contains(
            'apply.ColorMutationMayHaveOccurred') -and
        $controllerText.Contains('finally') -and
        $controllerText.Contains('ResetExactTarget(') -and
        $controllerText.Contains('ResetSystemDefault(')
    ) `
    -Detail (
        'The exact target and default-baseline contract must be durable ' +
        'before apply, partial color success must be recorded as a possible ' +
        'mutation, and every attempted apply must enter finally reset.'
    )

Add-Check `
    -Name 'receipt.denies-broad-shell-actions' `
    -Passed (
        $sourceText.Contains('InjectionRequested = false') -and
        $sourceText.Contains('ExplorerRestartRequested = false') -and
        $sourceText.Contains('ProcessTerminationRequested = false') -and
        $sourceText.Contains('RegistryMutationRequested = false')
    ) `
    -Detail (
        'Every live receipt must deny injection, Explorer restart, process ' +
        'termination and registry mutation.'
    )

$buildOutput = @(
    & dotnet build $projectPath --configuration Release --nologo --warnaserror `
        2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release-warning-free' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)

if ($buildExitCode -eq 0) {
    $modelOutput = @(
        & dotnet run `
            --project $projectPath `
            --configuration Release `
            --no-build `
            -- model-test 2>&1
    )
    $modelExitCode = $LASTEXITCODE
    $modelReceipt = $null
    try {
        $modelReceipt = (
            $modelOutput -join [Environment]::NewLine) |
            ConvertFrom-Json -Depth 20
    }
    catch {
        $modelReceipt = $null
    }
    Add-Check `
        -Name 'model.all-scenarios-pass-offline' `
        -Passed (
            $modelExitCode -eq 0 -and
            $null -ne $modelReceipt -and
            $modelReceipt.result -eq 'passed' -and
            $modelReceipt.scenarioCount -eq 11 -and
            $modelReceipt.passedCount -eq 11 -and
            -not $modelReceipt.mutationPerformed -and
            $modelReceipt.liveExplorer -eq 'not-run'
        ) `
        -Detail (
            "Model exit $modelExitCode; scenarios " +
            "$($modelReceipt.passedCount)/$($modelReceipt.scenarioCount)."
        )
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-native-window-style-session-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    liveMutationRun = $false
    activationPermitted = $false
    mutationPerformed = $false
    liveExplorer = 'not-run'
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 8

if (-not $passed) {
    exit 1
}
