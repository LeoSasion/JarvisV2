[CmdletBinding()]
param(
    [switch]$StaticOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath =
    Join-Path $root 'src\common\Jarvis.DesktopStyleProbe\Jarvis.DesktopStyleProbe.csproj'
$sourceRoot = Join-Path $root 'src\common\Jarvis.DesktopStyleProbe'

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
    'MoveWindow|ShowWindow|DestroyWindow|OpenProcess|CreateRemoteThread|' +
    'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|TerminateProcess|' +
    'System\.Diagnostics\.Process|ServiceController|' +
    'Microsoft\.Win32\.Registry)\b'
)
Add-Check `
    -Name 'source.no-window-process-or-system-mutation-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenMutationPattern)) `
    -Detail (
        'The desktop probe may not send window messages, alter windows, ' +
        'inspect processes, install hooks, inject or mutate system state.'
    )

$allowedImports = @(
    'EnumWindows',
    'FindWindowExW',
    'GetClassNameW',
    'GetWindowThreadProcessId',
    'IsWindowVisible'
)
$unexpectedImports = @(
    [regex]::Matches(
        $sourceText,
        '(?s)\[DllImport\((?<body>.*?)\)\]\s*(?:\[return:.*?\]\s*)?' +
        'private static extern \w+\s+(?<name>\w+)') |
        ForEach-Object {
            $name = $_.Groups['name'].Value
            $entryPointMatch = [regex]::Match(
                $_.Groups['body'].Value,
                'EntryPoint\s*=\s*"(?<entry>[^"]+)"')
            if ($entryPointMatch.Success) {
                $entryPointMatch.Groups['entry'].Value
            }
            else {
                $name
            }
        } |
        Where-Object { $_ -notin $allowedImports }
)
Add-Check `
    -Name 'source.readonly-user32-allowlist' `
    -Passed (
        $unexpectedImports.Count -eq 0 -and
        [regex]::Matches($sourceText, '\[DllImport\(').Count -eq 5
    ) `
    -Detail (
        'Allowed imports are exactly EnumWindows, FindWindowExW, ' +
        'GetClassNameW, GetWindowThreadProcessId and IsWindowVisible. ' +
        "Unexpected: $($unexpectedImports -join ', ')."
    )

Add-Check `
    -Name 'receipt.hard-nonmutation-boundary' `
    -Passed (
        $sourceText.Contains('executionSupported = false') -and
        $sourceText.Contains('mutationSupported = false') -and
        $sourceText.Contains('activationPermitted = false') -and
        $sourceText.Contains('mutationPerformed = false') -and
        $sourceText.Contains(
            'liveExplorer = "read-only-inspection"')
    ) `
    -Detail (
        'Every probe receipt must deny execution, mutation and activation ' +
        'while labelling Explorer contact as read-only inspection.'
    )

$buildOutput = @(
    & dotnet build $projectPath --configuration Release --nologo 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)

if ($buildExitCode -eq 0 -and -not $StaticOnly) {
    $probeOutput = @(
        & dotnet run `
            --project $projectPath `
            --configuration Release `
            --no-build `
            -- inspect 2>&1
    )
    $probeExitCode = $LASTEXITCODE
    $probeReceipt = $null
    try {
        $probeReceipt =
            ($probeOutput -join [Environment]::NewLine) |
            ConvertFrom-Json -Depth 20
    }
    catch {
        $probeReceipt = $null
    }
    $explorerProcessIds = @(
        Get-Process -Name explorer -ErrorAction Stop |
            Select-Object -ExpandProperty Id
    )
    $exactCandidate = if (
        $null -ne $probeReceipt -and
        $probeReceipt.candidateCount -eq 1
    ) {
        @($probeReceipt.candidates)[0]
    }
    else {
        $null
    }
    Add-Check `
        -Name 'live.exact-desktop-host-readonly' `
        -Passed (
            $probeExitCode -eq 0 -and
            $null -ne $probeReceipt -and
            $probeReceipt.result -eq 'passed-read-only' -and
            $null -ne $exactCandidate -and
            $exactCandidate.TopLevelClass -in @('Progman', 'WorkerW') -and
            $exactCandidate.ProcessId -in $explorerProcessIds -and
            $exactCandidate.ThreadId -gt 0 -and
            -not $probeReceipt.executionSupported -and
            -not $probeReceipt.mutationSupported -and
            -not $probeReceipt.activationPermitted -and
            -not $probeReceipt.mutationPerformed -and
            $probeReceipt.liveExplorer -eq 'read-only-inspection'
        ) `
        -Detail (
            "Probe exit $probeExitCode; candidate count " +
            "$($probeReceipt.candidateCount); Explorer PIDs " +
            "$($explorerProcessIds -join ',')."
        )
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-desktop-style-probe-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    executionSupported = $false
    mutationSupported = $false
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
