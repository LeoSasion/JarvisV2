[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src\common\Jarvis.ControlCenter'
$projectPath = Join-Path $sourceRoot 'Jarvis.ControlCenter.csproj'
$diagnosticsRoot = Join-Path $root (
    'src\common\Jarvis.ControlCenter.Diagnostics')
$diagnosticsProjectPath = Join-Path $diagnosticsRoot (
    'Jarvis.ControlCenter.Diagnostics.csproj')
$mainWindowPath = Join-Path $sourceRoot 'MainWindow.xaml'
$mainWindowCodePath = Join-Path $sourceRoot 'MainWindow.xaml.cs'
$viewModelPath = Join-Path $sourceRoot 'ConversationSurfaceViewModel.cs'
$providerPath = Join-Path $sourceRoot 'LocalDiagnosticModelProvider.cs'

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
$diagnosticsProjectText = [IO.File]::ReadAllText($diagnosticsProjectPath)
$mainWindowText = [IO.File]::ReadAllText($mainWindowPath)
$mainWindowCodeText = [IO.File]::ReadAllText($mainWindowCodePath)
$viewModelText = [IO.File]::ReadAllText($viewModelPath)
$providerText = [IO.File]::ReadAllText($providerPath)
$sourceText = @(
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
        Where-Object {
            $_.Extension -In @('.cs', '.xaml', '.csproj') -and
            $_.FullName -notmatch '\\(?:bin|obj)\\'
        } |
        Sort-Object FullName |
        ForEach-Object { [IO.File]::ReadAllText($_.FullName) }
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
        'The surface may compose the reviewed Pi host but must contain no ' +
        'shell process, service, registry, shutdown, injection or P/Invoke API.')

$forbiddenShellSurfacePattern = (
    '(?i)(?:Topmost\s*=\s*"True"|AllowsTransparency\s*=\s*"True"|' +
    'WindowState\s*=\s*"Maximized"|ShowInTaskbar\s*=\s*"False")'
)
Add-Check `
    -Name 'xaml.ordinary-bounded-window' `
    -Passed (
        -not [regex]::IsMatch(
            $mainWindowText,
            $forbiddenShellSurfacePattern) -and
        $mainWindowText.Contains('Width="1440"') -and
        $mainWindowText.Contains('Height="900"') -and
        $mainWindowText.Contains('MinWidth="1180"') -and
        $mainWindowText.Contains('ResizeMode="CanResizeWithGrip"')) `
    -Detail (
        'The surface must remain an ordinary bounded taskbar window, not a ' +
        'transparent, topmost or maximized shell replacement.')

Add-Check `
    -Name 'project.wpf-runtime-and-diagnostics-composition' `
    -Passed (
        $projectText.Contains('<OutputType>WinExe</OutputType>') -and
        $projectText.Contains('<UseWPF>true</UseWPF>') -and
        $projectText.Contains('<StartupObject>Jarvis.ControlCenter.Program</StartupObject>') -and
        $projectText.Contains('<TreatWarningsAsErrors>true</TreatWarningsAsErrors>') -and
        $projectText.Contains('Jarvis.PiAgentHost.csproj') -and
        $diagnosticsProjectText.Contains('<OutputType>Exe</OutputType>') -and
        $diagnosticsProjectText.Contains('Jarvis.ControlCenter.csproj')) `
    -Detail (
        'The deterministic WPF product must reference the reviewed Pi host, ' +
        'while the provider probe remains a separate console diagnostic.')

$requiredVisibleLabels = @(
    'Text="Conversation"',
    'Text="USER"',
    'Text="PI RUNTIME"',
    'Text="READ TOOL"',
    'Text="JARVIS"',
    'Content="SEND"',
    'Content="CANCEL"',
    'Mutation tools: unavailable',
    'SHELL // LOCKED',
    'SAFE SHUTDOWN',
    'production model ',
    'authentication is not configured'
)
$visibleSource = $mainWindowText + [Environment]::NewLine + $providerText
$missingVisibleLabels = @(
    $requiredVisibleLabels |
        Where-Object { -not $visibleSource.Contains($_) }
)
Add-Check `
    -Name 'surface.visible-handoff-and-safety-boundary' `
    -Passed ($missingVisibleLabels.Count -eq 0) `
    -Detail (
        'Missing required conversation/safety markers: ' +
        $(if ($missingVisibleLabels.Count -eq 0) {
            'none'
        }
        else {
            $missingVisibleLabels -join ', '
        }))

Add-Check `
    -Name 'surface.bound-conversation-controls-and-accessibility' `
    -Passed (
        $mainWindowText.Contains('Tag="impeccable:surface-seed:32fb29e4"') -and
        $mainWindowText.Contains('ItemsSource="{Binding Turns}"') -and
        $mainWindowText.Contains('IsEnabled="{Binding CanSubmit}"') -and
        $mainWindowText.Contains('IsEnabled="{Binding CanCancel}"') -and
        $mainWindowText.Contains('PreviewKeyDown="PromptInput_OnPreviewKeyDown"') -and
        $mainWindowText.Contains('AutomationProperties.Name="Send message"') -and
        $mainWindowText.Contains('AutomationProperties.Name="Cancel active turn"')) `
    -Detail (
        'The selected handoff-rail surface must bind real turns and controls, ' +
        'retain keyboard operation and expose accessible action names.')

Add-Check `
    -Name 'runtime.owned-start-stream-cancel-checkpoint-shutdown' `
    -Passed (
        $viewModelText.Contains('PiAgentDesktopRuntime.StartAsync') -and
        $viewModelText.Contains('PiAgentConversationCheckpointStore') -and
        $viewModelText.Contains('PiAgentConversationBinding') -and
        $viewModelText.Contains('SubmitAsync') -and
        $viewModelText.Contains('CancelAsync') -and
        $viewModelText.Contains('runtime.ShutdownAsync') -and
        $mainWindowCodeText.Contains('OnWindowClosing') -and
        $mainWindowCodeText.Contains('ShutdownAsync')) `
    -Detail (
        'The view model must own the Pi runtime, stream bound snapshots, ' +
        'support cancellation and flush/release it on window close.')

$forbiddenToolCallPattern =
    'DesktopModelToolCallStarted\s*\([^\)]*,\s*"(?:bash|edit|write)"'
Add-Check `
    -Name 'provider.deterministic-readonly-disclosed-boundary' `
    -Passed (
        $providerText.Contains('DisplayName = "LOCAL DIAGNOSTIC"') -and
        $providerText.Contains('new DesktopModelToolCallStarted(toolCallId, "ls")') -and
        $providerText.Contains('{\"path\":\".\",\"limit\":40}') -and
        $providerText.Contains('production model') -and
        $providerText.Contains('authentication is not configured') -and
        -not [regex]::IsMatch($providerText, $forbiddenToolCallPattern)) `
    -Detail (
        'The explicit diagnostic provider may request only root-confined ls, ' +
        'must stream a bounded response and must disclose absent production auth.')

Add-Check `
    -Name 'window.local-behavior-and-orderly-close' `
    -Passed (
        $mainWindowCodeText.Contains('DispatcherTimer') -and
        $mainWindowCodeText.Contains('DragMove()') -and
        $mainWindowCodeText.Contains('WindowState = WindowState.Minimized') -and
        $mainWindowCodeText.Contains('CancellationTokenSource timeout') -and
        $mainWindowCodeText.Contains('TimeSpan.FromSeconds(12)') -and
        $mainWindowCodeText.Contains('shutdownInProgress')) `
    -Detail (
        'Window chrome stays local, and closing gives the owned runtime a ' +
        'bounded orderly-shutdown interval.')

$providerProbeOutput = @(
    & $DotnetPath run `
        --project $diagnosticsProjectPath `
        --configuration Release `
        -- `
        --provider-probe 2>&1
)
$providerProbeExitCode = $LASTEXITCODE
$providerProbe = $null
try {
    $providerProbe =
        ($providerProbeOutput -join [Environment]::NewLine) |
        ConvertFrom-Json
}
catch {
    $providerProbe = $null
}
$providerProbePassed =
    $providerProbeExitCode -eq 0 -and
    $null -ne $providerProbe -and
    $providerProbe.Result -eq 'passed' -and
    $providerProbe.RequestedOnlyLs -and
    $providerProbe.StreamedText -and
    -not $providerProbe.ProductionAuthenticationConfigured -and
    -not $providerProbe.MutationPerformed -and
    @($providerProbe.EventSequence).Count -ge 8 -and
    @($providerProbe.Failures).Count -eq 0
Add-Check `
    -Name 'provider.executable-stream-probe' `
    -Passed $providerProbePassed `
    -Detail (
        'The executable provider receipt must prove one ls request, streamed ' +
        'text, no production auth and no mutation. Output: ' +
        (($providerProbeOutput | Select-Object -Last 14) -join ' '))

$buildOutput = @(
    & $DotnetPath build `
        $diagnosticsProjectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$buildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'build.release' `
    -Passed ($buildExitCode -eq 0) `
    -Detail (($buildOutput | Select-Object -Last 10) -join [Environment]::NewLine)

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 2
    receiptType = 'jarvisv2-control-center-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    conversationSupported = $true
    productionAuthenticationConfigured = $false
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
