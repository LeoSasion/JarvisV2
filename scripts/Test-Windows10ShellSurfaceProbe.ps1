[CmdletBinding()]
param(
    [switch]$StaticOnly,
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ShellSurfaceProbe')
$admissionRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.HostAdmission')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.ShellSurfaceProbe.csproj')
$readerPath = Join-Path $sourceRoot 'NativeWindowTopologyReader.cs'
$inspectorPath = Join-Path $sourceRoot 'ShellSurfaceInspector.cs'
$contractsPath = Join-Path $sourceRoot 'ProbeContracts.cs'

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
    foreach ($currentSourceRoot in @($sourceRoot, $admissionRoot)) {
        Get-ChildItem -LiteralPath $currentSourceRoot -File -Recurse |
            Where-Object Extension -In @('.cs', '.csproj') |
            Sort-Object FullName |
            ForEach-Object {
                [IO.File]::ReadAllText($_.FullName)
            }
    }
) -join [Environment]::NewLine
$readerText = [IO.File]::ReadAllText($readerPath)
$inspectorText = [IO.File]::ReadAllText($inspectorPath)
$contractsText = [IO.File]::ReadAllText($contractsPath)

$forbiddenMutationPattern = (
    '(?i)\b(?:SendMessage|PostMessage|SetWindowLong|SetWindowPos|' +
    'MoveWindow|ShowWindow|DestroyWindow|OpenProcess|CreateRemoteThread|' +
    'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|TerminateProcess|' +
    'Process\.Kill|CloseMainWindow|ServiceController|' +
    'RegistryKey\.SetValue|Registry\.SetValue|RegistryKey\.Delete|' +
    'Start-Process|Stop-Process|DwmSetWindowAttribute)\b'
)
Add-Check `
    -Name 'source.no-window-process-or-system-mutation' `
    -Passed (-not [regex]::IsMatch(
        $sourceText,
        $forbiddenMutationPattern)) `
    -Detail (
        'The probe may enumerate bounded window topology and exact Explorer ' +
        'process identities but may not message, style, hook, inject, ' +
        'terminate, launch or modify system state.')

$allowedImports = @(
    'EnumWindows',
    'FindWindowExW',
    'GetClassNameW',
    'GetWindowThreadProcessId',
    'GetShellWindow',
    'IsWindowVisible',
    'GetWindowRect'
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
            Where-Object { $_ -notin $observedImports }).Count -eq 0) `
    -Detail (
        'Only top-level/direct-child enumeration, class, PID/TID, shell ' +
        'identity, visibility and rectangle reads are allowed. Observed: ' +
        "$($observedImports -join ', ').")

Add-Check `
    -Name 'source.no-window-text-or-user-content' `
    -Passed (
        -not $sourceText.Contains('GetWindowText') -and
        -not $sourceText.Contains('AutomationElement') -and
        -not $sourceText.Contains('NameProperty') -and
        -not $contractsText.Contains('WindowTitle') -and
        $contractsText.Contains('bool WindowTextCollected')) `
    -Detail (
        'The structural inventory must omit window titles, folder paths, ' +
        'UI Automation names and other user-visible content.')

Add-Check `
    -Name 'source.bounded-hashable-topology' `
    -Passed (
        $readerText.Contains('MaximumNodes = 1024') -and
        $readerText.Contains('MaximumDepth = 8') -and
        $readerText.Contains('ComputeTopologyHash') -and
        $readerText.Contains('SHA256.HashData') -and
        $contractsText.Contains('string TopologySha256') -and
        $contractsText.Contains('bool Truncated')) `
    -Detail (
        'Every surface tree must have fixed node/depth limits and a ' +
        'deterministic SHA-256 topology receipt.')

Add-Check `
    -Name 'source.win10-surface-selection' `
    -Passed (
        $inspectorText.Contains('"Progman"') -and
        $inspectorText.Contains('"WorkerW"') -and
        $inspectorText.Contains('"SHELLDLL_DefView"') -and
        $inspectorText.Contains('"SysListView32"') -and
        $inspectorText.Contains('"CabinetWClass"') -and
        $inspectorText.Contains('"Shell_TrayWnd"') -and
        $inspectorText.Contains('"Shell_SecondaryTrayWnd"') -and
        $inspectorText.Contains(
            '.RootProcessId == shellProcessId') -and
        $inspectorText.Contains(
            '"read-shell-window-topology"')) `
    -Detail (
        'Desktop, Explorer and classic taskbar candidates must be selected ' +
        'by Win10 classes and tied back to the exact desktop Shell PID.')

Add-Check `
    -Name 'receipt.hard-readonly-boundary' `
    -Passed (
        $sourceText.Contains('WindowTextCollected') -and
        $sourceText.Contains('ExecutionSupported') -and
        $sourceText.Contains('MutationSupported') -and
        $sourceText.Contains('ActivationPermitted') -and
        $sourceText.Contains('MutationPerformed') -and
        $sourceText.Contains('"read-only-inspection"') -and
        $sourceText.Contains('false,')) `
    -Detail (
        'Every receipt must deny text collection, execution, mutation and ' +
        'activation while labeling Explorer contact as read-only inspection.')

