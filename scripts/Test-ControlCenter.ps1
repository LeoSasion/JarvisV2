[CmdletBinding()]
param(
    [string]$DotnetPath = 'dotnet',
    [string]$NodePath = 'node'
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
$handoffVfxPath = Join-Path $sourceRoot 'HandoffConstellationLayer.cs'
$viewModelPath = Join-Path $sourceRoot 'ConversationSurfaceViewModel.cs'
$uiTextPath = Join-Path $sourceRoot 'UiText.cs'
$conversationStatePath = Join-Path $root (
    'src\common\Jarvis.PiAgentHost\ConversationState.cs')
$providerPath = Join-Path $sourceRoot 'LocalDiagnosticModelProvider.cs'
$appPath = Join-Path $sourceRoot 'App.xaml.cs'
$launchOptionsPath = Join-Path $sourceRoot 'ConversationLaunchOptions.cs'
$bootstrapPath = Join-Path $sourceRoot 'DesktopRuntimeBootstrap.cs'
$bootstrapProbePath = Join-Path $sourceRoot 'DesktopRuntimeBootstrapProbe.cs'
$modelSetupPath = Join-Path $sourceRoot 'ModelSetupWindow.xaml'
$modelSetupCodePath = Join-Path $sourceRoot 'ModelSetupWindow.xaml.cs'
$sessionLaunchPath = Join-Path $sourceRoot 'SessionLaunchWindow.xaml'
$sessionLaunchCodePath = Join-Path $sourceRoot 'SessionLaunchWindow.xaml.cs'
$sessionAdmissionPath = Join-Path $sourceRoot (
    'DesktopSessionLaunchAdmission.cs')
$sessionAdmissionProbePath = Join-Path $sourceRoot (
    'DesktopSessionLaunchAdmissionProbe.cs')
$recentSessionStorePath = Join-Path $sourceRoot (
    'DesktopRecentSessionStore.cs')
$recentSessionStoreProbePath = Join-Path $sourceRoot (
    'DesktopRecentSessionStoreProbe.cs')
$desktopPresenceRoot = Join-Path $root (
    'src\common\Jarvis.DesktopPresence')
$desktopPresenceProjectPath = Join-Path $desktopPresenceRoot (
    'Jarvis.DesktopPresence.csproj')
$desktopStartupRegistrationPath = Join-Path $desktopPresenceRoot (
    'DesktopStartupRegistration.cs')
$singleInstancePath = Join-Path $desktopPresenceRoot (
    'ControlCenterSingleInstance.cs')
$desktopPresenceProbePath = Join-Path $desktopPresenceRoot (
    'DesktopPresenceProbe.cs')

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
$handoffVfxText = [IO.File]::ReadAllText($handoffVfxPath)
$viewModelText = [IO.File]::ReadAllText($viewModelPath)
$uiTextText = [IO.File]::ReadAllText($uiTextPath)
$conversationStateText = [IO.File]::ReadAllText($conversationStatePath)
$providerText = [IO.File]::ReadAllText($providerPath)
$appText = [IO.File]::ReadAllText($appPath)
$launchOptionsText = [IO.File]::ReadAllText($launchOptionsPath)
$bootstrapText = [IO.File]::ReadAllText($bootstrapPath)
$bootstrapProbeText = [IO.File]::ReadAllText($bootstrapProbePath)
$modelSetupText = [IO.File]::ReadAllText($modelSetupPath)
$modelSetupCodeText = [IO.File]::ReadAllText($modelSetupCodePath)
$sessionLaunchText = [IO.File]::ReadAllText($sessionLaunchPath)
$sessionLaunchCodeText = [IO.File]::ReadAllText($sessionLaunchCodePath)
$sessionAdmissionText = [IO.File]::ReadAllText($sessionAdmissionPath)
$sessionAdmissionProbeText = [IO.File]::ReadAllText(
    $sessionAdmissionProbePath)
$recentSessionStoreText = [IO.File]::ReadAllText(
    $recentSessionStorePath)
$recentSessionStoreProbeText = [IO.File]::ReadAllText(
    $recentSessionStoreProbePath)
$desktopPresenceProjectText = [IO.File]::ReadAllText(
    $desktopPresenceProjectPath)
$desktopStartupRegistrationText = [IO.File]::ReadAllText(
    $desktopStartupRegistrationPath)
$singleInstanceText = [IO.File]::ReadAllText($singleInstancePath)
$desktopPresenceProbeText = [IO.File]::ReadAllText(
    $desktopPresenceProbePath)
$localizedSurfaceText = @(
    $mainWindowText,
    $viewModelText,
    $modelSetupText,
    $modelSetupCodeText,
    $sessionLaunchText,
    $sessionLaunchCodeText
) -join [Environment]::NewLine
$declaredUiResourceKeys = @(
    [regex]::Matches($uiTextText, '\["(Loc\.[A-Za-z0-9.]+)"\]') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
)
$referencedUiResourceKeys = @(
    [regex]::Matches($localizedSurfaceText, 'Loc\.[A-Za-z0-9.]+') |
        ForEach-Object { $_.Value } |
        Sort-Object -Unique
)
$missingUiResourceKeys = @(
    $referencedUiResourceKeys |
        Where-Object { $_ -notin $declaredUiResourceKeys }
)
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
    -Name 'surface.reversible-own-window-immersive-mode' `
    -Passed (
        $mainWindowText.Contains(
            'PreviewKeyDown="MainWindow_OnPreviewKeyDown"') -and
        $mainWindowText.Contains('x:Name="ImmersiveModeButton"') -and
        $mainWindowText.Contains('x:Name="ImmersiveExitButton"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Immersive.EnterAutomation}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Immersive.ExitAutomation}"') -and
        $mainWindowText.Contains(
            'Data="M1,6 L1,1 L6,1 M12,1 L17,1 L17,6 M17,12 L17,17 L12,17 M6,17 L1,17 L1,12"') -and
        $mainWindowCodeText.Contains('eventArgs.Key == Key.F11') -and
        $mainWindowCodeText.Contains(
            'eventArgs.Key == Key.Escape && immersiveMode') -and
        $mainWindowCodeText.Contains('eventArgs.IsRepeat') -and
        $mainWindowCodeText.Contains('EnterImmersiveMode()') -and
        $mainWindowCodeText.Contains('ExitImmersiveMode()') -and
        $mainWindowCodeText.Contains(
            'HeaderChrome.Visibility = Visibility.Collapsed') -and
        $mainWindowCodeText.Contains(
            'WorkspaceRail.Visibility = Visibility.Collapsed') -and
        $mainWindowCodeText.Contains(
            'RuntimeInspector.Visibility = Visibility.Collapsed') -and
        $mainWindowCodeText.Contains(
            'StatusDock.Visibility = Visibility.Collapsed') -and
        $mainWindowCodeText.Contains(
            'ConversationShortcuts.Visibility = Visibility.Collapsed') -and
        $mainWindowCodeText.Contains(
            'ImmersiveExitButton.Visibility = Visibility.Visible') -and
        $mainWindowCodeText.Contains(
            'WindowState = WindowState.Maximized') -and
        $mainWindowCodeText.Contains(
            'WindowState = windowStateBeforeImmersive') -and
        $mainWindowCodeText.Contains(
            'ConversationWorkspace.IsKeyboardFocusWithin') -and
        $mainWindowCodeText.Contains('DispatcherPriority.Input') -and
        -not $mainWindowCodeText.Contains('Topmost = true') -and
        -not $mainWindowCodeText.Contains('ShowInTaskbar = false')) `
    -Detail (
        'F11 must reversibly focus the owned conversation window, Esc must ' +
        'exit before composer cancellation, an accessible in-frame escape ' +
        'control must remain visible, and the mode must restore layout, focus ' +
        'and prior window state without topmost or taskbar takeover.')

Add-Check `
    -Name 'project.wpf-runtime-and-diagnostics-composition' `
    -Passed (
        $projectText.Contains('<OutputType>WinExe</OutputType>') -and
        $projectText.Contains('<UseWPF>true</UseWPF>') -and
        $projectText.Contains('<StartupObject>Jarvis.ControlCenter.Program</StartupObject>') -and
        $projectText.Contains('<TreatWarningsAsErrors>true</TreatWarningsAsErrors>') -and
        $projectText.Contains('Jarvis.DesktopPresence.csproj') -and
        $projectText.Contains('Jarvis.PiAgentHost.csproj') -and
        $projectText.Contains('Jarvis.VisualEffects.csproj') -and
        $diagnosticsProjectText.Contains('<OutputType>Exe</OutputType>') -and
        $diagnosticsProjectText.Contains('Jarvis.ControlCenter.csproj') -and
        $diagnosticsProjectText.Contains('Jarvis.DesktopPresence.csproj') -and
        $desktopPresenceProjectText.Contains('<TargetFramework>net8.0-windows</TargetFramework>') -and
        $desktopPresenceProjectText.Contains('<TreatWarningsAsErrors>true</TreatWarningsAsErrors>')) `
    -Detail (
        'The deterministic WPF product must reference the reviewed Pi host, ' +
        'while the provider probe remains a separate console diagnostic.')

$desktopPresenceStaticBoundary =
    $desktopStartupRegistrationText.Contains(
        'current-user-run-key-exact-reg-sz-no-shell') -and
    $desktopStartupRegistrationText.Contains(
        'Software\Microsoft\Windows\CurrentVersion\Run') -and
    $desktopStartupRegistrationText.Contains(
        'RegistryValueKind.String') -and
    $desktopStartupRegistrationText.Contains(
        '--resume-latest --minimized') -and
    $desktopStartupRegistrationText.Contains(
        'RegistryValueOptions.DoNotExpandEnvironmentNames') -and
    $desktopStartupRegistrationText.Contains('FileAttributes.ReparsePoint') -and
    $singleInstanceText.Contains('EventResetMode.AutoReset') -and
    $singleInstanceText.Contains('WindowsIdentity.GetCurrent') -and
    $singleInstanceText.Contains('RegisterWaitForSingleObject') -and
    $singleInstanceText.Contains('SignalPrimary') -and
    $desktopPresenceProbeText.Contains('MemoryStartupValueStore') -and
    $desktopPresenceProbeText.Contains(
        'ProductionStartupStateTouched: false') -and
    $appText.Contains('ControlCenterSingleInstance') -and
    $appText.Contains('TryParseResumeLatest') -and
    $appText.Contains('"--resume-latest"') -and
    $appText.Contains('"--minimized"') -and
    $mainWindowText.Contains('x:Name="ResumeLatestButton"') -and
    $mainWindowText.Contains('x:Name="StartupRegistrationButton"') -and
    $mainWindowCodeText.Contains('ResumeLatestSessionAsync') -and
    $mainWindowCodeText.Contains('FindLatestAvailable') -and
    $mainWindowCodeText.Contains('startupRegistration.SetEnabled') -and
    -not [regex]::IsMatch(
        ($desktopStartupRegistrationText + $singleInstanceText),
        '(?i)\b(?:cmd\.exe|powershell|pwsh|ProcessStartInfo|' +
        'System\.Diagnostics\.Process|DllImport|LibraryImport|' +
        'CreateRemoteThread|WriteProcessMemory|SetWindowsHookEx)\b')
Add-Check `
    -Name 'desktop-presence.exact-current-user-startup-and-single-instance' `
    -Passed $desktopPresenceStaticBoundary `
    -Detail (
        'Desktop presence must use one exact current-user REG_SZ command, ' +
        'revalidate the latest workspace, coordinate one per-user process, ' +
        'and expose no shell, child-process or injection path.')

Add-Check `
    -Name 'surface.retained-handoff-vfx-and-neural-scrollbar' `
    -Passed (
        $mainWindowText.Contains('x:Name="HandoffConstellationVfx"') -and
        $mainWindowText.Contains('IsHitTestVisible="False"') -and
        $handoffVfxText.Contains(
            'OnCreateAutomationPeer() => null') -and
        $mainWindowText.Contains('x:Name="NeuralScrollTrack"') -and
        $mainWindowText.Contains('x:Name="NeuralScrollThumb"') -and
        $mainWindowText.Contains('ScrollBar.PageUpCommand') -and
        $mainWindowText.Contains('ScrollBar.PageDownCommand') -and
        $mainWindowText.Contains('<Trigger Property="IsDragging" Value="True">') -and
        $mainWindowText.Contains('<Trigger Property="Orientation" Value="Horizontal">') -and
        $mainWindowText.Contains('SystemColors.HighlightBrushKey') -and
        $mainWindowCodeText.Contains('HandoffConstellationVfx.Attach') -and
        $mainWindowCodeText.Contains('HandoffConstellationVfx.SetState') -and
        $mainWindowCodeText.Contains('HandoffConstellationVfx.Detach') -and
        $handoffVfxText.Contains(
            'handoff-constellation-with-triangle-glow-v2') -and
        $handoffVfxText.Contains('active-corner-triangle') -and
        $handoffVfxText.Contains('BlurEffect') -and
        $handoffVfxText.Contains(
            'bounded-vector-gaussian-glow-v1') -and
        $handoffVfxText.Contains('MaxGlowRegionWidth = 160.0') -and
        $handoffVfxText.Contains('MaxGlowRegionHeight = 72.0') -and
        $handoffVfxText.Contains('MaxStaticCommands = 96') -and
        $handoffVfxText.Contains('MaxPerFrameCommands = 24') -and
        $handoffVfxText.Contains('RenderSampleHz = 30') -and
        $handoffVfxText.Contains('RgbEffectEngine.Sample') -and
        $handoffVfxText.Contains('RetainedVectorSceneCompiler.Compile') -and
        $handoffVfxText.Contains('SystemParameters.HighContrast') -and
        $handoffVfxText.Contains('SystemParameters.ClientAreaAnimation') -and
        $handoffVfxText.Contains('RenderCapability.Tier') -and
        $handoffVfxText.Contains('WindowState.Minimized') -and
        -not $handoffVfxText.Contains('RenderTargetBitmap') -and
        -not $handoffVfxText.Contains('BitmapImage') -and
        -not $handoffVfxText.Contains('ImageBrush') -and
        -not $handoffVfxText.Contains('BitmapCache') -and
        -not $handoffVfxText.Contains('CompositionTarget.Rendering')) `
    -Detail (
        'The selected B constellation plus triangular active-corner focus must ' +
        'use the shared retained-vector/RGB boundary and one bounded blur ' +
        'post-process without bitmap assets; native accessibility, visibility ' +
        'and scrollbar Track behavior must remain intact.')

$requiredVisibleLabels = @(
    'Loc.Conversation.Title',
    'Loc.Stage.User',
    'Loc.Stage.Pi',
    'Loc.Stage.Tool',
    'Loc.Stage.Jarvis',
    'Loc.Composer.Send',
    'Loc.Common.Cancel',
    'Writes: desktop-owner approval only',
    'Shell / direct edit / unattended approval: locked',
    'NEW UTF-8 FILE PROPOSAL',
    'EXACT TEXT REPLACEMENT',
    'MULTI-HUNK PATCH',
    'MULTI-FILE CHANGE SET',
    'REJECT ALL',
    'APPLY CHANGE SET ONCE',
    'APPROVE ONCE',
    'CREATE ONCE',
    'APPLY PATCH ONCE',
    'Loc.Review.Section',
    'Loc.Review.Start',
    'Loc.Review.Rearm',
    'Loc.Review.Stop',
    'four writes maximum',
    'SHELL // LOCKED',
    'SAFE SHUTDOWN',
    'CONFIGURE OPENAI',
    'START PI SESSION',
    'Loc.Presence.Section',
    'Loc.Presence.Resume',
    'Loc.Presence.Enable',
    'production model ',
    'authentication is not configured'
)
$visibleSource = @(
    $mainWindowText,
    $viewModelText,
    $uiTextText,
    $conversationStateText,
    $providerText
) -join [Environment]::NewLine
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
        $mainWindowText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Composer.SendAutomation}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Composer.CancelAutomation}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{Binding OriginalAutomationName}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{Binding ProposedAutomationName}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.LiveSetting="Polite"') -and
        $mainWindowText.Contains('ItemsSource="{Binding WorkspaceEdits}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{Binding RejectionAutomationName}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{Binding ApprovalAutomationName}"') -and
        $mainWindowText.Contains(
            'Content="{Binding ApproveActionLabel}"') -and
        $mainWindowText.Contains(
            'Content="{Binding RejectActionLabel}"') -and
        $mainWindowText.Contains('Text="{Binding ProposalLabel}"') -and
        $mainWindowText.Contains('Text="{Binding PathLabel}"') -and
        $mainWindowText.Contains('ItemsSource="{Binding FileChanges}"') -and
        $mainWindowText.Contains('ItemsSource="{Binding ReviewSegments}"') -and
        [regex]::Matches(
            $mainWindowText,
            'VerticalScrollBarVisibility="Disabled"').Count -ge 2 -and
        $conversationStateText.Contains('NEW UTF-8 FILE PROPOSAL') -and
        $conversationStateText.Contains('EXACT TEXT REPLACEMENT') -and
        $conversationStateText.Contains('MULTI-HUNK PATCH') -and
        $conversationStateText.Contains('MULTI-FILE CHANGE SET') -and
        $conversationStateText.Contains('REJECT ALL') -and
        $conversationStateText.Contains('APPLY CHANGE SET ONCE') -and
        $conversationStateText.Contains('CREATE ONCE') -and
        $conversationStateText.Contains('APPLY PATCH ONCE') -and
        $conversationStateText.Contains('APPROVE ONCE') -and
        $mainWindowCodeText.Contains(
            'ApproveWorkspaceEditButton_OnClick') -and
        $mainWindowCodeText.Contains(
            'RejectWorkspaceEditButton_OnClick')) `
    -Detail (
        'The selected handoff-rail surface must bind real turns and controls, ' +
        'retain keyboard operation and expose accessible action names.')

Add-Check `
    -Name 'surface.explicit-protected-provider-setup' `
    -Passed (
        $mainWindowText.Contains(
            'Content="{DynamicResource Loc.Model.Configure}"') -and
        $mainWindowCodeText.Contains('ModelSetupWindow') -and
        $modelSetupText.Contains('x:Name="ApiKeyInput"') -and
        $modelSetupText.Contains('PasswordBox') -and
        $modelSetupText.Contains('gpt-5.6-sol') -and
        $modelSetupText.Contains(
            'READ / GREP / FIND / LS / PROPOSE_EDIT / PROPOSE_PATCH / PROPOSE_CREATE_FILE / PROPOSE_CHANGE_SET') -and
        $uiTextText.Contains(
            'WRITE // DESKTOP OWNER APPROVAL ONLY') -and
        $uiTextText.Contains('RETENTION // STORE FALSE') -and
        $uiTextText.Contains('SIDECAR // OFFLINE') -and
        $modelSetupText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Setup.KeyAutomation}"') -and
        $modelSetupText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Setup.SaveAutomation}"') -and
        $modelSetupCodeText.Contains(
            'OpenAiApiKeyCredentialStore.ValidateApiKey') -and
        $modelSetupCodeText.Contains(
            'Loc.Setup.Unreadable') -and
        $modelSetupCodeText.Contains('credentialStore.SaveAsync') -and
        $modelSetupCodeText.Contains('ApiKeyInput.Clear()') -and
        $mainWindowCodeText.Contains('replacementRequired = true')) `
    -Detail (
        'Provider setup must be an explicit keyboard-accessible PasswordBox ' +
        'flow that discloses model, tools, retention and sidecar boundaries, ' +
        'then protects the value without showing the previous key.')

Add-Check `
    -Name 'surface.reviewed-iteration-owner-policy' `
    -Passed (
        $mainWindowText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Review.StartAutomation}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Review.RearmAutomation}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Review.StopAutomation}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Review.RunTestsAutomation}"') -and
        $mainWindowText.Contains(
            'IsEnabled="{Binding CanStartReviewedIteration}"') -and
        $mainWindowText.Contains(
            'IsEnabled="{Binding CanResumeReviewedIteration}"') -and
        $mainWindowText.Contains(
            'IsEnabled="{Binding CanStopReviewedIteration}"') -and
        $mainWindowText.Contains(
            'IsEnabled="{Binding CanRunTrustedValidation}"') -and
        $mainWindowText.Contains('ReviewedIterationStatusLabel') -and
        $mainWindowText.Contains('ReviewedIterationReceiptLabel') -and
        $mainWindowText.Contains(
            'Loc.Review.PolicySummary') -and
        $mainWindowCodeText.Contains(
            'StartReviewedIterationButton_OnClick') -and
        $mainWindowCodeText.Contains(
            'ResumeReviewedIterationButton_OnClick') -and
        $mainWindowCodeText.Contains(
            'StopReviewedIterationButton_OnClick') -and
        $mainWindowCodeText.Contains(
            'RunTrustedValidationButton_OnClick') -and
        $mainWindowCodeText.Contains(
            'Type the reviewed iteration mission in the composer') -and
        $mainWindowText.Contains('Loc.Composer.Mode') -and
        $mainWindowText.Contains('Style="{StaticResource ActionButton}"') -and
        $viewModelText.Contains(
            'PiAgentReviewedIterationCoordinator.OpenAsync') -and
        $viewModelText.Contains(
            'StartReviewedIterationAsync') -and
        $viewModelText.Contains(
            'ApproveAndContinueAsync') -and
        $viewModelText.Contains(
            'RunTrustedValidationAndContinueAsync') -and
        $viewModelText.Contains(
            'ObserveReviewedIterationCompletionAsync') -and
        $viewModelText.Contains(
            'reviewedIteration.SuspendAsync')) `
    -Detail (
        'The incumbent conversation surface must expose a named, keyboard-' +
        'reachable owner policy with durable status, start/test-once/re-arm/stop ' +
        'actions and the repository-plus-pinned-test coordinator lifecycle.')

