[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root (
    'src\platforms\windows10\Jarvis.Win10.TaskbarEdgeOverlay')
$projectPath = Join-Path $sourceRoot (
    'Jarvis.Win10.TaskbarEdgeOverlay.csproj')
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
$gateText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'TaskbarOverlayGate.cs'))
$nativeText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'NativeTaskbarTarget.cs'))
$windowText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'OverlayWindow.xaml.cs'))
$xamlText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'OverlayWindow.xaml'))
$railXamlText = [IO.File]::ReadAllText(
    (Join-Path $sourceRoot 'TaskbarEdgeRail.xaml'))
$profiles =
    Get-Content -LiteralPath $profilePath -Raw |
        ConvertFrom-Json
$profile = @($profiles.profiles)

Add-Check `
    -Name 'project.read-gate-and-shared-signal-dependencies' `
    -Passed (
        $sourceText.Contains('<OutputType>WinExe</OutputType>') -and
        $sourceText.Contains('Jarvis.Win10.ShellSurfaceProbe.csproj') -and
        $sourceText.Contains('Jarvis.VisualEffects.csproj') -and
        $gateText.Contains('ShellSurfaceInspector.Inspect()') -and
        $sourceText.Contains('TaskbarOverlayPolicy.RequiredCapability')) `
    -Detail (
        'The taskbar edge canary must be an owned WPF WinExe that reuses ' +
        'the exact read-only Shell inventory and shared visual signal.')

Add-Check `
    -Name 'gate.exact-single-primary-bottom-taskbar' `
    -Passed (
        $gateText.Contains('ExactlyOnePrimaryTaskbar') -and
        $gateText.Contains('ExactHandleMatched') -and
        $gateText.Contains('DesktopShellProcessMatched') -and
        $gateText.Contains('RootClassMatched') -and
        $gateText.Contains('RootVisible') -and
        $gateText.Contains('BottomHorizontalGeometry') -and
        $gateText.Contains('Shell_TrayWnd')) `
    -Detail (
        'Admission must bind one explicit Shell_TrayWnd HWND/PID/TID owned ' +
        'by the desktop Shell and reject hidden, top or vertical taskbars.')

$forbiddenPattern = (
    '(?i)\b(?:DwmSetWindowAttribute|SetWindowCompositionAttribute|' +
    'SendMessage|PostMessage|OpenProcess|CreateRemoteThread|VirtualAllocEx|' +
    'WriteProcessMemory|ReadProcessMemory|SetWindowsHookEx|SetWindowPos|' +
    'MoveWindow|ShowWindow|DestroyWindow|TerminateProcess|Process\.Kill|' +
    'CloseMainWindow|RegistryKey|Registry\.|ServiceController|' +
    'windhawk\.exe)\b'
)
Add-Check `
    -Name 'source.no-shell-write-injection-or-system-mutation' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenPattern)) `
    -Detail (
        'The canary may read the exact taskbar and mutate only its own WPF ' +
        'HWND; Shell writes, injection, lifecycle and registry APIs are ' +
        'forbidden.')

$allowedImports = @(
    'IsWindow',
    'GetWindowRect',
    'GetWindowThreadProcessId',
    'GetForegroundWindow',
    'GetDpiForWindow',
    'IsWindowVisible',
    'GetClassNameW',
    'DwmGetWindowAttribute',
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
        'Native imports must be exact target/foreground readers plus ' +
        'own-HWND extended-style access. Actual: ' +
        ($actualImports -join ', '))

Add-Check `
    -Name 'window.owned-click-through-no-activate' `
    -Passed (
        $xamlText.Contains('AllowsTransparency="True"') -and
        $xamlText.Contains('Background="Transparent"') -and
        $xamlText.Contains('Opacity="0"') -and
        $xamlText.Contains('ShowActivated="False"') -and
        $xamlText.Contains('ShowInTaskbar="False"') -and
        $xamlText.Contains('WindowStyle="None"') -and
        $windowText.Contains('ExtendedStyleTransparent') -and
        $windowText.Contains('ExtendedStyleNoActivate') -and
        $windowText.Contains('TransparentHitTest') -and
        @([regex]::Matches(
            $windowText,
            'Marshal\.SetLastPInvokeError\(0\)')).Count -eq 2 -and
        $windowText.Contains('source.Handle')) `
    -Detail (
        'The owned edge HWND must be transparent, mouse-through, ' +
        'non-activating and absent from task switching.')

Add-Check `
    -Name 'visual.vector-edge-bounded-glow-no-content-obscuration' `
    -Passed (
        $railXamlText.Contains('BlurEffect') -and
        $railXamlText.Contains('Radius="3"') -and
        @([regex]::Matches($railXamlText, '<Path')).Count -ge 3 -and
        -not $railXamlText.Contains('LinearGradientBrush') -and
        -not $railXamlText.Contains('Segoe MDL2 Assets') -and
        -not [regex]::IsMatch(
            $railXamlText,
            '(?i)<Image|ImageBrush|\.png|\.jpg|\.jpeg|\.webp|\.gif') -and
        $sourceText.Contains('EdgeHeightDips = 8.0') -and
        $programText.Contains(
            '"neural-void-taskbar-edge-canary-v1"') -and
        $sourceText.Contains('InteractiveTaskbarContentObscured')) `
    -Detail (
        'Only an eight-DIP transparent edge may render authored vectors and ' +
        'a bounded blur; bitmap, glyph and taskbar-content replacement are ' +
        'forbidden.')