Add-Check `
    -Name 'project.shared-exact-host-admission' `
    -Passed (
        $sourceText.Contains(
            'Jarvis.Win10.HostAdmission.csproj') -and
        $sourceText.Contains(
            'ExactWindows10HostInspector.Inspect()') -and
        $sourceText.Contains(
            'win10-exact-host-admission')) `
    -Detail (
        'Both Win10 probes must consume the same embedded exact-host ' +
        'admission library instead of duplicating fingerprint logic.')

$buildOutput = @(
    & $DotnetPath build `
        $projectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 10) -join
        [Environment]::NewLine)

if ($buildExitCode -eq 0 -and -not $StaticOnly) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-shell-surface-probe.dll')
    $stateRoot = Join-Path $env:LOCALAPPDATA 'JARVIS2'
    $killSwitchPath = Join-Path $stateRoot 'disabled.flag'
    $permitPath = Join-Path $stateRoot 'active-module.txt'
    $killSwitchBefore =
        Test-Path -LiteralPath $killSwitchPath -PathType Leaf
    $permitBefore =
        Test-Path -LiteralPath $permitPath -PathType Leaf
    $explorerBefore = @(
        Get-Process -Name explorer -ErrorAction Stop |
            ForEach-Object {
                "$($_.Id):$($_.StartTime.ToUniversalTime().Ticks)"
            } |
            Sort-Object
    )

    $probeOutput = @(
        & $DotnetPath $assemblyPath inspect 2>&1
    )
    $probeExitCode = $LASTEXITCODE
    $receipt = $null
    try {
        $receipt =
            ($probeOutput -join [Environment]::NewLine) |
                ConvertFrom-Json -Depth 80
    }
    catch {
        $receipt = $null
    }

    $desktop = if (
        $null -ne $receipt -and
        @($receipt.inventory.desktopSurfaces).Count -eq 1
    ) {
        @($receipt.inventory.desktopSurfaces)[0]
    }
    else {
        $null
    }
    $explorerWindow = if (
        $null -ne $receipt -and
        @($receipt.inventory.explorerWindows).Count -ge 1
    ) {
        @($receipt.inventory.explorerWindows)[0]
    }
    else {
        $null
    }
    $taskbar = if (
        $null -ne $receipt -and
        @($receipt.inventory.primaryTaskbars).Count -eq 1
    ) {
        @($receipt.inventory.primaryTaskbars)[0]
    }
    else {
        $null
    }

    Add-Check `
        -Name 'live.complete-readonly-surface-inventory' `
        -Passed (
            $probeExitCode -eq 0 -and
            $null -ne $receipt -and
            $receipt.result -eq 'passed-read-only-inventory' -and
            $receipt.admission.profile.profileId -eq
                'win10-22h2-19045.6466-x64' -and
            $receipt.inventory.exactDesktopHostObserved -and
            $receipt.inventory.exactPrimaryTaskbarObserved -and
            $receipt.inventory.explorerWindowObserved -and
            $receipt.inventory.completeSurfaceSetObserved -and
            $null -ne $desktop -and
            $null -ne $explorerWindow -and
            $null -ne $taskbar -and
            $desktop.classHistogram.SHELLDLL_DefView -eq 1 -and
            $desktop.classHistogram.SysListView32 -eq 1 -and
            $explorerWindow.classHistogram.DirectUIHWND -ge 1 -and
            $explorerWindow.classHistogram.SHELLDLL_DefView -ge 1 -and
            $taskbar.classHistogram.MSTaskListWClass -eq 1 -and
            $taskbar.classHistogram.TrayNotifyWnd -eq 1 -and
            -not $receipt.windowTextCollected -and
            -not $receipt.executionSupported -and
            -not $receipt.mutationSupported -and
            -not $receipt.activationPermitted -and
            -not $receipt.mutationPerformed -and
            $receipt.liveExplorer -eq 'read-only-inspection' -and
            @($receipt.failures).Count -eq 0) `
        -Detail (
            "Probe exit $probeExitCode; result $($receipt.result); desktop " +
            "$(@($receipt.inventory.desktopSurfaces).Count); Explorer " +
            "$(@($receipt.inventory.explorerWindows).Count); taskbar " +
            "$(@($receipt.inventory.primaryTaskbars).Count).")

    $explorerAfter = @(
        Get-Process -Name explorer -ErrorAction Stop |
            ForEach-Object {
                "$($_.Id):$($_.StartTime.ToUniversalTime().Ticks)"
            } |
            Sort-Object
    )
    $killSwitchAfter =
        Test-Path -LiteralPath $killSwitchPath -PathType Leaf
    $permitAfter =
        Test-Path -LiteralPath $permitPath -PathType Leaf
    Add-Check `
        -Name 'live.shell-and-safety-state-unchanged' `
        -Passed (
            (($explorerBefore -join '|') -eq
                ($explorerAfter -join '|')) -and
            $killSwitchBefore -and
            $killSwitchAfter -and
            -not $permitBefore -and
            -not $permitAfter) `
        -Detail (
            'Explorer PID/start-time identity must be unchanged, the kill ' +
            'switch must remain armed and the one-shot permit must remain absent.')
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-shell-surface-probe-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    windowTextCollected = $false
    executionSupported = $false
    mutationSupported = $false
    activationPermitted = $false
    liveExplorer = if ($StaticOnly) {
        'not-run'
    }
    else {
        'read-only-inspection'
    }
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