Add-Check `
    -Name 'runtime.owned-start-stream-cancel-checkpoint-shutdown' `
    -Passed (
        $viewModelText.Contains('PiAgentDesktopRuntime.StartAsync') -and
        $viewModelText.Contains('PiAgentConversationCheckpointStore') -and
        $viewModelText.Contains('PiAgentReviewedIterationCoordinator') -and
        $viewModelText.Contains('PiAgentConversationBinding') -and
        $viewModelText.Contains('SubmitAsync') -and
        $viewModelText.Contains('CancelAsync') -and
        $viewModelText.Contains('ApplyWorkspaceEditAsync') -and
        $viewModelText.Contains('RejectWorkspaceEditAsync') -and
        $viewModelText.Contains('runtime.ShutdownAsync') -and
        $mainWindowCodeText.Contains('OnWindowClosing') -and
        $mainWindowCodeText.Contains('ShutdownAsync')) `
    -Detail (
        'The view model must own the Pi runtime, stream bound snapshots, ' +
        'support cancellation and flush/release it on window close.')

Add-Check `
    -Name 'surface.in-app-session-launch-and-admission' `
    -Passed (
        $mainWindowText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Empty.StartAutomation}"') -and
        $mainWindowText.Contains('IsEnabled="{Binding CanLaunchSession}"') -and
        $mainWindowCodeText.Contains('SessionLaunchWindow') -and
        $mainWindowCodeText.Contains('ResolveInitialWorkspace') -and
        $mainWindowCodeText.Contains('conversation.LaunchAsync') -and
        $viewModelText.Contains('public async Task LaunchAsync') -and
        $viewModelText.Contains('No command line is required.') -and
        $sessionLaunchText.Contains('Loc.Launch.Heading') -and
        $sessionLaunchText.Contains('x:Name="WorkspaceInput"') -and
        $sessionLaunchText.Contains('x:Name="LocalProviderOption"') -and
        $sessionLaunchText.Contains('x:Name="OpenAiProviderOption"') -and
        $sessionLaunchText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Launch.BrowseAutomation}"') -and
        $sessionLaunchText.Contains(
            'AutomationProperties.Name="{DynamicResource Loc.Launch.StartAutomation}"') -and
        $sessionLaunchText.Contains('IsDefault="True"') -and
        $sessionLaunchText.Contains('IsEnabled="False"') -and
        $uiTextText.Contains('TOOLS // READ + PROPOSE') -and
        $uiTextText.Contains('WRITES // OWNER REVIEW') -and
        $uiTextText.Contains('SHELL // LOCKED') -and
        $sessionLaunchText.Contains('Loc.Launch.Recent') -and
        $sessionLaunchText.Contains('x:Name="RecentSessionsList"') -and
        $uiTextText.Contains('CURRENTUSER DPAPI') -and
        $sessionLaunchText.Contains('{Binding ActionLabel}') -and
        $sessionLaunchText.Contains(
            'AutomationProperties.Name="{Binding AutomationName}"') -and
        $sessionLaunchCodeText.Contains('OpenFolderDialog') -and
        $sessionLaunchCodeText.Contains('RecentSessionButton_OnClick') -and
        $sessionLaunchCodeText.Contains('AdmitAndClose') -and
        $sessionLaunchCodeText.Contains('Loc.Launch.VerifyResume') -and
        $sessionLaunchCodeText.Contains(
            'DesktopSessionLaunchAdmission.Admit') -and
        $sessionLaunchCodeText.Contains(
            'workspace.Result == "passed"') -and
        $sessionAdmissionText.Contains('RejectsWindowsPathShape') -and
        $sessionAdmissionText.Contains('EnsureNoReparsePoints') -and
        $sessionAdmissionText.Contains('DesktopRuntimeBootstrap.Resolve') -and
        $sessionAdmissionProbeText.Contains('UnknownProviderRejected') -and
        $mainWindowCodeText.Contains('recentStore.LoadAsync') -and
        $mainWindowCodeText.Contains('recentStore.RememberAsync') -and
        $recentSessionStoreText.Contains('DataProtectionScope.CurrentUser') -and
        $recentSessionStoreText.Contains('FileOptions.WriteThrough') -and
        $recentSessionStoreText.Contains(
            'File.Move(temporaryPath, catalogPath, overwrite: true)') -and
        $recentSessionStoreText.Contains('MaximumEntries = 8') -and
        $recentSessionStoreText.Contains('EnsureNoReparsePoints') -and
        $recentSessionStoreText.Contains(
            'DesktopSessionLaunchAdmission.AdmitWorkspace') -and
        -not $recentSessionStoreText.Contains('ApiKey')) `
    -Detail (
        'The empty state must launch or one-action resume a keyboard-accessible ' +
        'native workspace/provider flow, keep the recent catalog encrypted, ' +
        'revalidate paths and runtime, and transition the window in-process.')

