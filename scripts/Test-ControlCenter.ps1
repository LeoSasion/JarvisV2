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
$viewModelPath = Join-Path $sourceRoot 'ConversationSurfaceViewModel.cs'
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
    'Writes: desktop-owner approval only',
    'Shell / direct edit / unattended approval: locked',
    'WORKSPACE EDIT PROPOSAL',
    'Content="REJECT"',
    'Content="APPROVE ONCE"',
    'SHELL // LOCKED',
    'SAFE SHUTDOWN',
    'CONFIGURE OPENAI',
    'START PI SESSION',
    'production model ',
    'authentication is not configured'
)
$visibleSource = @(
    $mainWindowText,
    $viewModelText,
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
        $mainWindowText.Contains('AutomationProperties.Name="Send message"') -and
        $mainWindowText.Contains('AutomationProperties.Name="Cancel active turn"') -and
        $mainWindowText.Contains('ItemsSource="{Binding WorkspaceEdits}"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="Reject workspace edit without writing"') -and
        $mainWindowText.Contains(
            'AutomationProperties.Name="Approve workspace edit once"') -and
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
        $mainWindowText.Contains('Content="CONFIGURE OPENAI"') -and
        $mainWindowCodeText.Contains('ModelSetupWindow') -and
        $modelSetupText.Contains('x:Name="ApiKeyInput"') -and
        $modelSetupText.Contains('PasswordBox') -and
        $modelSetupText.Contains('gpt-5.6-sol') -and
        $modelSetupText.Contains(
            'READ / GREP / FIND / LS / PROPOSE_EDIT') -and
        $modelSetupText.Contains(
            'WRITE // DESKTOP OWNER APPROVAL ONLY') -and
        $modelSetupText.Contains('RETENTION // STORE FALSE') -and
        $modelSetupText.Contains('SIDECAR // OFFLINE') -and
        $modelSetupText.Contains(
            'AutomationProperties.Name="OpenAI API key"') -and
        $modelSetupText.Contains(
            'AutomationProperties.Name="Protect and save OpenAI API key"') -and
        $modelSetupCodeText.Contains(
            'OpenAiApiKeyCredentialStore.ValidateApiKey') -and
        $modelSetupCodeText.Contains(
            'UNREADABLE / REPLACE REQUIRED') -and
        $modelSetupCodeText.Contains('credentialStore.SaveAsync') -and
        $modelSetupCodeText.Contains('ApiKeyInput.Clear()') -and
        $mainWindowCodeText.Contains('replacementRequired = true')) `
    -Detail (
        'Provider setup must be an explicit keyboard-accessible PasswordBox ' +
        'flow that discloses model, tools, retention and sidecar boundaries, ' +
        'then protects the value without showing the previous key.')

Add-Check `
    -Name 'runtime.owned-start-stream-cancel-checkpoint-shutdown' `
    -Passed (
        $viewModelText.Contains('PiAgentDesktopRuntime.StartAsync') -and
        $viewModelText.Contains('PiAgentConversationCheckpointStore') -and
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
            'AutomationProperties.Name="Start Pi workspace session"') -and
        $mainWindowText.Contains('IsEnabled="{Binding CanLaunchSession}"') -and
        $mainWindowCodeText.Contains('SessionLaunchWindow') -and
        $mainWindowCodeText.Contains('ResolveInitialWorkspace') -and
        $mainWindowCodeText.Contains('conversation.LaunchAsync') -and
        $viewModelText.Contains('public async Task LaunchAsync') -and
        $viewModelText.Contains('No command line is required.') -and
        $sessionLaunchText.Contains('Text="Start a workspace session"') -and
        $sessionLaunchText.Contains('x:Name="WorkspaceInput"') -and
        $sessionLaunchText.Contains('x:Name="LocalProviderOption"') -and
        $sessionLaunchText.Contains('x:Name="OpenAiProviderOption"') -and
        $sessionLaunchText.Contains(
            'AutomationProperties.Name="Browse for workspace directory"') -and
        $sessionLaunchText.Contains(
            'AutomationProperties.Name="Admit workspace and start Pi session"') -and
        $sessionLaunchText.Contains('IsDefault="True"') -and
        $sessionLaunchText.Contains('IsEnabled="False"') -and
        $sessionLaunchText.Contains('TOOLS // READ + PROPOSE') -and
        $sessionLaunchText.Contains('WRITES // OWNER REVIEW') -and
        $sessionLaunchText.Contains('SHELL // LOCKED') -and
        $sessionLaunchCodeText.Contains('OpenFolderDialog') -and
        $sessionLaunchCodeText.Contains(
            'DesktopSessionLaunchAdmission.Admit') -and
        $sessionLaunchCodeText.Contains(
            'workspace.Result == "passed"') -and
        $sessionAdmissionText.Contains('RejectsWindowsPathShape') -and
        $sessionAdmissionText.Contains('EnsureNoReparsePoints') -and
        $sessionAdmissionText.Contains('DesktopRuntimeBootstrap.Resolve') -and
        $sessionAdmissionProbeText.Contains('UnknownProviderRejected')) `
    -Detail (
        'The empty state must launch a keyboard-accessible native workspace ' +
        'and provider flow, preflight protected/reparse paths, resolve the ' +
        'portable runtime and transition the existing window in-process.')

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
            'runtime\pi-agent\src\host.mjs') -and
        $bootstrapText.Contains('JARVIS2_NODE_PATH') -and
        $bootstrapText.Contains(
            '@earendil-works\pi-ai\package.json') -and
        $bootstrapText.Contains(
            '@earendil-works\pi-coding-agent\package.json') -and
        $bootstrapText.Contains(
            'pi-agent-desktop-host-contract.json') -and
        $bootstrapText.Contains('ValidatePackageManifest') -and
        $bootstrapText.Contains('foreach ((string relativePath, string expected)') -and
        $bootstrapProbeText.Contains('packaged-layout') -and
        $bootstrapProbeText.Contains('developer-layout') -and
        $viewModelText.Contains('OpenAiResponsesModelProvider') -and
        $viewModelText.Contains('OpenAiApiKeyCredentialStore') -and
        $viewModelText.Contains('providerCredentialReady')) `
    -Detail (
        'The ordinary desktop process must resolve a complete packaged ' +
        'Node/Pi layout before developer fallback and expose production ' +
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
    $bootstrapProbe.TamperedPackageRejected -and
    $bootstrapProbe.MissingRuntimeRejected -and
    -not $bootstrapProbe.MutationPerformed -and
    @($bootstrapProbe.Failures).Count -eq 0
Add-Check `
    -Name 'runtime.executable-bootstrap-probe' `
    -Passed $bootstrapProbePassed `
    -Detail (
        'The executable bootstrap receipt must prove packaged and developer ' +
        'resolution, packaged precedence, incomplete-runtime rejection and ' +
        'no mutation. Output: ' +
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
