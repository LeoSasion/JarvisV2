[CmdletBinding()]
param(
    [switch]$StaticOnly,
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root (
    'src\common\Jarvis.DesktopStyleSession\Jarvis.DesktopStyleSession.csproj')
$sourceRoot = Join-Path $root 'src\common\Jarvis.DesktopStyleSession'

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
    (Join-Path $sourceRoot 'DesktopStyleSessionController.cs'))
$transportText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'DesktopListViewTransport.cs'))

$forbiddenApiPattern = (
    '(?i)\b(?:PostMessage|SetWindowLong|SetWindowPos|MoveWindow|' +
    'ShowWindow|DestroyWindow|OpenProcess|CreateRemoteThread|' +
    'VirtualAllocEx|WriteProcessMemory|ReadProcessMemory|' +
    'SetWindowsHookEx|TerminateProcess|ExitWindowsEx|' +
    'InitiateSystemShutdown|System\.Diagnostics\.Process|' +
    'ServiceController|Microsoft\.Win32\.Registry|' +
    'DwmSetWindowAttribute|SystemParametersInfo)\b'
)
Add-Check `
    -Name 'source.no-process-shell-service-registry-mutation-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenApiPattern)) `
    -Detail (
        'No process injection, hook, service, registry, Explorer lifecycle, ' +
        'DWM or system-parameter API may exist in the controller.'
    )

$forbiddenListViewPattern = (
    '(?i)\b(?:LVM_SETBKCOLOR|LVM_SETTEXTBKCOLOR|LVM_SETICONSPACING|' +
    'LVM_SETITEMPOSITION|LVM_ARRANGE|LVM_SETEXTENDEDLISTVIEWSTYLE)\b'
)
Add-Check `
    -Name 'source.text-color-only-listview-contract' `
    -Passed (
        -not [regex]::IsMatch($sourceText, $forbiddenListViewPattern) -and
        $transportText.Contains(
            'ListViewGetTextColor = ListViewFirst + 35') -and
        $transportText.Contains(
            'ListViewSetTextColor = ListViewFirst + 36')
    ) `
    -Detail (
        'The only ListView properties in scope are GETTEXTCOLOR and ' +
        'SETTEXTCOLOR; backgrounds, layout and item positions are excluded.'
    )

$allowedImports = @(
    'EnumWindows',
    'FindWindowExW',
    'GetClassNameW',
    'GetWindowThreadProcessId',
    'IsWindowVisible',
    'SendMessageTimeoutW',
    'RedrawWindow'
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
$unexpectedImports = @(
    $actualImports | Where-Object { $_ -notin $allowedImports }
)
Add-Check `
    -Name 'source.exact-user32-allowlist' `
    -Passed (
        $unexpectedImports.Count -eq 0 -and
        $actualImports.Count -eq 7 -and
        @($allowedImports | Where-Object { $_ -notin $actualImports }).Count `
            -eq 0
    ) `
    -Detail (
        'Imports must be exactly the five host-discovery APIs plus ' +
        'SendMessageTimeoutW and exact-target RedrawWindow. Actual: ' +
        "$($actualImports -join ', ')."
    )

Add-Check `
    -Name 'transport.fixed-bounded-timeout' `
    -Passed (
        $transportText.Contains(
            'MessageTimeoutMilliseconds = 250') -and
        $transportText.Contains(
            'SendMessageTimeoutBlock = 0x0001') -and
        $transportText.Contains(
            'SendMessageTimeoutAbortIfHung = 0x0002') -and
        $transportText.Contains(
            'SendMessageTimeoutErrorOnExit = 0x0020') -and
        $transportText.Contains('nuint.Zero') -and
        -not $transportText.Contains('unsafe')
    ) `
    -Detail (
        'Cross-process ListView messages carry scalar values only and use a ' +
        '250 ms BLOCK + ABORTIFHUNG + ERRORONEXIT timeout.'
    )

Add-Check `
    -Name 'transport.exact-target-redraw-only' `
    -Passed (
        $transportText.Contains('RedrawExactFolderView(') -and
        $transportText.Contains('RedrawInvalidate |') -and
        $transportText.Contains('RedrawErase |') -and
        $transportText.Contains('RedrawAllChildren |') -and
        $transportText.Contains('RedrawUpdateNow;') -and
        -not $transportText.Contains('HWND_BROADCAST') -and
        -not $transportText.Contains('InvalidateRect')
    ) `
    -Detail (
        'A visual refresh may target only the already-admitted FolderView ' +
        'HWND and must synchronously invalidate and redraw that exact tree.'
    )

Add-Check `
    -Name 'session.original-persisted-before-set' `
    -Passed (
        Test-MarkersInOrder `
            -Text $controllerText `
            -Markers @(
                'store.Prepare(journal);',
                'journal.ApplyAttempted = true;',
                'DesktopListViewTransport.SetTextColor('
            )
    ) `
    -Detail (
        'The original COLORREF and exact HWND identity must be durably ' +
        'journaled before the first SET attempt.'
    )

