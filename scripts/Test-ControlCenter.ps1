[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath =
    Join-Path $root 'src\common\Jarvis.ControlCenter\Jarvis.ControlCenter.csproj'
$sourceRoot = Join-Path $root 'src\common\Jarvis.ControlCenter'
$mainWindowPath = Join-Path $sourceRoot 'MainWindow.xaml'
$mainWindowCodePath = Join-Path $sourceRoot 'MainWindow.xaml.cs'

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

$projectText = [IO.File]::ReadAllText($projectPath)
$mainWindowText = [IO.File]::ReadAllText($mainWindowPath)
$mainWindowCodeText = [IO.File]::ReadAllText($mainWindowCodePath)
$sourceText = @(
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
        Where-Object Extension -In @('.cs', '.xaml', '.csproj') |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine

$forbiddenRuntimePattern = (
    '(?i)\b(?:DllImport|LibraryImport|OpenProcess|CreateRemoteThread|' +
    'VirtualAllocEx|WriteProcessMemory|SetWindowsHookEx|NtQueueApcThread|' +
    'StartService|ServiceController|Microsoft\.Win32\.Registry|' +
    'System\.Diagnostics\.Process|ProcessStartInfo|ManagementObjectSearcher|' +
    'RestartManager|ExitWindowsEx|InitiateSystemShutdown)\b'
)
Add-Check `
    -Name 'source.no-live-process-service-registry-or-injection-api' `
    -Passed (-not [regex]::IsMatch($sourceText, $forbiddenRuntimePattern)) `
    -Detail (
        'The control center must contain no process, service, registry, ' +
        'shutdown, injection, hook-installation or P/Invoke API.'
    )

$forbiddenShellSurfacePattern = (
    '(?i)(?:Topmost\s*=\s*"True"|AllowsTransparency\s*=\s*"True"|' +
    'WindowState\s*=\s*"Maximized"|ShowInTaskbar\s*=\s*"False")'
)
Add-Check `
    -Name 'xaml.ordinary-bounded-window' `
    -Passed (
        -not [regex]::IsMatch(
            $mainWindowText,
            $forbiddenShellSurfacePattern
        ) -and
        $mainWindowText.Contains('Width="1440"') -and
        $mainWindowText.Contains('Height="900"') -and
        $mainWindowText.Contains('ResizeMode="CanResizeWithGrip"')
    ) `
    -Detail (
        'The preview must remain a bounded, resizable, taskbar-visible ' +
        'ordinary window; it may not become a transparent or topmost overlay.'
    )

Add-Check `
    -Name 'project.wpf-winexe-warnings-as-errors' `
    -Passed (
        $projectText.Contains('<OutputType>WinExe</OutputType>') -and
        $projectText.Contains('<UseWPF>true</UseWPF>') -and
        $projectText.Contains('<TreatWarningsAsErrors>true</TreatWarningsAsErrors>') -and
        $projectText.Contains('<Deterministic>true</Deterministic>')
    ) `
    -Detail (
        'The preview must be a deterministic WPF WinExe with compiler ' +
        'warnings promoted to errors.'
    )

$requiredVisibleLabels = @(
    'Text="LOCKED"',
    'Text="/ FAIL-CLOSED"',
    'QUARANTINED',
    'STOPPED / MANUAL / PID 0',
    'TARGET MAPPINGS // 0',
    'ACTIVATION // FALSE',
    'LIVE EXPLORER // NOT-RUN',
    'DWM / ISOLATED BRANCH',
    'Text="FORBIDDEN"',
    'READ-ONLY CONTROL SURFACE',
    'EXECUTION DISABLED'
)
$missingVisibleLabels = @(
    $requiredVisibleLabels |
        Where-Object { -not $mainWindowText.Contains($_) }
)
Add-Check `
    -Name 'xaml.visible-safety-boundary' `
    -Passed ($missingVisibleLabels.Count -eq 0) `
    -Detail (
        'Missing required visible labels: ' +
        $(if ($missingVisibleLabels.Count -eq 0) {
            'none'
        }
        else {
            $missingVisibleLabels -join ', '
        })
    )

Add-Check `
    -Name 'code.behavior-local-window-only' `
    -Passed (
        $mainWindowCodeText.Contains('DispatcherTimer') -and
        $mainWindowCodeText.Contains('DragMove()') -and
        $mainWindowCodeText.Contains('WindowState = WindowState.Minimized') -and
        $mainWindowCodeText.Contains('Close()')
    ) `
    -Detail (
        'The only code-behind behavior must be local clock refresh, window ' +
        'drag, minimize and close.'
    )

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
    -Detail (($buildOutput | Select-Object -Last 8) -join [Environment]::NewLine)

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-control-center-audit'
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