Add-Check `
    -Name 'runtime.portable-bootstrap-and-opt-in-provider' `
    -Passed (
        $appText.Contains('"--conversation"') -and
        $appText.Contains('"--provider"') -and
        $appText.Contains('DesktopSessionLaunchAdmission.Admit') -and
        $launchOptionsText.Contains('LocalDiagnostic') -and
        $launchOptionsText.Contains('OpenAiResponses') -and
        $bootstrapText.Contains(
            'runtime\node\node.exe') -and
        $bootstrapText.Contains(
            'runtime\git\cmd\git.exe') -and
        $bootstrapText.Contains(
            'runtime\pi-agent\src\host.mjs') -and
        $bootstrapText.Contains('JARVIS2_NODE_PATH') -and
        $bootstrapText.Contains(
            '@earendil-works\pi-ai\package.json') -and
        $bootstrapText.Contains(
            '@earendil-works\pi-coding-agent\package.json') -and
        $bootstrapText.Contains(
            'pi-agent-desktop-host-contract.json') -and
        $bootstrapText.Contains('ValidatePackageManifest') -and
        $bootstrapText.Contains('ValidateGitRuntimeClosure') -and
        $bootstrapText.Contains('gitRuntimeFileCount') -and
        $bootstrapText.Contains('foreach ((string relativePath, string expected)') -and
        $bootstrapProbeText.Contains('packaged-layout') -and
        $bootstrapProbeText.Contains('developer-layout') -and
        $bootstrapProbeText.Contains('TamperedGitRuntimeRejected') -and
        $bootstrapProbeText.Contains('ExtraGitRuntimeFileRejected') -and
        $viewModelText.Contains('OpenAiResponsesModelProvider') -and
        $viewModelText.Contains('OpenAiApiKeyCredentialStore') -and
        $viewModelText.Contains('providerCredentialReady')) `
    -Detail (
        'The ordinary desktop process must resolve a complete packaged ' +
        'Node/Pi/fixed-Git layout before developer fallback and expose production ' +
        'Responses only through an explicit provider choice and protected key.')

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

Add-Check `
    -Name 'surface.windows-owned-display-language' `
    -Passed (
        $appText.Contains(
            'UiText.ApplyWindowsLanguage(this, CultureInfo.CurrentUICulture)') -and
        $uiTextText.Contains(
            'LanguageAuthority = "windows-current-ui-culture"') -and
        $uiTextText.Contains('CultureInfo.GetCultureInfo("zh-CN")') -and
        $uiTextText.Contains('CultureInfo.GetCultureInfo("en-US")') -and
        $uiTextText.Contains('InternalOverrideSupported') -and
        $uiTextText.Contains('SettingsPersisted') -and
        $mainWindowText.Contains(
            'Text="{DynamicResource Loc.Language.Authority}"') -and
        $mainWindowText.Contains(
            'Text="{DynamicResource Loc.Language.Current}"') -and
        $mainWindowText.Contains(
            'Text="{DynamicResource Loc.Language.Description}"') -and
        $sessionLaunchText.Contains('DynamicResource Loc.Launch.') -and
        $modelSetupText.Contains('DynamicResource Loc.Setup.') -and
        $missingUiResourceKeys.Count -eq 0 -and
        -not $uiTextText.Contains('language-settings.json') -and
        -not $uiTextText.Contains('Set-WinUserLanguageList')) `
    -Detail (
        'The desktop must resolve Windows CurrentUICulture at startup, expose ' +
        'that authority read-only, localize all native windows from one complete ' +
        'catalog, and persist no competing language preference. Missing ' +
        'resource keys: ' +
        $(if ($missingUiResourceKeys.Count -eq 0) {
            'none'
        }
        else {
            $missingUiResourceKeys -join ', '
        }))