Add-Check `
    -Name 'preview.reuses-runtime-vector-model' `
    -Passed (
        $xamlText.Contains('<local:TaskbarEdgeRail') -and
        $railXamlText.Contains('TaskbarEdgeVectorModel') -and
        $sourceText.Contains('GaussianBlurRadius = 3') -and
        $sourceText.Contains('TaskbarEdgeVectorModel.SampleStrokeCoverage') -and
        $sourceText.Contains('TaskbarEdgeVectorModel.GaussianKernel')) `
    -Detail (
        'The runtime XAML and deterministic offline evidence renderer must ' +
        'consume one analytic vector and Gaussian post-process model.')

Add-Check `
    -Name 'signal.shared-rgb-frame-fail-closed' `
    -Passed (
        $windowText.Contains('RgbEffectEngine.Sample(') -and
        $windowText.Contains('VisualSignalFrameFactory.Create(') -and
        $windowText.Contains('VisualSignalFrameCompiler.Compile(') -and
        $windowText.Contains('ReadyForOwnedProcessPrototype') -and
        $windowText.Contains('BuildSignalFrames()') -and
        $windowText.Contains(
            'Shared visual signal frame was rejected.') -and
        $programText.Contains('VisualSignalContract.ContractId') -and
        $sourceText.Contains('SharedRgbBound')) `
    -Detail (
        'The rail must consume the shared RGB/visual-signal contract and ' +
        'hide instead of rendering a rejected frame.')

Add-Check `
    -Name 'retreat.fullscreen-hidden-target-retirement-closed' `
    -Passed (
        $nativeText.Contains('OccludesTaskbarEdge(') -and
        $nativeText.Contains('ExtendedFrameBoundsAttribute = 9') -and
        $nativeText.Contains('DwmGetWindowAttribute(') -and
        $windowText.Contains('EdgeOccludedByFullscreen') -and
        $windowText.Contains('FullscreenRetreatSamples') -and
        $windowText.Contains('SystemParameters.HighContrast') -and
        $windowText.Contains('AccessibilityRetreatSamples') -and
        $windowText.Contains('Opacity = 1.0') -and
        $windowText.Contains('Visibility = Visibility.Hidden') -and
        $windowText.Contains('TargetRetiredOrIncompatible = true') -and
        $windowText.Contains('Close();')) `
    -Detail (
        'Fullscreen coverage and hidden taskbars must retreat visually; ' +
        'target identity or geometry drift must close the session.')

Add-Check `
    -Name 'session.confirmed-ttl-bounded' `
    -Passed (
        $sourceText.Contains('MinimumTtlSeconds = 10') -and
        $sourceText.Contains('MaximumTtlSeconds = 60') -and
        $sourceText.Contains(
            '--confirm-owned-taskbar-edge-overlay-preview') -and
        $windowText.Contains('DateTimeOffset.UtcNow >= expiresAtUtc')) `
    -Detail (
        'The canary must require explicit confirmation and expire after ' +
        '10-60 seconds.')

Add-Check `
    -Name 'receipt.denies-shell-mutation-and-module-activation' `
    -Passed (
        $sourceText.Contains('OwnedWindowOnly') -and
        $sourceText.Contains('MouseTransparent') -and
        $sourceText.Contains('NoActivate') -and
        $sourceText.Contains('ExplorerMutationPerformed') -and
        $sourceText.Contains('InjectionRequested') -and
        $sourceText.Contains('ExplorerRestartRequested') -and
        $sourceText.Contains('RegistryMutationRequested') -and
        $sourceText.Contains('ModuleActivationPermitted')) `
    -Detail (
        'Every receipt must identify the own-process boundary and deny ' +
        'Explorer mutation, injection, restart, registry and modules.')

Add-Check `
    -Name 'profile.taskbar-edge-preview-granted-activation-denied' `
    -Passed (
        $profile.Count -eq 1 -and
        @($profile[0].allowedCapabilities) -contains
            'run-bounded-owned-taskbar-edge-overlay-preview' -and
        -not $profile[0].activationPermitted -and
        $profile[0].liveExplorer -eq 'not-run') `
    -Detail (
        'The exact host profile may grant only the owned edge preview while ' +
        'Shell module activation remains denied.')

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
    -Detail (($buildOutput | Select-Object -Last 12) -join
        [Environment]::NewLine)