Add-Check `
    -Name 'session.finally-rollback-and-verify' `
    -Passed (
        $controllerText.Contains('finally') -and
        $controllerText.Contains('RollBackExactTarget(') -and
        $controllerText.Contains(
            'restoredColor == journal.OriginalColorRef') -and
        $controllerText.Contains('Desktop target identity changed before SET')
    ) `
    -Detail (
        'TTL, Ctrl+C and exceptions must converge on an exact-target rollback ' +
        'whose restored value is read back and verified.'
    )

Add-Check `
    -Name 'policy.explicit-confirmation-and-sixty-second-cap' `
    -Passed (
        $sourceText.Contains(
            '--confirm-live-desktop-text-color') -and
        $sourceText.Contains(
            '--confirm-live-desktop-text-color-rollback') -and
        $sourceText.Contains('MaximumTtlSeconds = 60') -and
        $sourceText.Contains('MinimumTtlSeconds = 10')
    ) `
    -Detail (
        'Apply and rollback have separate exact confirmations and previews ' +
        'are hard bounded to 10-60 seconds.'
    )

Add-Check `
    -Name 'receipt.path-confined-and-denies-broad-activation' `
    -Passed (
        $sourceText.Contains('ResolveJournalSessionPath(') -and
        $sourceText.Contains(
            'string sessionPath = ResolveJournalSessionPath(journal);') -and
        $sourceText.Contains(
            'string activePath = ResolveSessionPath(ActiveSessionPath);') -and
        $sourceText.Contains('ActivationPermitted = false') -and
        $sourceText.Contains('ExplorerRestartRequested = false') -and
        $sourceText.Contains('ProcessTerminationRequested = false') -and
        $sourceText.Contains('RegistryMutationRequested = false')
    ) `
    -Detail (
        'Every journal write is rebound to its canonical run path, and every ' +
        'live session denies module activation, Explorer restart, process ' +
        'termination and registry mutation.'
    )

$buildOutput = @(
    & $DotnetPath build $projectPath --configuration Release --nologo --warnaserror `
        2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release-warning-free' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)

if ($buildExitCode -eq 0) {
    $modelOutput = @(
        & $DotnetPath run `
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
            ConvertFrom-Json
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
            $modelReceipt.scenarioCount -eq 12 -and
            $modelReceipt.passedCount -eq 12 -and
            -not $modelReceipt.activationPermitted -and
            -not $modelReceipt.mutationPerformed -and
            $modelReceipt.liveExplorer -eq 'not-run'
        ) `
        -Detail (
            "Model exit $modelExitCode; scenarios " +
            "$($modelReceipt.passedCount)/$($modelReceipt.scenarioCount)."
        )
}

if ($buildExitCode -eq 0 -and -not $StaticOnly) {
    $explorerProcessIds = @(
        Get-Process -Name explorer -ErrorAction Stop |
            Select-Object -ExpandProperty Id
    )
    if ($explorerProcessIds.Count -ne 1) {
        Add-Check `
            -Name 'live.readonly-text-color-inspection' `
            -Passed $false `
            -Detail (
                'Exactly one Explorer process is required for the read-only ' +
                "inspection; observed $($explorerProcessIds.Count)."
            )
    }
    else {
        $inspectOutput = @(
            & $DotnetPath run `
                --project $projectPath `
                --configuration Release `
                --no-build `
                -- inspect `
                --expected-explorer-pid $explorerProcessIds[0] 2>&1
        )
        $inspectExitCode = $LASTEXITCODE
        $inspectReceipt = $null
        try {
            $inspectReceipt = (
                $inspectOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
        }
        catch {
            $inspectReceipt = $null
        }
        Add-Check `
            -Name 'live.readonly-text-color-inspection' `
            -Passed (
                $inspectExitCode -eq 0 -and
                $null -ne $inspectReceipt -and
                $inspectReceipt.result -eq 'passed-read-only' -and
                $inspectReceipt.target.processId -eq $explorerProcessIds[0] -and
                $inspectReceipt.transport.message -eq 'LVM_GETTEXTCOLOR' -and
                $inspectReceipt.transport.timeoutMilliseconds -eq 250 -and
                $inspectReceipt.transport.scalarOnly -and
                -not $inspectReceipt.mutationSupported -and
                -not $inspectReceipt.activationPermitted -and
                -not $inspectReceipt.mutationPerformed -and
                $inspectReceipt.liveExplorer -eq 'read-only-inspection'
            ) `
            -Detail (
                "Inspection exit $inspectExitCode; Explorer PID " +
                "$($explorerProcessIds[0]); result " +
                "$($inspectReceipt.result)."
            )
    }
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-desktop-style-session-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    liveMutationRun = $false
    activationPermitted = $false
    mutationPerformed = $false
    liveExplorer = if ($StaticOnly) {
        'not-run'
    }
    else {
        'read-only-inspection'
    }
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 8

if (-not $passed) {
    exit 1
}