$uiLanguageProbeOutput = @(
    & $DotnetPath run `
        --project $diagnosticsProjectPath `
        --configuration Release `
        -- `
        --ui-language-probe 2>&1
)
$uiLanguageProbeExitCode = $LASTEXITCODE
$uiLanguageProbe = $null
try {
    $uiLanguageProbe =
        ($uiLanguageProbeOutput -join [Environment]::NewLine) |
            ConvertFrom-Json
}
catch {
    $uiLanguageProbe = $null
}
$uiLanguageProbePassed =
    $uiLanguageProbeExitCode -eq 0 -and
    $null -ne $uiLanguageProbe -and
    $uiLanguageProbe.Result -eq 'passed' -and
    $uiLanguageProbe.WindowsAuthority -eq
        'windows-current-ui-culture' -and
    $uiLanguageProbe.SimplifiedChineseResource -eq 'zh-CN' -and
    $uiLanguageProbe.SimplifiedChineseNeutralResource -eq 'zh-CN' -and
    $uiLanguageProbe.SimplifiedChineseSingaporeResource -eq 'zh-CN' -and
    $uiLanguageProbe.EnglishResource -eq 'en-US' -and
    $uiLanguageProbe.UnsupportedFallbackResource -eq 'en-US' -and
    $uiLanguageProbe.ResourceCatalogComplete -and
    -not $uiLanguageProbe.InternalOverrideSupported -and
    -not $uiLanguageProbe.SettingsPersisted -and
    -not $uiLanguageProbe.ReadyForShellMutation -and
    -not $uiLanguageProbe.ActivationPermitted -and
    $uiLanguageProbe.LiveExplorer -eq 'not-run' -and
    -not $uiLanguageProbe.MutationPerformed -and
    @($uiLanguageProbe.Failures).Count -eq 0
