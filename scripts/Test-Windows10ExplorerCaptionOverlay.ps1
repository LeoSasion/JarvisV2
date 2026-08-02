[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.ExplorerCaptionOverlay')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.ExplorerCaptionOverlay.csproj')
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
        Where-Object Extension -In @('.cs', '.csproj', '.xaml') |
        Sort-Object FullName
)
$sourceText = @(
    $sourceFiles | ForEach-Object {
        [IO.File]::ReadAllText($_.FullName)
    }
) -join [Environment]::NewLine
$programText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'Program.cs'))
$windowText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'OverlayWindow.xaml.cs'))
$xamlText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'OverlayWindow.xaml'))
$nativeText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'NativeOverlayTarget.cs'))
$profiles =
    Get-Content -LiteralPath $profilePath -Raw |
        ConvertFrom-Json
$profile = @($profiles.profiles)

Add-Check `
    -Name 'project.read-gate-only-dependency' `
    -Passed (
        $sourceText.Contains('<OutputType>WinExe</OutputType>') -and
        $sourceText.Contains(
            'Jarvis.Win10.ExplorerCaptionPlan.csproj') -and
        $sourceText.Contains(
            'ExplorerCaptionGate.Inspect(expectedWindowHandle)') -and
        $sourceText.Contains('OverlayPolicy.RequiredCapability')) `
    -Detail (
        'The owned WPF overlay must use a non-console WinExe host, reuse ' +
        'the exact read gate and require its separate own-process ' +
        'capability.')

Add-Check `
    -Name 'gate.separate-explorer-process-readonly-compatible' `
    -Passed (
        $sourceText.Contains(
            'explorer-root-pid-not-desktop-shell') -and
        $sourceText.Contains('OnlySeparateProcessFailure') -and
        $sourceText.Contains('TargetInObservedExplorerProcessSet') -and
        $sourceText.Contains('AcceptedCaptionGateFailures')) `
    -Detail (
        'The owned overlay may accept only the exact gate''s single ' +
        'desktop-Shell PID mismatch when the target PID is still in the ' +
        'read-only observed Explorer process set; every other gate failure ' +
        'remains blocking.')

$forbiddenPattern = (
    '(?i)\b(?:DwmSetWindowAttribute|SetWindowCompositionAttribute|' +
    'SendMessage|PostMessage|OpenProcess|CreateRemoteThread|VirtualAllocEx|' +
    'WriteProcessMemory|ReadProcessMemory|SetWindowsHookEx|SetWindowPos|' +
    'MoveWindow|ShowWindow|DestroyWindow|TerminateProcess|Process\.Kill|' +
    'CloseMainWindow|RegistryKey|Registry\.|ServiceController|' +
    'explorer\.exe|windhawk\.exe)\b'
)
Add-Check `
    -Name 'source.no-explorer-write-injection-or-system-mutation' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenPattern)) `
    -Detail (
        'The overlay may read target identity and mutate only its own WPF ' +
        'HWND; Explorer writes, injection, lifecycle and registry APIs are ' +
        'forbidden.')

$allowedImports = @(
    'IsWindow',
    'GetWindowRect',
    'GetWindowThreadProcessId',
    'GetForegroundWindow',
    'GetDpiForWindow',
    'GetClassNameW',
    'GetWindowLongPtrW',
    'SetWindowLongPtrW'
)
$importMatches = @(
    [regex]::Matches(
        $sourceText,
        '(?s)\[DllImport\((?<body>.*?)\)\]\s*' +
        '(?:\[return:.*?\]\s*)?(?:private|internal) static extern ' +
        '\w+\s+(?<name>\w+)')
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
        }).Count -eq 0) `
    -Detail (
        'Native imports must be exact target readers plus own-HWND extended ' +
        'style access. Actual: ' + ($actualImports -join ', '))

Add-Check `
    -Name 'window.owned-click-through-no-activate' `
    -Passed (
        $xamlText.Contains('AllowsTransparency="True"') -and
        $xamlText.Contains('ShowActivated="False"') -and
        $xamlText.Contains('ShowInTaskbar="False"') -and
        $xamlText.Contains('WindowStyle="None"') -and
        $windowText.Contains('ExtendedStyleTransparent') -and
        $windowText.Contains('ExtendedStyleNoActivate') -and
        $windowText.Contains('TransparentHitTest') -and
        @([regex]::Matches(
            $windowText,
            'Marshal\.SetLastPInvokeError\(0\)')).Count -eq 2 -and
        $windowText.Contains('Marshal.GetLastPInvokeError()') -and
        $windowText.Contains('SetWindowLongPtr(') -and
        $windowText.Contains('source.Handle')) `
    -Detail (
        'The overlay HWND must be transparent, no-activate, absent from the ' +
        'taskbar, and return HTTRANSPARENT while styling only source.Handle.')