$modelReceipt = $null
$previewReceipt = $null
$previewPath = Join-Path $root (
    'artifacts\win10-taskbar-edge-overlay-tests\' +
    'taskbar-edge-canary.png')
if ($buildExitCode -eq 0) {
    $assemblyPath = Join-Path $sourceRoot (
        'bin\Release\net8.0-windows\' +
        'jarvis-win10-taskbar-edge-overlay.dll')
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
        -Name 'model.fail-closed-policy-and-signal-scenarios' `
        -Passed (
            $modelExitCode -eq 0 -and
            $null -ne $modelReceipt -and
            $modelReceipt.result -eq 'passed' -and
            $modelReceipt.scenarioCount -eq 21 -and
            $modelReceipt.passedCount -eq 21 -and
            -not $modelReceipt.explorerMutationPerformed -and
            -not $modelReceipt.injectionRequested -and
            -not $modelReceipt.moduleActivationPermitted) `
        -Detail (
            "Model exit $modelExitCode; scenarios " +
            "$($modelReceipt.passedCount)/$($modelReceipt.scenarioCount).")

    $previewOutput = @(
        & $DotnetPath $assemblyPath render-preview `
            --output $previewPath 2>&1
    )
    $previewExitCode = $LASTEXITCODE
    try {
        $previewReceipt =
            ($previewOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $previewReceipt = $null
    }
    $previewBytes = if (Test-Path -LiteralPath $previewPath -PathType Leaf) {
        [IO.File]::ReadAllBytes($previewPath)
    }
    else {
        [byte[]]@()
    }
    $previewWidth = if ($previewBytes.Length -ge 24) {
        ([int]$previewBytes[16] -shl 24) -bor
        ([int]$previewBytes[17] -shl 16) -bor
        ([int]$previewBytes[18] -shl 8) -bor
        [int]$previewBytes[19]
    }
    else { 0 }
    $previewHeight = if ($previewBytes.Length -ge 24) {
        ([int]$previewBytes[20] -shl 24) -bor
        ([int]$previewBytes[21] -shl 16) -bor
        ([int]$previewBytes[22] -shl 8) -bor
        [int]$previewBytes[23]
    }
    else { 0 }
    Add-Check `
        -Name 'preview.shared-vector-postprocess-offline-render' `
        -Passed (
            $previewExitCode -eq 0 -and
            $null -ne $previewReceipt -and
            $previewReceipt.result -eq
                'rendered-offline-analytic-vector-preview' -and
            $previewReceipt.width -eq 1600 -and
            $previewReceipt.height -eq 48 -and
            $previewReceipt.changedPixelCount -gt 1600 -and
            $previewReceipt.changedPixelCount -lt 4000 -and
            $previewReceipt.distinctChangedColorCount -ge 8 -and
            $previewReceipt.minimumChangedX -eq 0 -and
            $previewReceipt.maximumChangedX -eq 1599 -and
            $previewReceipt.minimumChangedY -eq 0 -and
            $previewReceipt.maximumChangedY -le 7 -and
            -not $previewReceipt.shellContacted -and
            -not $previewReceipt.explorerMutationPerformed -and
            -not $previewReceipt.moduleActivationPermitted -and
            $previewWidth -eq 1600 -and
            $previewHeight -eq 48 -and
            $previewBytes.Length -gt 256) `
        -Detail (
            "Preview exit $previewExitCode; PNG ${previewWidth}x" +
            "${previewHeight}, $($previewBytes.Length) bytes, " +
            "$($previewReceipt.changedPixelCount) changed pixels, " +
            "$($previewReceipt.distinctChangedColorCount) colors, Y " +
            "$($previewReceipt.minimumChangedY).." +
            "$($previewReceipt.maximumChangedY).")
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-win10-taskbar-edge-overlay-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    scenarioCount = if ($null -eq $modelReceipt) { 0 } else {
        $modelReceipt.scenarioCount
    }
    scenarioPassedCount = if ($null -eq $modelReceipt) { 0 } else {
        $modelReceipt.passedCount
    }
    previewPath = if ($null -eq $previewReceipt) { $null } else {
        $previewReceipt.outputPath
    }
    liveMutationRun = $false
    explorerMutationPerformed = $false
    injectionRequested = $false
    moduleActivationPermitted = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 10

if (-not $passed) {
    exit 1
}