Add-Check `
    -Name 'surface.executable-windows-language-probe' `
    -Passed $uiLanguageProbePassed `
    -Detail (
        'The executable receipt must select Simplified Chinese and English ' +
        'from Windows culture, fall back deterministically, prove complete ' +
        'resources, and expose no local override, persistence, shell mutation ' +
        'or activation path. Output: ' +
        (($uiLanguageProbeOutput | Select-Object -Last 18) -join ' '))

$desktopPresenceProbeOutput = @(
    & $DotnetPath run `
        --project $diagnosticsProjectPath `
        --configuration Release `
        -- `
        --desktop-presence-probe 2>&1
)
$desktopPresenceProbeExitCode = $LASTEXITCODE
$desktopPresenceProbe = $null
try {
    $desktopPresenceProbe =
        ($desktopPresenceProbeOutput -join [Environment]::NewLine) |
            ConvertFrom-Json
}
catch {
    $desktopPresenceProbe = $null
}
$desktopPresenceProbePassed =
    $desktopPresenceProbeExitCode -eq 0 -and
    $null -ne $desktopPresenceProbe -and
    $desktopPresenceProbe.Result -eq 'passed' -and
    $desktopPresenceProbe.RegistrationEnablePassed -and
    $desktopPresenceProbe.RegistrationIdempotencePassed -and
    $desktopPresenceProbe.RegistrationDriftVisible -and
    $desktopPresenceProbe.RegistrationDisablePassed -and
    $desktopPresenceProbe.ExactResumeCommandPassed -and
    $desktopPresenceProbe.SingleInstanceAdmissionPassed -and
    $desktopPresenceProbe.SecondaryActivationPassed -and
    $desktopPresenceProbe.PrimaryReacquirePassed -and
    -not $desktopPresenceProbe.ProductionStartupStateTouched -and
    @($desktopPresenceProbe.Failures).Count -eq 0
Add-Check `
    -Name 'desktop-presence.executable-registration-and-instance-probe' `
    -Passed $desktopPresenceProbePassed `
    -Detail (
        'The executable probe must prove exact enable/idempotence/drift/' +
        'disable behavior, one primary instance, secondary activation, clean ' +
        'reacquire and zero production startup-state mutation. Output: ' +
        (($desktopPresenceProbeOutput | Select-Object -Last 18) -join ' '))

