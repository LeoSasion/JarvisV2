[CmdletBinding()]
param(
    [switch]$StaticOnly,
    [string]$NodePath = 'node',
    [string]$DotnetPath = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src\common\Jarvis.PiAgentHost'
$contractPath = Join-Path $root (
    'config\pi-agent-desktop-host-contract.json')
$schemaPath = Join-Path $root (
    'config\pi-agent-desktop-host-contract.schema.json')
$packagePath = Join-Path $sourceRoot 'package.json'
$lockPath = Join-Path $sourceRoot 'pnpm-lock.yaml'
$hostPath = Join-Path $sourceRoot 'src\host.mjs'
$protocolTestPath = Join-Path $sourceRoot 'test\protocol.test.mjs'
$brokerTestPath = Join-Path $sourceRoot (
    'test\desktop-model-broker.test.mjs')
$bridgeProjectPath = Join-Path $sourceRoot 'Jarvis.PiAgentHost.csproj'
$bridgeSourcePath = Join-Path $sourceRoot 'DesktopBridge.cs'
$productionBrokerSourcePath = Join-Path $sourceRoot 'DesktopModelBroker.cs'
$brokerSourcePath = Join-Path $sourceRoot 'DiagnosticModelBroker.cs'
$conversationSourcePath = Join-Path $sourceRoot 'ConversationState.cs'
$conversationProbeSourcePath = Join-Path $sourceRoot (
    'DiagnosticConversation.cs')
$desktopRuntimeSourcePath = Join-Path $sourceRoot 'DesktopRuntime.cs'
$desktopRuntimeProbeSourcePath = Join-Path $sourceRoot (
    'DiagnosticDesktopRuntime.cs')
$bridgeProgramPath = Join-Path $sourceRoot 'Program.cs'
$bridgeFixtureRoot = Join-Path $sourceRoot 'test\fixtures'
$controlCenterRoot = Join-Path $root (
    'src\common\Jarvis.ControlCenter')
$controlCenterProjectPath = Join-Path $controlCenterRoot (
    'Jarvis.ControlCenter.csproj')
$controlCenterBindingPath = Join-Path $controlCenterRoot (
    'PiAgentConversationBinding.cs')

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

$contract =
    Get-Content -LiteralPath $contractPath -Raw |
        ConvertFrom-Json
$schema =
    Get-Content -LiteralPath $schemaPath -Raw |
        ConvertFrom-Json
$package =
    Get-Content -LiteralPath $packagePath -Raw |
        ConvertFrom-Json
$runtimeSourceText = @(
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'src') `
        -File `
        -Filter '*.mjs' |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine

Add-Check `
    -Name 'contract.official-exact-upstream' `
    -Passed (
        $contract.schemaVersion -eq 1 -and
        $contract.contractId -eq
            'jarvisv2-pi-agent-desktop-host-v1' -and
        $contract.upstream.package -eq
            '@earendil-works/pi-coding-agent' -and
        $contract.upstream.exactVersion -eq '0.82.1' -and
        $contract.upstream.repository -eq
            'https://github.com/earendil-works/pi' -and
        $contract.upstream.license -eq 'MIT' -and
        $package.dependencies.'@earendil-works/pi-ai' -eq '0.82.1' -and
        $package.dependencies.'@earendil-works/pi-coding-agent' -eq
            '0.82.1') `
    -Detail (
        'The sidecar must pin the reviewed official Pi package exactly, ' +
        'without a floating range.')

Add-Check `
    -Name 'contract.read-only-session-and-tools' `
    -Passed (
        $contract.runtime.sessionCreationEnabled -and
        $contract.runtime.desktopLaunchImplemented -and
        $contract.runtime.launchState -eq
            'read-only-session-admission' -and
        $contract.session.enabled -and
        $contract.session.promptingEnabled -eq
            'desktop-broker-required' -and
        $contract.session.modelAuthentication -eq
            'desktop-process-only' -and
        $contract.session.modelTransport -eq
            'local-named-pipe' -and
        $contract.session.modelBrokerProtocol -eq
            'jarvisv2-pi-model-broker-v1' -and
        $contract.session.modelBrokerLifetime -eq
            'desktop-owned-multi-request' -and
        $contract.session.modelBrokerMaxFrameBytes -eq 1048576 -and
        $contract.session.modelBrokerMaxConcurrentConnections -eq 4 -and
        $contract.session.desktopTurnEventStream -eq
            'bounded-ordered-single-consumer' -and
        $contract.session.desktopTurnEventBufferCapacity -eq 512 -and
        $contract.session.desktopTurnEventBackpressurePolicy -eq
            'fail-closed-at-request-timeout' -and
        $contract.session.desktopConversationStateModel -eq
            'immutable-revisioned-single-active-turn' -and
        $contract.session.desktopConversationRetainedTurns -eq 128 -and
        $contract.session.desktopConversationMaxAssistantCharacters -eq
            262144 -and
        $contract.session.desktopConversationNotificationDispatch -eq
            'captured-synchronization-context' -and
        $contract.session.desktopConversationCheckpoint -eq
            'bounded-completed-text-context-restore' -and
        $contract.session.desktopConversationCheckpointMaxTurns -eq 32 -and
        $contract.session.desktopConversationCheckpointMaxBytes -eq
            32768 -and
        $contract.session.desktopConversationCheckpointMaxTextBytes -eq
            16384 -and
        $contract.session.desktopConversationCheckpointPersistence -eq
            'desktop-owned-external' -and
        $contract.session.desktopRuntimeOwnership -eq
            'desktop-owned-broker-sidecar-session-conversation' -and
        $contract.session.desktopRuntimeShutdown -eq
            'quiesce-cancel-sidecar-shutdown-broker-dispose' -and
        $contract.session.credentialTransport -eq 'forbidden' -and
        $contract.session.persistence -eq 'in-memory' -and
        $contract.session.workspaceBinding -eq
            'single-explicit-root' -and
        $contract.session.resourceDiscovery -eq 'disabled' -and
        -not $contract.session.modelNetworkAllowed -and
        (@($contract.tools.initialAllowlist) -join '|') -eq
            'read|grep|find|ls' -and
        (@($contract.tools.initiallyDenied) -join '|') -eq
            'bash|edit|write' -and
        -not $contract.tools.unattendedSelfIteration) `
    -Detail (
        'The managed desktop may create one real in-memory Pi session for ' +
        'one admitted workspace; prompting requires a desktop-owned named ' +
        'pipe while credentials, discovery and mutation tools remain denied.')

Add-Check `
    -Name 'contract.jsonl-and-shell-boundary' `
    -Passed (
        $contract.runtime.integrationMode -eq 'sdk-sidecar-jsonl' -and
        $contract.runtime.piOfflineRequired -and
        $contract.transport.framing -eq 'lf-delimited-jsonl' -and
        $contract.transport.maxFrameBytes -eq 65536 -and
        -not $contract.transport.credentialFieldsAllowed -and
        -not $contract.boundaries.shellMutationSupported -and
        -not $contract.boundaries.explorerMutationSupported -and
        -not $contract.boundaries.systemMutationSupported -and
        -not $contract.boundaries.activationPermitted -and
        $contract.boundaries.liveExplorer -eq 'not-run') `
    -Detail (
        'The language-neutral boundary uses bounded LF-delimited JSONL and ' +
        'cannot mutate the Shell, Explorer or system.')

Add-Check `
    -Name 'schema.fixed-safety-values' `
    -Passed (
        $schema.'$schema' -eq
            'https://json-schema.org/draft/2020-12/schema' -and
        $schema.title -eq
            'JarvisV2 Pi Agent desktop host contract' -and
        $schema.properties.upstream.properties.exactVersion.const -eq
            '0.82.1' -and
        $schema.properties.runtime.properties.nodeMinimumMajor.const -eq
            22 -and
        $schema.properties.runtime.properties.launchState.const -eq
            'read-only-session-admission' -and
        $schema.properties.runtime.properties.desktopLaunchImplemented.const `
            -eq $true -and
        $schema.properties.transport.properties.maxFrameBytes.const -eq
            65536 -and
        $schema.properties.runtime.properties.sessionCreationEnabled.const `
            -eq $true -and
        $schema.properties.session.properties.promptingEnabled.const `
            -eq 'desktop-broker-required' -and
        $schema.properties.session.properties.modelAuthentication.const `
            -eq 'desktop-process-only' -and
        $schema.properties.session.properties.modelTransport.const `
            -eq 'local-named-pipe' -and
        $schema.properties.session.properties.modelBrokerProtocol.const `
            -eq 'jarvisv2-pi-model-broker-v1' -and
        $schema.properties.session.properties.modelBrokerLifetime.const `
            -eq 'desktop-owned-multi-request' -and
        $schema.properties.session.properties.modelBrokerMaxFrameBytes.const `
            -eq 1048576 -and
        $schema.properties.session.properties.modelBrokerMaxConcurrentConnections.const `
            -eq 4 -and
        $schema.properties.session.properties.desktopTurnEventStream.const `
            -eq 'bounded-ordered-single-consumer' -and
        $schema.properties.session.properties.desktopTurnEventBufferCapacity.const `
            -eq 512 -and
        $schema.properties.session.properties.desktopTurnEventBackpressurePolicy.const `
            -eq 'fail-closed-at-request-timeout' -and
        $schema.properties.session.properties.desktopConversationStateModel.const `
            -eq 'immutable-revisioned-single-active-turn' -and
        $schema.properties.session.properties.desktopConversationRetainedTurns.const `
            -eq 128 -and
        $schema.properties.session.properties.desktopConversationMaxAssistantCharacters.const `
            -eq 262144 -and
        $schema.properties.session.properties.desktopConversationNotificationDispatch.const `
            -eq 'captured-synchronization-context' -and
        $schema.properties.session.properties.desktopConversationCheckpoint.const `
            -eq 'bounded-completed-text-context-restore' -and
        $schema.properties.session.properties.desktopConversationCheckpointMaxTurns.const `
            -eq 32 -and
        $schema.properties.session.properties.desktopConversationCheckpointMaxBytes.const `
            -eq 32768 -and
        $schema.properties.session.properties.desktopConversationCheckpointMaxTextBytes.const `
            -eq 16384 -and
        $schema.properties.session.properties.desktopConversationCheckpointPersistence.const `
            -eq 'desktop-owned-external' -and
        $schema.properties.session.properties.desktopRuntimeOwnership.const `
            -eq 'desktop-owned-broker-sidecar-session-conversation' -and
        $schema.properties.session.properties.desktopRuntimeShutdown.const `
            -eq 'quiesce-cancel-sidecar-shutdown-broker-dispose' -and
        $schema.properties.session.properties.modelNetworkAllowed.const `
            -eq $false -and
        $schema.properties.transport.properties.credentialFieldsAllowed.const `
            -eq $false -and
        $schema.properties.boundaries.properties.activationPermitted.const `
            -eq $false) `
    -Detail (
        'The published schema must hard-code the single-root in-memory ' +
        'session plus desktop-broker-only prompting and disabled credential ' +
        'transport, sidecar network and activation.')

$forbiddenRuntimePattern = (
    '(?i)\b(?:child_process|spawn|execFile|execSync|shell\s*:|' +
    'ANTHROPIC_API_KEY|OPENAI_API_KEY|auth\.json|' +
    'writeFile|appendFile|rmSync|unlinkSync)\b'
)
$sessionCreateCount = [regex]::Matches(
    $runtimeSourceText,
    'createAgentSession\s*\('
).Count
$modelRuntimeCreateCount = [regex]::Matches(
    $runtimeSourceText,
    'ModelRuntime\.create\s*\('
).Count
$sessionPromptCount = [regex]::Matches(
    $runtimeSourceText,
    '\.prompt\s*\('
).Count
Add-Check `
    -Name 'source.root-confined-session-sidecar' `
    -Passed (
        -not [regex]::IsMatch(
            $runtimeSourceText,
            $forbiddenRuntimePattern) -and
        $sessionCreateCount -eq 1 -and
        $modelRuntimeCreateCount -eq 1 -and
        $sessionPromptCount -eq 1 -and
        $runtimeSourceText.Contains('process.env.PI_OFFLINE = "1"') -and
        $runtimeSourceText.Contains(
            'allowModelNetwork: false') -and
        $runtimeSourceText.Contains(
            'SessionManager.inMemory') -and
        $runtimeSourceText.Contains(
            'SettingsManager.inMemory') -and
        $runtimeSourceText.Contains(
            'single-explicit-root') -and
        $runtimeSourceText.Contains(
            'path-outside-workspace') -and
        $runtimeSourceText.Contains(
            'reparse-point-forbidden') -and
        $runtimeSourceText.Contains('JARVIS_MODEL_BROKER_PIPE') -and
        $runtimeSourceText.Contains('jarvisv2-pi-model-broker-v1') -and
        $runtimeSourceText.Contains('desktop-broker-capability') -and
        $runtimeSourceText.Contains('Buffer.byteLength') -and
        $runtimeSourceText.Contains('Buffer.byteLength(line, "utf8")') -and
        $runtimeSourceText.Contains('buffer.indexOf("\n")') -and
        $runtimeSourceText.Contains('credential-field-forbidden')) `
    -Detail (
        'Runtime source must create exactly one root-confined in-memory SDK ' +
        'session and one broker-gated prompt path without credential files, ' +
        'writes or child processes.')

$bridgeSourceText =
    [IO.File]::ReadAllText($bridgeSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($productionBrokerSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($brokerSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($conversationSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($conversationProbeSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($desktopRuntimeSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($desktopRuntimeProbeSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($bridgeProgramPath)
$forbiddenBridgePattern = (
    '(?i)\b(?:DllImport|LibraryImport|OpenProcess|CreateRemoteThread|' +
    'WriteProcessMemory|SetWindowsHookEx|Microsoft\.Win32\.Registry|' +
    'ServiceController|HttpClient|ClientWebSocket|TcpClient|UdpClient|' +
    'CreateAgentSession\s*\(|ModelRuntime\.create\s*\()\b'
)
Add-Check `
    -Name 'desktop-bridge.owned-process-fail-closed' `
    -Passed (
        (Test-Path -LiteralPath $bridgeProjectPath -PathType Leaf) -and
        -not [regex]::IsMatch(
            $bridgeSourceText,
            $forbiddenBridgePattern) -and
        $bridgeSourceText.Contains('UseShellExecute = false') -and
        $bridgeSourceText.Contains('CreateNoWindow = true') -and
        $bridgeSourceText.Contains('RedirectStandardInput = true') -and
        $bridgeSourceText.Contains('RedirectStandardOutput = true') -and
        $bridgeSourceText.Contains('RedirectStandardError = true') -and
        $bridgeSourceText.Contains(
            'startInfo.Environment["PI_OFFLINE"] = "1"') -and
        $bridgeSourceText.Contains(
            'startInfo.Environment.Clear()') -and
        $bridgeSourceText.Contains(
            'startInfo.Environment["JARVIS_MODEL_BROKER_PIPE"]') -and
        $bridgeSourceText.Contains(
            'process.Kill(entireProcessTree: true)') -and
        $bridgeSourceText.Contains('StartReadOnlySessionAsync') -and
        $bridgeSourceText.Contains('PromptAsync') -and
        $bridgeSourceText.Contains('StartTurnAsync') -and
        $bridgeSourceText.Contains('AbortTurnAsync') -and
        $bridgeSourceText.Contains('PumpOutputAsync') -and
        $bridgeSourceText.Contains('ReadEventsAsync') -and
        $bridgeSourceText.Contains('Channel.CreateBounded') -and
        $bridgeSourceText.Contains(
            'TurnEventBufferCapacity = 512') -and
        $bridgeSourceText.Contains(
            'backpressure deadline') -and
        $bridgeSourceText.Contains('PiAgentConversationState') -and
        $bridgeSourceText.Contains(
            'MaximumRetainedTurns = 128') -and
        $bridgeSourceText.Contains(
            'MaximumAssistantCharacters = 262_144') -and
        $bridgeSourceText.Contains(
            'MaximumCheckpointTurns = 32') -and
        $bridgeSourceText.Contains(
            'MaximumCheckpointBytes = 32_768') -and
        $bridgeSourceText.Contains(
            'MaximumCheckpointTextBytes = 16_384') -and
        $bridgeSourceText.Contains('ExportCheckpoint') -and
        $bridgeSourceText.Contains('conversationCheckpoint') -and
        $bridgeSourceText.Contains(
            'PiAgentConversationSnapshot') -and
        $bridgeSourceText.Contains('SynchronizationContext') -and
        $bridgeSourceText.Contains('CancelActiveTurnAsync') -and
        $bridgeSourceText.Contains('QuiesceAsync') -and
        $bridgeSourceText.Contains('PiAgentDesktopRuntime') -and
        $bridgeSourceText.Contains(
            'desktop-owned-broker-sidecar-session-conversation') -and
        $bridgeSourceText.Contains(
            'quiesce-cancel-sidecar-shutdown-broker-dispose') -and
        $bridgeSourceText.Contains('DesktopModelBrokerServer') -and
        $bridgeSourceText.Contains('IDesktopModelProvider') -and
        $bridgeSourceText.Contains(
            'MaximumConcurrentConnections = 4') -and
        $bridgeSourceText.Contains(
            'AllowedToolNames.Contains') -and
        $bridgeSourceText.Contains('PipeOptions.CurrentUserOnly') -and
        $bridgeSourceText.Contains('jarvisv2-pi-model-broker-v1') -and
        $bridgeSourceText.Contains('sessionCreationPassed') -and
        $bridgeSourceText.Contains('workspaceBound') -and
        $bridgeSourceText.Contains('"shutdown"') -and
        $bridgeSourceText.Contains('wrong-ready-rejected') -and
        $bridgeSourceText.Contains('oversized-ready-rejected') -and
        $bridgeSourceText.Contains('hung-ready-times-out') -and
        (Test-Path -LiteralPath (
            Join-Path $bridgeFixtureRoot 'wrong-ready\host.mjs'
        ) -PathType Leaf) -and
        (Test-Path -LiteralPath (
            Join-Path $bridgeFixtureRoot 'oversized-ready\host.mjs'
        ) -PathType Leaf) -and
        (Test-Path -LiteralPath (
            Join-Path $bridgeFixtureRoot 'hung-ready\host.mjs'
        ) -PathType Leaf)) `
    -Detail (
        'The managed bridge may own only the exact no-shell Node child, ' +
        'scrub credential variables, bind one read-only session, admit one ' +
        'multi-request current-user model pipe and terminate only its owned ' +
        'processes and connections on cleanup.')

$controlCenterProjectText =
    [IO.File]::ReadAllText($controlCenterProjectPath)
$controlCenterBindingText =
    [IO.File]::ReadAllText($controlCenterBindingPath)
Add-Check `
    -Name 'desktop-conversation.nonvisual-wpf-binding' `
    -Passed (
        $controlCenterProjectText.Contains(
            '..\Jarvis.PiAgentHost\Jarvis.PiAgentHost.csproj') -and
        $controlCenterBindingText.Contains(
            'INotifyPropertyChanged') -and
        $controlCenterBindingText.Contains(
            'PiAgentConversationSnapshot') -and
        $controlCenterBindingText.Contains(
            'SubmitAsync') -and
        $controlCenterBindingText.Contains(
            'CancelAsync') -and
        -not $controlCenterBindingText.Contains('System.Windows') -and
        -not $controlCenterBindingText.Contains('Process.') -and
        -not $controlCenterBindingText.Contains('Registry')) `
    -Detail (
        'Control Center may reference the reviewed Pi host and compile a ' +
        'property-change adapter without changing XAML or owning transport.')

$bridgeBuildOutput = @(
    & $DotnetPath build `
        $bridgeProjectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$bridgeBuildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'desktop-bridge.release-build' `
    -Passed ($bridgeBuildExitCode -eq 0) `
    -Detail (
        ($bridgeBuildOutput | Select-Object -Last 8) -join
            [Environment]::NewLine)

$controlCenterBuildOutput = @(
    & $DotnetPath build `
        $controlCenterProjectPath `
        --configuration Release `
        --nologo `
        --warnaserror 2>&1
)
$controlCenterBuildExitCode = $LASTEXITCODE
Add-Check `
    -Name 'desktop-conversation.control-center-release-build' `
    -Passed ($controlCenterBuildExitCode -eq 0) `
    -Detail (
        ($controlCenterBuildOutput | Select-Object -Last 10) -join
            [Environment]::NewLine)

$lockValid = $false
if (Test-Path -LiteralPath $lockPath -PathType Leaf) {
    $lockText = [IO.File]::ReadAllText($lockPath)
    $lockValid =
        $lockText.Contains(
            "'@earendil-works/pi-ai@0.82.1'") -and
        $lockText.Contains(
            "'@earendil-works/pi-coding-agent@0.82.1'") -and
        $lockText.Contains('integrity:')
}
Add-Check `
    -Name 'dependency.frozen-lock' `
    -Passed $lockValid `
    -Detail (
        'pnpm-lock.yaml must pin Pi 0.82.1 and retain registry integrity ' +
        'hashes; lifecycle scripts are not required.')

if (-not $StaticOnly) {
    $nodeVersionOutput = @(& $NodePath --version 2>&1)
    $nodeExitCode = $LASTEXITCODE
    $nodeVersion = $null
    if ($nodeExitCode -eq 0 -and
        ($nodeVersionOutput -join '') -match '^v(?<major>\d+)') {
        $nodeVersion = [int]$Matches['major']
    }
    Add-Check `
        -Name 'runtime.node-version' `
        -Passed ($nodeVersion -ge $contract.runtime.nodeMinimumMajor) `
        -Detail (
            "Node exit $nodeExitCode; version " +
            "$($nodeVersionOutput -join '').")

    $inspectOutput = @(
        & $NodePath $hostPath inspect 2>&1
    )
    $inspectExitCode = $LASTEXITCODE
    $inspectReceipt = $null
    try {
        $inspectReceipt =
            ($inspectOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $inspectReceipt = $null
    }
    $inspectResult = if ($null -ne $inspectReceipt) {
        $inspectReceipt.result
    }
    else {
        'unparsed'
    }
    $installedVersion = if ($null -ne $inspectReceipt) {
        $inspectReceipt.installedVersion
    }
    else {
        'unknown'
    }
    Add-Check `
        -Name 'runtime.embedded-sdk-inspection' `
        -Passed (
            $inspectExitCode -eq 0 -and
            $null -ne $inspectReceipt -and
            $inspectReceipt.result -eq
                'passed-embedded-dependency' -and
            $inspectReceipt.installedVersion -eq '0.82.1' -and
            @($inspectReceipt.missingExports).Count -eq 0 -and
            $inspectReceipt.piOffline -and
            $inspectReceipt.transportReady -and
            $inspectReceipt.desktopLaunchImplemented -and
            $inspectReceipt.sessionCreationEnabled -and
            -not $inspectReceipt.promptingEnabled -and
            $inspectReceipt.sessionPersistence -eq 'in-memory' -and
            -not $inspectReceipt.modelNetworkAllowed -and
            -not $inspectReceipt.resourceDiscoveryEnabled -and
            -not $inspectReceipt.credentialTransportAllowed -and
            -not $inspectReceipt.shellMutationSupported -and
            -not $inspectReceipt.explorerMutationSupported -and
            -not $inspectReceipt.activationPermitted -and
            $inspectReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Inspect exit $inspectExitCode; result " +
            "$inspectResult; installed $installedVersion.")

    $protocolOutput = @(
        & $NodePath $protocolTestPath 2>&1
    )
    $protocolExitCode = $LASTEXITCODE
    $protocolReceipt = $null
    try {
        $protocolReceipt =
            ($protocolOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $protocolReceipt = $null
    }
    $protocolResult = if ($null -ne $protocolReceipt) {
        $protocolReceipt.result
    }
    else {
        'unparsed'
    }
    $recordCount = if ($null -ne $protocolReceipt) {
        $protocolReceipt.recordCount
    }
    else {
        0
    }
    Add-Check `
        -Name 'runtime.jsonl-policy-probe' `
        -Passed (
            $protocolExitCode -eq 0 -and
            $null -ne $protocolReceipt -and
            $protocolReceipt.result -eq 'passed' -and
            $protocolReceipt.recordCount -eq 7 -and
            $protocolReceipt.framing -eq 'lf-delimited-jsonl' -and
            $protocolReceipt.credentialFieldsRejected -and
            $protocolReceipt.credentialEnvironmentClean -and
            $protocolReceipt.batchedFramesAccepted -eq 81 -and
            $protocolReceipt.oversizedFrameRejected -and
            $protocolReceipt.sessionCreationEnabled -and
            -not $protocolReceipt.promptingEnabled -and
            $protocolReceipt.sessionPersistence -eq 'in-memory' -and
            $protocolReceipt.workspaceBinding -eq
                'single-explicit-root' -and
            $protocolReceipt.protectedRootRejected -and
            $protocolReceipt.workspaceEscapeRejected -and
            $protocolReceipt.reparsePointRejected -and
            $protocolReceipt.repeatedBindingRejected -and
            -not $protocolReceipt.modelNetworkAllowed -and
            -not $protocolReceipt.resourceDiscoveryEnabled -and
            -not $protocolReceipt.credentialTransportAllowed -and
            (@($protocolReceipt.initialTools) -join '|') -eq
                'read|grep|find|ls' -and
            -not $protocolReceipt.shellMutationSupported -and
            -not $protocolReceipt.explorerMutationSupported -and
            -not $protocolReceipt.activationPermitted -and
            $protocolReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Protocol exit $protocolExitCode; result " +
            "$protocolResult; records $recordCount.")

    $brokerTestOutput = @(
        & $NodePath $brokerTestPath 2>&1
    )
    $brokerTestExitCode = $LASTEXITCODE
    $brokerTestReceipt = $null
    try {
        $brokerTestReceipt =
            ($brokerTestOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $brokerTestReceipt = $null
    }
    $brokerTestResult = if ($null -ne $brokerTestReceipt) {
        $brokerTestReceipt.result
    }
    else {
        'unparsed'
    }
    Add-Check `
        -Name 'runtime.pi-desktop-model-broker-probe' `
        -Passed (
            $brokerTestExitCode -eq 0 -and
            $null -ne $brokerTestReceipt -and
            $brokerTestReceipt.result -eq 'passed' -and
            $brokerTestReceipt.protocol -eq
                'jarvisv2-pi-model-broker-v1' -and
            $brokerTestReceipt.provider -eq
                'jarvis-desktop-broker' -and
            $brokerTestReceipt.model -eq 'desktop-default' -and
            $brokerTestReceipt.namedPipeOnly -and
            -not $brokerTestReceipt.credentialTransportAllowed -and
            $brokerTestReceipt.promptingEnabled -and
            $brokerTestReceipt.deltaCount -eq 2 -and
            $brokerTestReceipt.response -eq 'JARVIS broker online.' -and
            $brokerTestReceipt.faultScenarioCount -eq 5 -and
            $brokerTestReceipt.invalidPipeRejected -and
            $brokerTestReceipt.wrongProtocolRejected -and
            $brokerTestReceipt.disconnectRejected -and
            $brokerTestReceipt.oversizedFrameRejected -and
            $brokerTestReceipt.activeTurnAbortPassed -and
            $brokerTestReceipt.liveModelNetwork -eq 'not-run' -and
            $brokerTestReceipt.liveExplorer -eq 'not-run' -and
            -not $brokerTestReceipt.mutationPerformed) `
        -Detail (
            "Node broker exit $brokerTestExitCode; result " +
            "$brokerTestResult.")

    $bridgeOutput = @(
        & $DotnetPath run `
            --project $bridgeProjectPath `
            --configuration Release `
            --no-build `
            -- `
            probe `
            --node $NodePath `
            --sidecar $hostPath 2>&1
    )
    $bridgeExitCode = $LASTEXITCODE
    $bridgeReceipt = $null
    try {
        $bridgeReceipt =
            ($bridgeOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $bridgeReceipt = $null
    }
    $bridgeResult = if ($null -ne $bridgeReceipt) {
        $bridgeReceipt.result
    }
    else {
        'unparsed'
    }
    Add-Check `
        -Name 'runtime.desktop-owned-transport-probe' `
        -Passed (
            $bridgeExitCode -eq 0 -and
            $null -ne $bridgeReceipt -and
            $bridgeReceipt.result -eq 'passed' -and
            $bridgeReceipt.protocol -eq
                'jarvisv2-pi-agent-desktop-host-v1' -and
            $bridgeReceipt.package -eq
                '@earendil-works/pi-coding-agent' -and
            $bridgeReceipt.installedVersion -eq '0.82.1' -and
            $bridgeReceipt.desktopLaunchImplemented -and
            $bridgeReceipt.readyObserved -and
            $bridgeReceipt.helloPassed -and
            $bridgeReceipt.capabilitiesPassed -and
            $bridgeReceipt.sessionCreationPassed -and
            $bridgeReceipt.workspaceBound -and
            $bridgeReceipt.shutdownPassed -and
            $bridgeReceipt.piOffline -and
            $bridgeReceipt.credentialEnvironmentScrubbed -and
            (@($bridgeReceipt.initialTools) -join '|') -eq
                'read|grep|find|ls' -and
            (@($bridgeReceipt.deniedTools) -join '|') -eq
                'bash|edit|write' -and
            $bridgeReceipt.sessionCreationEnabled -and
            -not $bridgeReceipt.promptingEnabled -and
            -not $bridgeReceipt.sessionPersisted -and
            -not $bridgeReceipt.credentialTransportAllowed -and
            -not $bridgeReceipt.shellMutationSupported -and
            -not $bridgeReceipt.explorerMutationSupported -and
            -not $bridgeReceipt.systemMutationSupported -and
            -not $bridgeReceipt.activationPermitted -and
            $bridgeReceipt.liveExplorer -eq 'not-run' -and
            -not $bridgeReceipt.mutationPerformed) `
        -Detail (
            "Desktop bridge exit $bridgeExitCode; result $bridgeResult.")

    $brokerBridgeOutput = @(
        & $DotnetPath run `
            --project $bridgeProjectPath `
            --configuration Release `
            --no-build `
            -- `
            broker-probe `
            --node $NodePath `
            --sidecar $hostPath 2>&1
    )
    $brokerBridgeExitCode = $LASTEXITCODE
    $brokerBridgeReceipt = $null
    try {
        $brokerBridgeReceipt =
            ($brokerBridgeOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $brokerBridgeReceipt = $null
    }
    $brokerBridgeResult = if ($null -ne $brokerBridgeReceipt) {
        $brokerBridgeReceipt.result
    }
    else {
        'unparsed'
    }
    Add-Check `
        -Name 'runtime.desktop-owned-model-broker-probe' `
        -Passed (
            $brokerBridgeExitCode -eq 0 -and
            $null -ne $brokerBridgeReceipt -and
            $brokerBridgeReceipt.result -eq 'passed' -and
            $brokerBridgeReceipt.protocol -eq
                'jarvisv2-pi-model-broker-v1' -and
            $brokerBridgeReceipt.readyObserved -and
            $brokerBridgeReceipt.capabilitiesPassed -and
            $brokerBridgeReceipt.sessionCreationPassed -and
            $brokerBridgeReceipt.promptPassed -and
            $brokerBridgeReceipt.multiTurnPassed -and
            $brokerBridgeReceipt.toolRoundTripPassed -and
            $brokerBridgeReceipt.eventStreamPassed -and
            $brokerBridgeReceipt.orderedEventSequence -and
            $brokerBridgeReceipt.singleEventConsumerEnforced -and
            $brokerBridgeReceipt.toolTurnStreamEventCount -eq 4 -and
            $brokerBridgeReceipt.toolExecutionCount -eq 1 -and
            $brokerBridgeReceipt.completedTurnCount -eq 3 -and
            $brokerBridgeReceipt.abortPassed -and
            $brokerBridgeReceipt.abortStatus -eq 'aborted' -and
            $brokerBridgeReceipt.abortStreamPassed -and
            $brokerBridgeReceipt.invalidToolRejected -and
            $brokerBridgeReceipt.providerFaultCount -eq 1 -and
            $brokerBridgeReceipt.concurrentResponsePump -and
            $brokerBridgeReceipt.response -eq
                'JARVIS desktop broker online.' -and
            $brokerBridgeReceipt.deltaCount -eq 2 -and
            $brokerBridgeReceipt.brokerRequestCount -eq 5 -and
            $brokerBridgeReceipt.brokerFaultCount -eq 0 -and
            $brokerBridgeReceipt.namedPipeOnly -and
            -not $brokerBridgeReceipt.credentialTransportAllowed -and
            -not $brokerBridgeReceipt.piSidecarModelNetworkAllowed -and
            $brokerBridgeReceipt.liveModelNetwork -eq 'diagnostic-only' -and
            $brokerBridgeReceipt.liveExplorer -eq 'not-run' -and
            -not $brokerBridgeReceipt.mutationPerformed) `
        -Detail (
            "Desktop broker exit $brokerBridgeExitCode; result " +
            "$brokerBridgeResult.")

    $conversationOutput = @(
        & $DotnetPath run `
            --project $bridgeProjectPath `
            --configuration Release `
            --no-build `
            -- `
            conversation-probe `
            --node $NodePath `
            --sidecar $hostPath 2>&1
    )
    $conversationExitCode = $LASTEXITCODE
    $conversationReceipt = $null
    try {
        $conversationReceipt =
            ($conversationOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $conversationReceipt = $null
    }
    $conversationResult = if ($null -ne $conversationReceipt) {
        $conversationReceipt.result
    }
    else {
        'unparsed'
    }
    Add-Check `
        -Name 'runtime.desktop-conversation-state-probe' `
        -Passed (
            $conversationExitCode -eq 0 -and
            $null -ne $conversationReceipt -and
            $conversationReceipt.result -eq 'passed' -and
            $conversationReceipt.normalTurnPassed -and
            $conversationReceipt.deltaSnapshotsObserved -and
            $conversationReceipt.revisionOrderPassed -and
            $conversationReceipt.toolLifecyclePassed -and
            $conversationReceipt.singleActiveTurnEnforced -and
            $conversationReceipt.cancelRequestObserved -and
            $conversationReceipt.abortTurnPassed -and
            $conversationReceipt.notificationContextUsed -and
            $conversationReceipt.retainedTurnLimit -eq 128 -and
            $conversationReceipt.assistantCharacterLimit -eq 262144 -and
            $conversationReceipt.completedTurnCount -eq 3 -and
            $conversationReceipt.observedSnapshotCount -ge 16 -and
            $conversationReceipt.canSubmitAfterTerminal -and
            -not $conversationReceipt.canCancelAfterTerminal -and
            -not $conversationReceipt.credentialTransportAllowed -and
            -not $conversationReceipt.piSidecarModelNetworkAllowed -and
            $conversationReceipt.liveModelNetwork -eq 'diagnostic-only' -and
            $conversationReceipt.liveExplorer -eq 'not-run' -and
            -not $conversationReceipt.mutationPerformed) `
        -Detail (
            "Desktop conversation exit $conversationExitCode; result " +
            "$conversationResult.")

    $desktopRuntimeOutput = @(
        & $DotnetPath run `
            --project $bridgeProjectPath `
            --configuration Release `
            --no-build `
            -- `
            runtime-probe `
            --node $NodePath `
            --sidecar $hostPath 2>&1
    )
    $desktopRuntimeExitCode = $LASTEXITCODE
    $desktopRuntimeReceipt = $null
    try {
        $desktopRuntimeReceipt =
            ($desktopRuntimeOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $desktopRuntimeReceipt = $null
    }
    $desktopRuntimeResult = if ($null -ne $desktopRuntimeReceipt) {
        $desktopRuntimeReceipt.result
    }
    else {
        'unparsed'
    }
    Add-Check `
        -Name 'runtime.desktop-owned-lifecycle-probe' `
        -Passed (
            $desktopRuntimeExitCode -eq 0 -and
            $null -ne $desktopRuntimeReceipt -and
            $desktopRuntimeReceipt.result -eq 'passed' -and
            $desktopRuntimeReceipt.ownershipModel -eq
                'desktop-owned-broker-sidecar-session-conversation' -and
            $desktopRuntimeReceipt.shutdownModel -eq
                'quiesce-cancel-sidecar-shutdown-broker-dispose' -and
            $desktopRuntimeReceipt.runtimeCompositionPassed -and
            $desktopRuntimeReceipt.multiTurnPassed -and
            $desktopRuntimeReceipt.toolRoundTripPassed -and
            $desktopRuntimeReceipt.checkpointExportPassed -and
            $desktopRuntimeReceipt.checkpointContextRestorePassed -and
            $desktopRuntimeReceipt.checkpointAdmissionPassed -and
            $desktopRuntimeReceipt.quiesceClosedSubmission -and
            $desktopRuntimeReceipt.shutdownCancelledActiveTurn -and
            $desktopRuntimeReceipt.orderlyShutdownPassed -and
            $desktopRuntimeReceipt.startupRollbackPassed -and
            $desktopRuntimeReceipt.credentialEnvironmentClean -and
            $desktopRuntimeReceipt.normalBrokerRequestCount -eq 4 -and
            $desktopRuntimeReceipt.resumeBrokerRequestCount -eq 1 -and
            $desktopRuntimeReceipt.abortBrokerRequestCount -eq 1 -and
            $desktopRuntimeReceipt.exportedCheckpointTurnCount -eq 3 -and
            $desktopRuntimeReceipt.restoredCheckpointTurnCount -eq 3 -and
            $desktopRuntimeReceipt.brokerFaultCount -eq 0 -and
            -not $desktopRuntimeReceipt.credentialTransportAllowed -and
            -not $desktopRuntimeReceipt.piSidecarModelNetworkAllowed -and
            $desktopRuntimeReceipt.liveModelNetwork -eq 'diagnostic-only' -and
            $desktopRuntimeReceipt.liveExplorer -eq 'not-run' -and
            -not $desktopRuntimeReceipt.mutationPerformed) `
        -Detail (
            "Desktop runtime exit $desktopRuntimeExitCode; result " +
            "$desktopRuntimeResult.")

    $faultOutput = @(
        & $DotnetPath run `
            --project $bridgeProjectPath `
            --configuration Release `
            --no-build `
            -- `
            fault-tests `
            --node $NodePath `
            --fixtures $bridgeFixtureRoot 2>&1
    )
    $faultExitCode = $LASTEXITCODE
    $faultReceipt = $null
    try {
        $faultReceipt =
            ($faultOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $faultReceipt = $null
    }
    $faultResult = if ($null -ne $faultReceipt) {
        $faultReceipt.result
    }
    else {
        'unparsed'
    }
    Add-Check `
        -Name 'runtime.desktop-bridge-fault-probe' `
        -Passed (
            $faultExitCode -eq 0 -and
            $null -ne $faultReceipt -and
            $faultReceipt.result -eq 'passed' -and
            $faultReceipt.scenarioCount -eq 3 -and
            $faultReceipt.passedCount -eq 3 -and
            (@($faultReceipt.scenarios | ForEach-Object name) -join '|') -eq
                'wrong-ready-rejected|oversized-ready-rejected|' +
                'hung-ready-times-out' -and
            $faultReceipt.sessionCreationEnabled -and
            -not $faultReceipt.shellMutationSupported -and
            -not $faultReceipt.explorerMutationSupported -and
            -not $faultReceipt.systemMutationSupported -and
            -not $faultReceipt.activationPermitted -and
            $faultReceipt.liveExplorer -eq 'not-run' -and
            -not $faultReceipt.mutationPerformed) `
        -Detail (
            "Desktop fault probe exit $faultExitCode; result $faultResult.")
}

$passed = $failures.Count -eq 0
[pscustomobject]@{
    schemaVersion = 1
    receiptType = 'jarvisv2-pi-agent-host-audit'
    result = if ($passed) { 'passed' } else { 'failed' }
    staticOnly = [bool]$StaticOnly
    checkCount = $checks.Count
    passedCount = @($checks | Where-Object passed).Count
    integrationMode = 'sdk-sidecar-jsonl'
    embeddedPackage = '@earendil-works/pi-coding-agent'
    embeddedVersion = '0.82.1'
    transportProbeImplemented = $true
    sessionCreationEnabled = $true
    promptingEnabled = $false
    promptingAdmission = 'desktop-broker-required'
    desktopModelBrokerImplemented = $true
    desktopModelBrokerLifetime = 'desktop-owned-multi-request'
    multiTurnPromptingImplemented = $true
    toolRoundTripImplemented = $true
    providerToolAllowlistEnforced = $true
    orderedTurnEventStreamingImplemented = $true
    turnEventBufferCapacity = 512
    desktopConversationStateImplemented = $true
    desktopConversationRetainedTurns = 128
    desktopConversationMaxAssistantCharacters = 262144
    desktopConversationNotificationDispatch =
        'captured-synchronization-context'
    desktopConversationCheckpoint =
        'bounded-completed-text-context-restore'
    desktopConversationCheckpointMaxTurns = 32
    desktopConversationCheckpointMaxBytes = 32768
    desktopConversationCheckpointMaxTextBytes = 16384
    desktopConversationCheckpointPersistence =
        'desktop-owned-external'
    desktopRuntimeImplemented = $true
    desktopRuntimeOwnership =
        'desktop-owned-broker-sidecar-session-conversation'
    desktopRuntimeShutdown =
        'quiesce-cancel-sidecar-shutdown-broker-dispose'
    asynchronousTurnsImplemented = $true
    activeTurnCancellationImplemented = $true
    sessionPersistence = 'in-memory'
    workspaceBinding = 'single-explicit-root'
    desktopLaunchImplemented = $true
    credentialTransportAllowed = $false
    shellMutationSupported = $false
    explorerMutationSupported = $false
    systemMutationSupported = $false
    activationPermitted = $false
    liveExplorer = 'not-run'
    mutationPerformed = $false
    checks = $checks
    failures = $failures
} | ConvertTo-Json -Depth 12

if (-not $passed) {
    exit 1
}