Add-Check `
    -Name 'visual.neural-void-vector-glow-native-controls-preserved' `
    -Passed (
        $xamlText.Contains('BlurEffect Radius="4"') -and
        @([regex]::Matches($xamlText, '<Path')).Count -ge 3 -and
        -not $xamlText.Contains('LinearGradientBrush') -and
        -not $xamlText.Contains('Segoe MDL2 Assets') -and
        -not [regex]::IsMatch(
            $xamlText,
            '(?i)<Image|ImageBrush|\.png|\.jpg|\.jpeg|\.webp|\.gif') -and
        $windowText.Contains(
            'NativeCaptionControlReserveDips = 138') -and
        $windowText.Contains('CalculateOverlayWidthDips(') -and
        $windowText.Contains('RemainingTimeText.Text') -and
        $programText.Contains(
            '"neural-void-explorer-caption-canary-v2"')) `
    -Detail (
        'The canary must use authored vector geometry plus a bounded blur ' +
        'pass, no bitmap or Unicode icon assets, expose remaining TTL, and ' +
        'leave the native 138-DIP caption-control cluster uncovered.')

Add-Check `
    -Name 'session.foreground-tracked-ttl-bounded' `
    -Passed (
        $sourceText.Contains('MinimumTtlSeconds = 10') -and
        $sourceText.Contains('MaximumTtlSeconds = 60') -and
        $sourceText.Contains(
            '--confirm-owned-explorer-caption-overlay-preview') -and
        $windowText.Contains('snapshot.IsForeground') -and
        $windowText.Contains('Visibility = Visibility.Hidden') -and
        $windowText.Contains('TargetRetired = true') -and
        $windowText.Contains('DateTimeOffset now = DateTimeOffset.UtcNow') -and
        $windowText.Contains('now >= expiresAtUtc')) `
    -Detail (
        'The overlay must require explicit confirmation, hide when the exact ' +
        'Explorer is not foreground, close on retirement, and expire in ' +
        '10-60 seconds.')

Add-Check `
    -Name 'receipt.denies-explorer-mutation-and-module-activation' `
    -Passed (
        $sourceText.Contains('OwnedWindowOnly') -and
        $sourceText.Contains('MouseTransparent') -and
        $sourceText.Contains('NoActivate') -and
        $sourceText.Contains('VisualContractId') -and
        $sourceText.Contains('NativeCaptionControlsUnobscured') -and
        $sourceText.Contains('VectorCorePreserved') -and
        $sourceText.Contains('BoundedGlowPostProcess') -and
        $sourceText.Contains('BitmapAssetsUsed') -and
        $sourceText.Contains('ExplorerMutationPerformed') -and
        $sourceText.Contains('InjectionRequested') -and
        $sourceText.Contains('ExplorerRestartRequested') -and
        $sourceText.Contains('RegistryMutationRequested') -and
        $sourceText.Contains('ModuleActivationPermitted')) `
    -Detail (
        'Every session receipt must identify the own-process boundary and ' +
        'deny Explorer mutation, injection, restart, registry and modules.')

Add-Check `
    -Name 'profile.overlay-enabled-caption-write-disabled' `
    -Passed (
        $profile.Count -eq 1 -and
        $profile[0].status -eq
            'observed-caption-write-disabled-owned-overlay-visually-verified' -and
        @($profile[0].allowedCapabilities) -contains
            'run-bounded-owned-explorer-caption-overlay-preview' -and
        @($profile[0].allowedCapabilities) -notcontains
            'run-bounded-single-explorer-dark-caption-preview' -and
        -not $profile[0].activationPermitted -and
        $profile[0].liveExplorer -eq 'not-run') `
    -Detail (
        'The profile may grant the own-process overlay while the failed DWM ' +
        'caption writer and module activation remain disabled.')

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
        'jarvis-win10-explorer-caption-overlay.dll')
    $modelOutput = @(& $DotnetPath $assemblyPath model-test 2>&1)
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
            $modelReceipt.scenarioCount -eq 13 -and
            $modelReceipt.passedCount -eq 13 -and
            -not $modelReceipt.explorerMutationPerformed -and
            -not $modelReceipt.moduleActivationPermitted) `
        -Detail (
            "Model exit $modelExitCode; scenarios " +
            "$($modelReceipt.passedCount)/$($modelReceipt.scenarioCount).")
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-explorer-caption-overlay-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scenarioCount = if ($null -eq $modelReceipt) { 0 } else {
        $modelReceipt.scenarioCount
    }
    scenarioPassedCount = if ($null -eq $modelReceipt) { 0 } else {
        $modelReceipt.passedCount
    }
    liveMutationRun = $false
    explorerMutationPerformed = $false
    moduleActivationPermitted = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