$handoffVfxProbeOutput = @(
    & $DotnetPath run `
        --project $diagnosticsProjectPath `
        --configuration Release `
        -- `
        --handoff-vfx-probe 2>&1
)
$handoffVfxProbeExitCode = $LASTEXITCODE
$handoffVfxProbe = $null
try {
    $handoffVfxProbe =
        ($handoffVfxProbeOutput -join [Environment]::NewLine) |
            ConvertFrom-Json
}
catch {
    $handoffVfxProbe = $null
}
$handoffVfxProbePassed =
    $handoffVfxProbeExitCode -eq 0 -and
    $null -ne $handoffVfxProbe -and
    $handoffVfxProbe.SchemaVersion -eq 2 -and
    $handoffVfxProbe.Result -eq 'passed' -and
    $handoffVfxProbe.CompositionId -eq
        'handoff-constellation-with-triangle-glow-v2' -and
    $handoffVfxProbe.StaticCommandCount -le 96 -and
    $handoffVfxProbe.MaximumPerFrameCommandCount -le 24 -and
    $handoffVfxProbe.StageCount -eq 4 -and
    $handoffVfxProbe.SignalFixedStepHz -eq 60 -and
    $handoffVfxProbe.RenderSampleHz -eq 30 -and
    $handoffVfxProbe.RetainedScenesCompiled -and
    $handoffVfxProbe.SharedRgbBound -and
    $handoffVfxProbe.FocusPrimitive -eq
        'closed-outline-triangle' -and
    $handoffVfxProbe.PostProcessId -eq
        'bounded-vector-gaussian-glow-v1' -and
    $handoffVfxProbe.GlowRadius -eq 8 -and
    $handoffVfxProbe.MaximumGlowRegionWidth -le 160 -and
    $handoffVfxProbe.MaximumGlowRegionHeight -le 72 -and
    $handoffVfxProbe.BoundedPostProcessRegion -and
    $handoffVfxProbe.VectorCorePreserved -and
    -not $handoffVfxProbe.BitmapAssetsUsed -and
    -not $handoffVfxProbe.ParticlesEnabled -and
    $handoffVfxProbe.PostProcessingEnabled -and
    -not $handoffVfxProbe.ReadyForShellMutation -and
    -not $handoffVfxProbe.ActivationPermitted -and
    $handoffVfxProbe.LiveExplorer -eq 'not-run' -and
    -not $handoffVfxProbe.MutationPerformed -and
    @($handoffVfxProbe.Failures).Count -eq 0
Add-Check `
    -Name 'vfx.executable-retained-handoff-probe' `
    -Passed $handoffVfxProbePassed `
    -Detail (
        'The executable probe must compile every triangular stage/frame scene, ' +
        'bound the glow pass to 160x72, remain within the 96/24 command caps, ' +
        'bind shared RGB, use no bitmap asset, and expose no particle, Shell or ' +
        'activation capability. Output: ' +
        (($handoffVfxProbeOutput | Select-Object -Last 18) -join ' '))

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

$bootstrapProbeOutput = @(
    & $DotnetPath run `
        --project $diagnosticsProjectPath `
        --configuration Release `
        -- `
        --bootstrap-probe 2>&1
)
$bootstrapProbeExitCode = $LASTEXITCODE
$bootstrapProbe = $null
try {
    $bootstrapProbe =
        ($bootstrapProbeOutput -join [Environment]::NewLine) |
            ConvertFrom-Json
}
catch {
    $bootstrapProbe = $null
}
$bootstrapProbePassed =
    $bootstrapProbeExitCode -eq 0 -and
    $null -ne $bootstrapProbe -and
    $bootstrapProbe.Result -eq 'passed' -and
    $bootstrapProbe.PackagedLayoutPassed -and
    $bootstrapProbe.DeveloperLayoutPassed -and
    $bootstrapProbe.PackagedLayoutPrecedencePassed -and
    $bootstrapProbe.TamperedGitRuntimeRejected -and
    $bootstrapProbe.ExtraGitRuntimeFileRejected -and
    $bootstrapProbe.TamperedPackageRejected -and
    $bootstrapProbe.MissingRuntimeRejected -and
    -not $bootstrapProbe.MutationPerformed -and
    @($bootstrapProbe.Failures).Count -eq 0
Add-Check `
    -Name 'runtime.executable-bootstrap-probe' `
    -Passed $bootstrapProbePassed `
    -Detail (
        'The executable bootstrap receipt must prove packaged and developer ' +
        'resolution, packaged precedence, full Git-closure tamper rejection, ' +
        'incomplete-runtime rejection and no mutation. Output: ' +
        (($bootstrapProbeOutput | Select-Object -Last 14) -join ' '))

$piRuntimeDependency = Join-Path $root (
    'src\common\Jarvis.PiAgentHost\node_modules\' +
    '@earendil-works\pi-coding-agent\package.json')
$piRuntimeDependenciesAvailable =
    Test-Path -LiteralPath $piRuntimeDependency -PathType Leaf
$nodeCommand = Get-Command $NodePath -ErrorAction Stop
$resolvedNodePath = [IO.Path]::GetFullPath($nodeCommand.Source)
$sessionLaunchProbeOutput = @(
    & $DotnetPath run `
        --project $diagnosticsProjectPath `
        --configuration Release `
        -- `
        --session-launch-probe `
        --node $resolvedNodePath `
        --workspace $root 2>&1
)
$sessionLaunchProbeExitCode = $LASTEXITCODE
$sessionLaunchProbe = $null
try {
    $sessionLaunchProbe =
        ($sessionLaunchProbeOutput -join [Environment]::NewLine) |
            ConvertFrom-Json
}
catch {
    $sessionLaunchProbe = $null
}
$sessionProviderBoundaryPassed =
    if ($null -eq $sessionLaunchProbe) {
        $false
    }
    elseif ($piRuntimeDependenciesAvailable) {
        $sessionLaunchProbe.LocalLaunchPassed -and
        $sessionLaunchProbe.OpenAiLaunchPassed -and
        -not $sessionLaunchProbe.IncompleteRuntimeRejected
    }
    else {
        -not $sessionLaunchProbe.LocalLaunchPassed -and
        -not $sessionLaunchProbe.OpenAiLaunchPassed -and
        $sessionLaunchProbe.IncompleteRuntimeRejected
    }
$sessionLaunchProbePassed =
    $sessionLaunchProbeExitCode -eq 0 -and
    $null -ne $sessionLaunchProbe -and
    $sessionLaunchProbe.Result -eq 'passed' -and
    $sessionLaunchProbe.WorkspaceAdmissionPassed -and
    $sessionProviderBoundaryPassed -and
    $sessionLaunchProbe.RelativeWorkspaceRejected -and
    $sessionLaunchProbe.MissingWorkspaceRejected -and
    $sessionLaunchProbe.DriveRootRejected -and
    $sessionLaunchProbe.ProtectedWorkspaceRejected -and
    $sessionLaunchProbe.UnknownProviderRejected -and
    -not $sessionLaunchProbe.MutationPerformed -and
    @($sessionLaunchProbe.Failures).Count -eq 0
Add-Check `
    -Name 'runtime.executable-session-launch-admission-probe' `
    -Passed $sessionLaunchProbePassed `
    -Detail (
        'The executable launcher receipt must admit both providers when the ' +
        'fixed runtime is complete, reject an incomplete clean-checkout ' +
        'runtime, and reject invalid workspace/provider inputs without ' +
        'mutation. Output: ' +
        (($sessionLaunchProbeOutput | Select-Object -Last 18) -join ' '))

if ($piRuntimeDependenciesAvailable) {
    $sessionLifecycleProbeOutput = @(
        & $DotnetPath run `
            --project $diagnosticsProjectPath `
            --configuration Release `
            -- `
            --session-launch-lifecycle-probe `
            --node $resolvedNodePath `
            --workspace $root 2>&1
    )
    $sessionLifecycleProbeExitCode = $LASTEXITCODE
    $sessionLifecycleProbe = $null
    try {
        $sessionLifecycleProbe =
            ($sessionLifecycleProbeOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $sessionLifecycleProbe = $null
    }
    $sessionLifecycleProbePassed =
        $sessionLifecycleProbeExitCode -eq 0 -and
        $null -ne $sessionLifecycleProbe -and
        $sessionLifecycleProbe.Result -eq 'passed' -and
        $sessionLifecycleProbe.IdleLaunchAvailable -and
        $sessionLifecycleProbe.RuntimeReady -and
        $sessionLifecycleProbe.RuntimeStopped -and
        $sessionLifecycleProbe.OwnedRuntimeReleased -and
        -not $sessionLifecycleProbe.MutationPerformed -and
        @($sessionLifecycleProbe.Failures).Count -eq 0
    $sessionLifecycleDetail =
        'The executable lifecycle receipt must transition the idle view ' +
        'model through the GUI launch path to a ready local Pi runtime, ' +
        'then stop and release the owned runtime. Output: ' +
        (($sessionLifecycleProbeOutput | Select-Object -Last 14) -join ' ')
}
else {
    $sessionLifecycleProbePassed =
        $sessionAdmissionProbeText.Contains(
            'RunLifecycleAsync') -and
        $sessionAdmissionProbeText.Contains(
            'ConversationSurfaceViewModel.CreateIdle') -and
        $sessionAdmissionProbeText.Contains('viewModel.LaunchAsync') -and
        $sessionAdmissionProbeText.Contains('viewModel.ShutdownAsync')
    $sessionLifecycleDetail =
        'The clean-checkout static boundary is present. The executable ' +
        'lifecycle probe is deferred because optional local Pi node_modules ' +
        'are not installed; release owners run it before packaging.'
}
Add-Check `
    -Name 'runtime.in-process-session-lifecycle-probe' `
    -Passed $sessionLifecycleProbePassed `
    -Detail $sessionLifecycleDetail

$recentSessionStoreProbeOutput = @(
    & $DotnetPath run `
        --project $diagnosticsProjectPath `
        --configuration Release `
        -- `
        --recent-session-store-probe `
        --workspace $root 2>&1
)
$recentSessionStoreProbeExitCode = $LASTEXITCODE
$recentSessionStoreProbe = $null
try {
    $recentSessionStoreProbe =
        ($recentSessionStoreProbeOutput -join [Environment]::NewLine) |
            ConvertFrom-Json
}
catch {
    $recentSessionStoreProbe = $null
}
$recentSessionStoreProbePassed =
    $recentSessionStoreProbeExitCode -eq 0 -and
    $null -ne $recentSessionStoreProbe -and
    $recentSessionStoreProbe.Result -eq 'passed' -and
    $recentSessionStoreProbe.CurrentUserRoundTripPassed -and
    $recentSessionStoreProbe.ProviderAndRecencyPassed -and
    $recentSessionStoreProbe.DuplicateWorkspaceCollapsed -and
    $recentSessionStoreProbe.PlaintextWorkspaceAbsent -and
    $recentSessionStoreProbe.CiphertextTamperRejected -and
    $recentSessionStoreProbe.TemporaryStorageRemoved -and
    -not $recentSessionStoreProbe.MutationPerformed -and
    @($recentSessionStoreProbe.Failures).Count -eq 0
Add-Check `
    -Name 'runtime.encrypted-recent-session-store-probe' `
    -Passed $recentSessionStoreProbePassed `
    -Detail (
        'The executable CurrentUser-DPAPI probe must prove atomic encrypted ' +
        'round-trip, latest-provider replacement, no plaintext path, tamper ' +
        'rejection and temporary cleanup. Output: ' +
        (($recentSessionStoreProbeOutput | Select-Object -Last 16) -join ' '))

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
    portableRuntimeBootstrapImplemented = $true
    productionProviderAvailable = $true
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
