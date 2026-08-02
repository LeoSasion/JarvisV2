[CmdletBinding()]
param(
    [switch]$StaticOnly,
    [string]$NodePath = 'node',
    [string]$DotnetPath = 'dotnet',
    [string]$GitPath = 'git.exe'
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
$piSdkAdapterPath = Join-Path $sourceRoot 'src\pi-sdk-adapter.mjs'
$runtimeInspectorPath = Join-Path $sourceRoot (
    'src\pi-runtime-inspector.mjs')
$readOnlySessionPath = Join-Path $sourceRoot 'src\read-only-session.mjs'
$workspaceEditProposalPath = Join-Path $sourceRoot (
    'src\workspace-edit-proposal.mjs')
$protocolTestPath = Join-Path $sourceRoot 'test\protocol.test.mjs'
$workspaceEditTestPath = Join-Path $sourceRoot (
    'test\workspace-edit-approval.test.mjs')
$workspaceChangeSetTestPath = Join-Path $sourceRoot (
    'test\workspace-change-set-approval.test.mjs')
$protocolSourcePath = Join-Path $sourceRoot 'src\protocol.mjs'
$brokerTestPath = Join-Path $sourceRoot (
    'test\desktop-model-broker.test.mjs')
$bridgeProjectPath = Join-Path $sourceRoot 'Jarvis.PiAgentHost.csproj'
$bridgeSourcePath = Join-Path $sourceRoot 'DesktopBridge.cs'
$productionBrokerSourcePath = Join-Path $sourceRoot 'DesktopModelBroker.cs'
$brokerSourcePath = Join-Path $sourceRoot 'DiagnosticModelBroker.cs'
$conversationSourcePath = Join-Path $sourceRoot 'ConversationState.cs'
$checkpointStoreSourcePath = Join-Path $sourceRoot (
    'ConversationCheckpointStore.cs')
$conversationProbeSourcePath = Join-Path $sourceRoot (
    'DiagnosticConversation.cs')
$desktopRuntimeSourcePath = Join-Path $sourceRoot 'DesktopRuntime.cs'
$desktopRuntimeProbeSourcePath = Join-Path $sourceRoot (
    'DiagnosticDesktopRuntime.cs')
$reviewedIterationStatePath = Join-Path $sourceRoot (
    'ReviewedIterationState.cs')
$reviewedIterationStorePath = Join-Path $sourceRoot (
    'ReviewedIterationStore.cs')
$reviewedIterationGatePath = Join-Path $sourceRoot (
    'ReviewedIterationRepositoryGate.cs')
$reviewedIterationCoordinatorPath = Join-Path $sourceRoot (
    'ReviewedIterationCoordinator.cs')
$reviewedIterationTrustedValidationPath = Join-Path $sourceRoot (
    'ReviewedIterationTrustedValidation.cs')
$reviewedIterationProbePath = Join-Path $sourceRoot (
    'DiagnosticReviewedIteration.cs')
$trustedValidationManifestPath = Join-Path $root (
    'config\pi-agent-trusted-validation.json')
$openAiProviderSourcePath = Join-Path $sourceRoot (
    'OpenAiResponsesModelProvider.cs')
$openAiCredentialSourcePath = Join-Path $sourceRoot (
    'OpenAiApiKeyCredentialStore.cs')
$openAiProbeSourcePath = Join-Path $sourceRoot (
    'DiagnosticOpenAiResponsesProvider.cs')
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
$nonMutationRuntimeSourceText = @(
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'src') `
        -File `
        -Filter '*.mjs' |
        Where-Object Name -ne 'workspace-edit-proposal.mjs' |
        Sort-Object FullName |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName)
        }
) -join [Environment]::NewLine
$workspaceEditProposalText =
    [IO.File]::ReadAllText($workspaceEditProposalPath)
$piSdkAdapterText = [IO.File]::ReadAllText($piSdkAdapterPath)
$runtimeInspectorText = [IO.File]::ReadAllText($runtimeInspectorPath)
$readOnlySessionText = [IO.File]::ReadAllText($readOnlySessionPath)
$protocolSourceText = [IO.File]::ReadAllText($protocolSourcePath)

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
        $contract.runtime.sdkImportModel -eq
            'pinned-package-core-module-adapter' -and
        $package.dependencies.'@earendil-works/pi-ai' -eq '0.82.1' -and
        $package.dependencies.'@earendil-works/pi-coding-agent' -eq
            '0.82.1') `
    -Detail (
        'The sidecar must pin the reviewed official Pi package exactly, ' +
        'without a floating range.')

Add-Check `
    -Name 'runtime.pinned-core-sdk-adapter' `
    -Passed (
        (Test-Path -LiteralPath $piSdkAdapterPath -PathType Leaf) -and
        $piSdkAdapterText.Contains(
            'import.meta.resolve(packageName)') -and
        $piSdkAdapterText.Contains(
            'packageEntryUrl.pathname.endsWith("/dist/index.js")') -and
        $piSdkAdapterText.Contains(
            'import(new URL("./core/sdk.js", packageEntryUrl))') -and
        $piSdkAdapterText.Contains(
            'import(new URL("./core/extensions/index.js", packageEntryUrl))') -and
        $piSdkAdapterText.Contains(
            'import(new URL("./core/tools/index.js", packageEntryUrl))') -and
        $readOnlySessionText.Contains(
            'from "./pi-sdk-adapter.mjs"') -and
        $runtimeInspectorText.Contains(
            'await import("./pi-sdk-adapter.mjs")') -and
        -not $readOnlySessionText.Contains(
            'from "@earendil-works/pi-coding-agent"') -and
        -not $runtimeInspectorText.Contains(
            'await import(packageName)')) `
    -Detail (
        'The exact pinned package entry must resolve to the reviewed layout, ' +
        'and sidecar readiness may load only the required core SDK modules.')

Add-Check `
    -Name 'contract.review-gated-session-and-tools' `
    -Passed (
        $contract.runtime.sessionCreationEnabled -and
        $contract.runtime.desktopLaunchImplemented -and
        $contract.runtime.launchState -eq
            'review-gated-workspace-session-admission' -and
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
        $contract.session.desktopConversationCheckpointStore -eq
            'current-user-dpapi-atomic-workspace-bound' -and
        $contract.session.desktopConversationCheckpointStoreRoot -eq
            'local-appdata-jarvis2-pi-agent-conversations' -and
        $contract.session.desktopConversationCheckpointEnvelopeMaxBytes -eq
            65536 -and
        $contract.session.desktopConversationCheckpointSave -eq
            'ordered-terminal-autosave-and-shutdown-flush' -and
        $contract.session.desktopConversationCheckpointSaveTimeoutMilliseconds -eq
            5000 -and
        $contract.session.desktopConversationCheckpointFailure -eq
            'close-submissions-and-surface-on-shutdown' -and
        $contract.session.reviewedIterationEnabled -and
        $contract.session.reviewedIterationPolicy -eq
            'desktop-owner-fixed-four-edits-six-hours' -and
        $contract.session.reviewedIterationContinuation -eq
            'automatic-after-owner-approval-and-passed-repository-gate' -and
        $contract.session.reviewedIterationStore -eq
            'current-user-dpapi-atomic-workspace-bound-durable-receipts' -and
        $contract.session.reviewedIterationStoreRoot -eq
            'local-appdata-jarvis2-pi-agent-reviewed-iterations' -and
        $contract.session.reviewedIterationEnvelopeMaxBytes -eq 262144 -and
        $contract.session.reviewedIterationValidationProfile -eq
            'git-head-pathset-text-hash-diffcheck-structured-parse-v2' -and
        $contract.session.reviewedIterationRepositoryBaseline -eq
            'clean-git-head-required' -and
        $contract.session.reviewedIterationRestart -eq
            'interrupted-explicit-rearm-no-proposal-restore' -and
        -not $contract.session.reviewedIterationWorkspaceCodeExecution -and
        $contract.session.desktopRuntimeOwnership -eq
            'desktop-owned-broker-sidecar-session-conversation' -and
        $contract.session.desktopRuntimeShutdown -eq
            'quiesce-cancel-checkpoint-flush-sidecar-shutdown-broker-dispose' -and
        $contract.session.credentialTransport -eq 'forbidden' -and
        $contract.session.persistence -eq 'in-memory' -and
        $contract.session.workspaceBinding -eq
            'single-explicit-root' -and
        $contract.session.resourceDiscovery -eq 'disabled' -and
        -not $contract.session.modelNetworkAllowed -and
        (@($contract.tools.initialAllowlist) -join '|') -eq
            'read|grep|find|ls|propose_edit|propose_patch|propose_create_file|propose_change_set' -and
        (@($contract.tools.initiallyDenied) -join '|') -eq
            'bash|edit|write' -and
        $contract.tools.proposalTool -eq
            'non-mutating-explicit-utf8-replace-patch-create-or-change-set' -and
        $contract.tools.proposalMaxFileBytes -eq 1048576 -and
        $contract.tools.proposalMaxSegmentBytes -eq 4096 -and
        $contract.tools.patchProposalMaxHunks -eq 8 -and
        $contract.tools.patchProposalMaxPreviewBytes -eq 16384 -and
        $contract.tools.createProposalMaxBytes -eq 16384 -and
        $contract.tools.changeSetMinimumFiles -eq 2 -and
        $contract.tools.changeSetMaximumFiles -eq 4 -and
        $contract.tools.changeSetMaximumPreviewBytes -eq 32768 -and
        $contract.tools.pendingProposalLimit -eq 1 -and
        $contract.tools.pendingProposalPolicy -eq
            'blocks-new-turns-and-clears-on-shutdown' -and
        $contract.tools.approvalOwner -eq 'desktop-user-only' -and
        $contract.tools.approvalMode -eq
            'one-shot-explicit-operation-before-state-sha256' -and
        $contract.tools.commitMode -eq
            'single-file-atomic-or-multi-file-durable-before-after-convergence' -and
        $contract.tools.changeSetRecovery -eq
            'strict-journal-before-tools-rollback-or-complete' -and
        -not $contract.tools.simultaneousMultiPathVisibilityClaimed -and
        $contract.tools.newFileSupported -and
        $contract.tools.newFileParentPolicy -eq
            'existing-canonical-directory-no-auto-create' -and
        -not $contract.tools.versionControlMetadataMutation -and
        -not $contract.tools.deleteSupported -and
        $contract.tools.mutationGrant -eq
            'desktop-owner-one-shot-reviewed-text-single-file-or-two-to-four-file-set' -and
        $contract.tools.reviewedSelfIteration -and
        $contract.tools.automaticReasoningContinuation -and
        -not $contract.tools.unattendedApproval -and
        -not $contract.tools.selfAuthoredPolicy -and
        -not $contract.tools.unattendedSelfIteration) `
    -Detail (
        'The managed desktop may create one real in-memory Pi session for ' +
        'one admitted workspace; prompting requires a desktop-owned named ' +
        'pipe while direct mutation stays denied and exact edits require a ' +
        'desktop-owner one-shot before-hash decision.')

Add-Check `
    -Name 'contract.jsonl-and-shell-boundary' `
    -Passed (
        $contract.runtime.integrationMode -eq 'sdk-sidecar-jsonl' -and
        $contract.runtime.piOfflineRequired -and
        $contract.transport.framing -eq 'lf-delimited-jsonl' -and
        $contract.transport.maxFrameBytes -eq 131072 -and
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
    -Name 'contract.desktop-production-provider-boundary' `
    -Passed (
        $contract.desktopProvider.mode -eq 'opt-in' -and
        $contract.desktopProvider.implementation -eq
            'openai-responses-api' -and
        $contract.desktopProvider.endpoint -eq
            'https://api.openai.com/v1/responses' -and
        $contract.desktopProvider.model -eq 'gpt-5.6-sol' -and
        $contract.desktopProvider.reasoningEffort -eq 'medium' -and
        $contract.desktopProvider.streaming -eq
            'server-sent-events' -and
        -not $contract.desktopProvider.responseStorage -and
        $contract.desktopProvider.networkOwner -eq
            'desktop-process-only' -and
        $contract.desktopProvider.credentialStore -eq
            'current-user-dpapi-atomic' -and
        $contract.desktopProvider.credentialRoot -eq
            'local-appdata-jarvis2-credentials' -and
        -not $contract.desktopProvider.ambientCredentialAllowed -and
        -not $contract.desktopProvider.credentialTransportToSidecar) `
    -Detail (
        'The opt-in production provider must keep HTTPS and DPAPI ' +
        'credentials in the desktop process, disable response storage and ' +
        'leave the Pi sidecar offline and credential-free.')

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
        $schema.properties.runtime.properties.sdkImportModel.const -eq
            'pinned-package-core-module-adapter' -and
        $schema.properties.runtime.properties.launchState.const -eq
            'review-gated-workspace-session-admission' -and
        $schema.properties.runtime.properties.desktopLaunchImplemented.const `
            -eq $true -and
        $schema.properties.transport.properties.maxFrameBytes.const -eq
            131072 -and
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
        $schema.properties.session.properties.desktopConversationCheckpointStore.const `
            -eq 'current-user-dpapi-atomic-workspace-bound' -and
        $schema.properties.session.properties.desktopConversationCheckpointStoreRoot.const `
            -eq 'local-appdata-jarvis2-pi-agent-conversations' -and
        $schema.properties.session.properties.desktopConversationCheckpointEnvelopeMaxBytes.const `
            -eq 65536 -and
        $schema.properties.session.properties.desktopConversationCheckpointSave.const `
            -eq 'ordered-terminal-autosave-and-shutdown-flush' -and
        $schema.properties.session.properties.desktopConversationCheckpointSaveTimeoutMilliseconds.const `
            -eq 5000 -and
        $schema.properties.session.properties.desktopConversationCheckpointFailure.const `
            -eq 'close-submissions-and-surface-on-shutdown' -and
        $schema.properties.session.properties.reviewedIterationEnabled.const `
            -eq $true -and
        $schema.properties.session.properties.reviewedIterationPolicy.const `
            -eq 'desktop-owner-fixed-four-edits-six-hours' -and
        $schema.properties.session.properties.reviewedIterationContinuation.const `
            -eq 'automatic-after-owner-approval-and-passed-repository-gate' -and
        $schema.properties.session.properties.reviewedIterationStore.const `
            -eq 'current-user-dpapi-atomic-workspace-bound-durable-receipts' -and
        $schema.properties.session.properties.reviewedIterationEnvelopeMaxBytes.const `
            -eq 262144 -and
        $schema.properties.session.properties.reviewedIterationValidationProfile.const `
            -eq 'git-head-pathset-text-hash-diffcheck-structured-parse-v2' -and
        $schema.properties.session.properties.reviewedIterationWorkspaceCodeExecution.const `
            -eq $false -and
        $schema.properties.session.properties.desktopRuntimeOwnership.const `
            -eq 'desktop-owned-broker-sidecar-session-conversation' -and
        $schema.properties.session.properties.desktopRuntimeShutdown.const `
            -eq 'quiesce-cancel-checkpoint-flush-sidecar-shutdown-broker-dispose' -and
        $schema.properties.session.properties.modelNetworkAllowed.const `
            -eq $false -and
        $schema.properties.desktopProvider.properties.mode.const `
            -eq 'opt-in' -and
        $schema.properties.desktopProvider.properties.endpoint.const `
            -eq 'https://api.openai.com/v1/responses' -and
        $schema.properties.desktopProvider.properties.model.const `
            -eq 'gpt-5.6-sol' -and
        $schema.properties.desktopProvider.properties.reasoningEffort.const `
            -eq 'medium' -and
        $schema.properties.desktopProvider.properties.responseStorage.const `
            -eq $false -and
        $schema.properties.desktopProvider.properties.ambientCredentialAllowed.const `
            -eq $false -and
        $schema.properties.desktopProvider.properties.credentialTransportToSidecar.const `
            -eq $false -and
        $schema.properties.transport.properties.credentialFieldsAllowed.const `
            -eq $false -and
        (@($schema.properties.tools.properties.initialAllowlist.const) -join '|') `
            -eq 'read|grep|find|ls|propose_edit|propose_patch|propose_create_file|propose_change_set' -and
        $schema.properties.tools.properties.proposalTool.const -eq
            'non-mutating-explicit-utf8-replace-patch-create-or-change-set' -and
        $schema.properties.tools.properties.proposalMaxFileBytes.const -eq
            1048576 -and
        $schema.properties.tools.properties.proposalMaxSegmentBytes.const -eq
            4096 -and
        $schema.properties.tools.properties.patchProposalMaxHunks.const -eq
            8 -and
        $schema.properties.tools.properties.patchProposalMaxPreviewBytes.const `
            -eq 16384 -and
        $schema.properties.tools.properties.createProposalMaxBytes.const -eq
            16384 -and
        $schema.properties.tools.properties.changeSetMinimumFiles.const -eq
            2 -and
        $schema.properties.tools.properties.changeSetMaximumFiles.const -eq
            4 -and
        $schema.properties.tools.properties.changeSetMaximumPreviewBytes.const `
            -eq 32768 -and
        $schema.properties.tools.properties.pendingProposalLimit.const -eq
            1 -and
        $schema.properties.tools.properties.approvalOwner.const -eq
            'desktop-user-only' -and
        $schema.properties.tools.properties.approvalMode.const -eq
            'one-shot-explicit-operation-before-state-sha256' -and
        $schema.properties.tools.properties.commitMode.const -eq
            'single-file-atomic-or-multi-file-durable-before-after-convergence' -and
        $schema.properties.tools.properties.changeSetRecovery.const -eq
            'strict-journal-before-tools-rollback-or-complete' -and
        $schema.properties.tools.properties.simultaneousMultiPathVisibilityClaimed.const `
            -eq $false -and
        $schema.properties.tools.properties.newFileSupported.const -eq
            $true -and
        $schema.properties.tools.properties.newFileParentPolicy.const -eq
            'existing-canonical-directory-no-auto-create' -and
        $schema.properties.tools.properties.versionControlMetadataMutation.const -eq
            $false -and
        $schema.properties.tools.properties.deleteSupported.const -eq
            $false -and
        $schema.properties.tools.properties.reviewedSelfIteration.const -eq
            $true -and
        $schema.properties.tools.properties.automaticReasoningContinuation.const -eq
            $true -and
        $schema.properties.tools.properties.unattendedApproval.const -eq
            $false -and
        $schema.properties.tools.properties.selfAuthoredPolicy.const -eq
            $false -and
        $schema.properties.tools.properties.unattendedSelfIteration.const -eq
            $false -and
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
            $nonMutationRuntimeSourceText,
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
        $runtimeSourceText.Contains('credential-field-forbidden') -and
        (Test-Path -LiteralPath $workspaceEditProposalPath -PathType Leaf) -and
        (Test-Path -LiteralPath $workspaceEditTestPath -PathType Leaf) -and
        (Test-Path -LiteralPath $workspaceChangeSetTestPath -PathType Leaf) -and
        $workspaceEditProposalText.Contains('propose_edit') -and
        $workspaceEditProposalText.Contains('propose_patch') -and
        $workspaceEditProposalText.Contains('propose_create_file') -and
        $workspaceEditProposalText.Contains('propose_change_set') -and
        $workspaceEditProposalText.Contains(
            'maximumWorkspaceEditSegmentBytes = 4_096') -and
        $workspaceEditProposalText.Contains(
            'maximumWorkspaceEditFileBytes = 1_048_576') -and
        $workspaceEditProposalText.Contains(
            'maximumWorkspaceCreateFileBytes = 16_384') -and
        $workspaceEditProposalText.Contains(
            'maximumWorkspacePatchHunks = 8') -and
        $workspaceEditProposalText.Contains(
            'maximumWorkspacePatchPreviewBytes = 16_384') -and
        $workspaceEditProposalText.Contains(
            'minimumWorkspaceChangeSetFiles = 2') -and
        $workspaceEditProposalText.Contains(
            'maximumWorkspaceChangeSetFiles = 4') -and
        $workspaceEditProposalText.Contains(
            'maximumWorkspaceChangeSetPreviewBytes = 32_768') -and
        $workspaceEditProposalText.Contains(
            'workspaceTransactionJournalName') -and
        $workspaceEditProposalText.Contains(
            'workspace-change-set-recovery-required') -and
        $workspaceEditProposalText.Contains(
            'durable-before-or-after-convergence-no-simultaneous-visibility-claim') -and
        $workspaceEditProposalText.Contains(
            'WorkspaceTransactionCrashForTest') -and
        -not $protocolSourceText.Contains('transactionHooks') -and
        $workspaceEditProposalText.Contains(
            'workspace-patch-overlap') -and
        $workspaceEditProposalText.Contains('isUtf8') -and
        $workspaceEditProposalText.Contains('stats.nlink !== 1') -and
        $workspaceEditProposalText.Contains(
            'content.indexOf(search, first + 1)') -and
        $workspaceEditProposalText.Contains(
            'workspace-edit-drifted') -and
        $workspaceEditProposalText.Contains(
            'workspace-edit-proposal-mismatch') -and
        $workspaceEditProposalText.Contains('await temporary.sync()') -and
        $workspaceEditProposalText.Contains(
            'await rename(temporaryPath, first.safePath)') -and
        $workspaceEditProposalText.Contains(
            'await open(safePath, "wx", 0o600)') -and
        $workspaceEditProposalText.Contains(
            'workspace-vcs-metadata-forbidden') -and
        $workspaceEditProposalText.Contains(
            'mutationPerformed: true')) `
    -Detail (
        'Runtime source must create exactly one root-confined in-memory SDK ' +
        'session and one broker-gated prompt path without credential files, ' +
        'child processes or unreviewed writes; the isolated edit module must ' +
        'bind one exact replacement, one bounded single-file multi-hunk patch, one exclusive new UTF-8 file, or one bounded multi-file set to an explicit review digest and owner decision; recovery hooks remain unreachable from the protocol.')

$bridgeSourceText =
    [IO.File]::ReadAllText($bridgeSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($productionBrokerSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($brokerSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($conversationSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($checkpointStoreSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($conversationProbeSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($desktopRuntimeSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($desktopRuntimeProbeSourcePath) +
    [Environment]::NewLine +
    [IO.File]::ReadAllText($bridgeProgramPath)
$openAiProviderText = [IO.File]::ReadAllText($openAiProviderSourcePath)
$openAiCredentialText = [IO.File]::ReadAllText($openAiCredentialSourcePath)
$openAiProbeText = [IO.File]::ReadAllText($openAiProbeSourcePath)
$reviewedIterationProductionText = @(
    [IO.File]::ReadAllText($reviewedIterationStatePath),
    [IO.File]::ReadAllText($reviewedIterationStorePath),
    [IO.File]::ReadAllText($reviewedIterationGatePath),
    [IO.File]::ReadAllText($reviewedIterationCoordinatorPath),
    [IO.File]::ReadAllText($reviewedIterationTrustedValidationPath)
) -join [Environment]::NewLine
$reviewedIterationProbeText =
    [IO.File]::ReadAllText($reviewedIterationProbePath)
$bridgeProjectText = [IO.File]::ReadAllText($bridgeProjectPath)
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
        $bridgeProjectText.Contains(
            '<TargetFramework>net8.0-windows</TargetFramework>') -and
        $bridgeProjectText.Contains(
            '<FrameworkReference Include="Microsoft.WindowsDesktop.App" />') -and
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
        $bridgeSourceText.Contains('CommitWorkspaceEditAsync') -and
        $bridgeSourceText.Contains('DiscardWorkspaceEditAsync') -and
        $bridgeSourceText.Contains('PiAgentWorkspaceEditProposed') -and
        $bridgeSourceText.Contains('PiAgentWorkspaceEditStatus') -and
        $bridgeSourceText.Contains(
            'PiAgentWorkspaceEditStatus.Expired') -and
        $bridgeSourceText.Contains('workspaceEditDecisionTask') -and
        $bridgeSourceText.Contains('workspace-edit-drifted') -and
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
            'current-user-dpapi-atomic-workspace-bound') -and
        $bridgeSourceText.Contains(
            'ProtectedData.Protect') -and
        $bridgeSourceText.Contains(
            'ProtectedData.Unprotect') -and
        $bridgeSourceText.Contains(
            'DataProtectionScope.CurrentUser') -and
        $bridgeSourceText.Contains(
            'MaximumEnvelopeBytes = 65_536') -and
        $bridgeSourceText.Contains(
            'FileOptions.WriteThrough') -and
        $bridgeSourceText.Contains(
            'File.Move') -and
        $bridgeSourceText.Contains(
            'FileAttributes.ReparsePoint') -and
        $bridgeSourceText.Contains(
            'PiAgentConversationSnapshot') -and
        $bridgeSourceText.Contains('SynchronizationContext') -and
        $bridgeSourceText.Contains('CancelActiveTurnAsync') -and
        $bridgeSourceText.Contains('QuiesceAsync') -and
        $bridgeSourceText.Contains('PiAgentDesktopRuntime') -and
        $bridgeSourceText.Contains(
            'desktop-owned-broker-sidecar-session-conversation') -and
        $bridgeSourceText.Contains(
            'quiesce-cancel-checkpoint-flush-sidecar-shutdown-broker-dispose') -and
        $bridgeSourceText.Contains(
            'ordered-terminal-autosave-fail-closed') -and
        $bridgeSourceText.Contains(
            'CheckpointSaveTimeoutMilliseconds = 5_000') -and
        $bridgeSourceText.Contains(
            'TerminalCheckpointAvailable') -and
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
        'scrub credential variables, bind one review-gated session, admit one ' +
        'multi-request current-user model pipe and terminate only its owned ' +
        'processes and connections on cleanup.')

$forbiddenProductionProviderPattern = (
    '(?i)\b(?:DllImport|LibraryImport|OpenProcess|CreateRemoteThread|' +
    'WriteProcessMemory|SetWindowsHookEx|Microsoft\.Win32\.Registry|' +
    'ServiceController|ProcessStartInfo|OPENAI_API_KEY)\b'
)
Add-Check `
    -Name 'desktop-provider.responses-dpapi-streaming-boundary' `
    -Passed (
        (Test-Path -LiteralPath $openAiProviderSourcePath -PathType Leaf) -and
        (Test-Path -LiteralPath $openAiCredentialSourcePath -PathType Leaf) -and
        (Test-Path -LiteralPath $openAiProbeSourcePath -PathType Leaf) -and
        -not [regex]::IsMatch(
            ($openAiProviderText + $openAiCredentialText),
            $forbiddenProductionProviderPattern) -and
        $openAiProviderText.Contains(
            'https://api.openai.com/v1/responses') -and
        $openAiProviderText.Contains('"gpt-5.6-sol"') -and
        $openAiProviderText.Contains('"medium"') -and
        $openAiProviderText.Contains('["store"] = false') -and
        $openAiProviderText.Contains('response.output_text.delta') -and
        $openAiProviderText.Contains(
            'response.function_call_arguments.delta') -and
        $openAiProviderText.Contains(
            'MaximumFunctionArgumentsCharacters') -and
        $openAiProviderText.Contains(
            'did not return an SSE stream') -and
        $openAiProviderText.Contains('AllowedToolNames.Contains') -and
        $openAiProviderText.Contains('"propose_edit"') -and
        $openAiProviderText.Contains('"propose_patch"') -and
        $openAiProviderText.Contains('"propose_create_file"') -and
        $openAiProviderText.Contains('"propose_change_set"') -and
        $openAiCredentialText.Contains('ProtectedData.Protect') -and
        $openAiCredentialText.Contains('ProtectedData.Unprotect') -and
        $openAiCredentialText.Contains(
            'DataProtectionScope.CurrentUser') -and
        $openAiCredentialText.Contains('FileOptions.WriteThrough') -and
        $openAiCredentialText.Contains('File.Move') -and
        $openAiCredentialText.Contains('FileAttributes.ReparsePoint') -and
        $openAiProbeText.Contains('LiveModelNetworkCalled') -and
        $openAiProbeText.Contains('OversizedToolArgumentsRejected') -and
        $openAiProbeText.Contains('CredentialTransportToSidecar')) `
    -Detail (
        'The production Provider may use only the exact Responses endpoint, ' +
        'bounded SSE and the exact reviewed read/proposal tools; authentication must come from ' +
        'an atomic CurrentUser DPAPI envelope, never the environment.')

$forbiddenIterationPattern = (
    '(?i)\b(?:cmd\.exe|powershell(?:\.exe)?|pwsh(?:\.exe)?|bash|' +
    'UseShellExecute\s*=\s*true|Microsoft\.Win32\.Registry|' +
    'CreateRemoteThread|WriteProcessMemory|SetWindowsHookEx)\b'
)
Add-Check `
    -Name 'reviewed-iteration.durable-owner-policy-and-fixed-gate' `
    -Passed (
        (Test-Path -LiteralPath $reviewedIterationStatePath -PathType Leaf) -and
        (Test-Path -LiteralPath $reviewedIterationStorePath -PathType Leaf) -and
        (Test-Path -LiteralPath $reviewedIterationGatePath -PathType Leaf) -and
        (Test-Path -LiteralPath $reviewedIterationCoordinatorPath -PathType Leaf) -and
        (Test-Path -LiteralPath $reviewedIterationTrustedValidationPath -PathType Leaf) -and
        (Test-Path -LiteralPath $trustedValidationManifestPath -PathType Leaf) -and
        (Test-Path -LiteralPath $reviewedIterationProbePath -PathType Leaf) -and
        -not [regex]::IsMatch(
            $reviewedIterationProductionText,
            $forbiddenIterationPattern) -and
        $reviewedIterationProductionText.Contains(
            'MaximumApprovedEdits = 4') -and
        $reviewedIterationProductionText.Contains(
            'PolicyLifetimeHours = 6') -and
        $reviewedIterationProductionText.Contains(
            'PiAgentReviewedIterationFileReceipt') -and
        $reviewedIterationProductionText.Contains(
            'snapshot.SchemaVersion is not (1 or 2 or 3)') -and
        $reviewedIterationProductionText.Contains(
            'ComputeChangeSetAfterDigest') -and
        $reviewedIterationProductionText.Contains(
            'desktop-owner-one-shot-per-edit-no-model-decision-authority') -and
        $reviewedIterationProductionText.Contains(
            'desktop-owner-one-shot-pinned-head-tests-no-model-execution-authority') -and
        $reviewedIterationProductionText.Contains(
            'desktop-owner-approved-pinned-head-node-test-direct-no-shell') -and
        $reviewedIterationProductionText.Contains(
            'current-user-dpapi-atomic-workspace-bound-durable-receipts') -and
        $reviewedIterationProductionText.Contains(
            'DataProtectionScope.CurrentUser') -and
        $reviewedIterationProductionText.Contains(
            'FileOptions.WriteThrough') -and
        $reviewedIterationProductionText.Contains(
            'File.Move(temporaryPath, receiptPath, overwrite: true)') -and
        $reviewedIterationProductionText.Contains(
            'git-head-pathset-text-hash-diffcheck-structured-parse-v2') -and
        $reviewedIterationProductionText.Contains(
            'RequireUntrackedDiffCheckAsync') -and
        $reviewedIterationProductionText.Contains(
            'strict-utf8-text') -and
        $reviewedIterationProductionText.Contains(
            '"--no-textconv"') -and
        $reviewedIterationProductionText.Contains(
            'UseShellExecute = false') -and
        $reviewedIterationProductionText.Contains(
            'startInfo.Environment.Clear()') -and
        $reviewedIterationProductionText.Contains(
            'startInfo.ArgumentList.Add("--test")') -and
        $reviewedIterationProductionText.Contains(
            'AwaitingTrustedValidation') -and
        $reviewedIterationProductionText.Contains(
            'RunTrustedValidationAndContinueAsync') -and
        $reviewedIterationProductionText.Contains(
            'CancelTrustedValidationOperation') -and
        $reviewedIterationProductionText.Contains(
            'GIT_OPTIONAL_LOCKS') -and
        $reviewedIterationProductionText.Contains(
            'core.fsmonitor=false') -and
        $reviewedIterationProductionText.Contains(
            'AppContext.BaseDirectory') -and
        $reviewedIterationProductionText.Contains(
            '"runtime"') -and
        $reviewedIterationProductionText.Contains(
            '"git"') -and
        $reviewedIterationProductionText.Contains(
            'process.Kill(entireProcessTree: true)') -and
        $reviewedIterationProductionText.Contains(
            'DtdProcessing = DtdProcessing.Prohibit') -and
        $reviewedIterationProductionText.Contains(
            'PiAgentReviewedIterationStatus.Interrupted') -and
        $reviewedIterationProductionText.Contains(
            'no pending edit capability was restored') -and
        $reviewedIterationProbeText.Contains(
            'RestartDidNotRestoreProposalCapability') -and
        $reviewedIterationProbeText.Contains(
            'UnattendedApprovalAllowed') -and
        $reviewedIterationProbeText.Contains(
            'ApprovedNewFileValidated') -and
        $reviewedIterationProbeText.Contains(
            'UntrackedWhitespaceRejected')) `
    -Detail (
        'Reviewed iteration must remain a desktop-owner policy with four ' +
        'one-shot write approvals, separate pinned-test approval, six-hour expiry, ' +
        'DPAPI receipts, fixed direct-Git pre/post gates and explicit restart re-arm.')

$controlCenterProjectText =
    [IO.File]::ReadAllText($controlCenterProjectPath)
$controlCenterBindingText =
    [IO.File]::ReadAllText($controlCenterBindingPath)
Add-Check `
    -Name 'desktop-conversation.wpf-binding-boundary' `
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
        $controlCenterBindingText.Contains(
            'ApplyWorkspaceEditAsync') -and
        $controlCenterBindingText.Contains(
            'RejectWorkspaceEditAsync') -and
        -not $controlCenterBindingText.Contains('System.Windows') -and
        -not $controlCenterBindingText.Contains('Process.') -and
        -not $controlCenterBindingText.Contains('Registry')) `
    -Detail (
        'Control Center may reference the reviewed Pi host and bind its ' +
        'property-change adapter into a product surface without owning transport.')

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
    $resolvedGitPath = if ([IO.Path]::IsPathFullyQualified($GitPath)) {
        [IO.Path]::GetFullPath($GitPath)
    }
    else {
        (Get-Command $GitPath -ErrorAction Stop).Source
    }
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

    $openAiProbeOutput = @(
        & $DotnetPath run `
            --project $bridgeProjectPath `
            --configuration Release `
            --no-build `
            -- `
            openai-provider-probe 2>&1
    )
    $openAiProbeExitCode = $LASTEXITCODE
    $openAiProbeReceipt = $null
    try {
        $openAiProbeReceipt =
            ($openAiProbeOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $openAiProbeReceipt = $null
    }
    Add-Check `
        -Name 'runtime.openai-provider-offline-probe' `
        -Passed (
            $openAiProbeExitCode -eq 0 -and
            $null -ne $openAiProbeReceipt -and
            $openAiProbeReceipt.result -eq 'passed' -and
            $openAiProbeReceipt.model -eq 'gpt-5.6-sol' -and
            $openAiProbeReceipt.requestContractPassed -and
            $openAiProbeReceipt.textStreamPassed -and
            $openAiProbeReceipt.toolStreamPassed -and
            $openAiProbeReceipt.usageMappingPassed -and
            $openAiProbeReceipt.credentialHeaderOnly -and
            $openAiProbeReceipt.credentialStoreRoundTripPassed -and
            $openAiProbeReceipt.credentialCiphertextPassed -and
            $openAiProbeReceipt.credentialCorruptionRejected -and
            $openAiProbeReceipt.httpFailureRedacted -and
            $openAiProbeReceipt.malformedStreamRejected -and
            $openAiProbeReceipt.cancellationPassed -and
            -not $openAiProbeReceipt.liveModelNetworkCalled -and
            -not $openAiProbeReceipt.credentialTransportToSidecar -and
            -not $openAiProbeReceipt.mutationPerformed -and
            @($openAiProbeReceipt.failures).Count -eq 0) `
        -Detail (
            'The synthetic desktop Provider must prove request, streaming, ' +
            'tool, credential, redaction, malformed-input and cancellation ' +
            'paths without making a live model call. Output: ' +
            (($openAiProbeOutput | Select-Object -Last 18) -join ' '))

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
            $inspectReceipt.sdkImportModel -eq
                'pinned-package-core-module-adapter' -and
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
            $inspectReceipt.workspaceEditProposalSupported -and
            $inspectReceipt.workspaceEditApprovalOwner -eq
                'desktop-user-only' -and
            $inspectReceipt.workspaceEditApprovalMode -eq
                'one-shot-explicit-operation-before-state-sha256' -and
            -not $inspectReceipt.workspaceEditExistingFilesOnly -and
            $inspectReceipt.workspaceFileCreateSupported -and
            $inspectReceipt.workspaceFileCreateMode -eq
                'exclusive-existing-parent-owner-approved' -and
            -not $inspectReceipt.unattendedSelfIteration -and
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
                'read|grep|find|ls|propose_edit|propose_patch|propose_create_file|propose_change_set' -and
            $protocolReceipt.workspaceEditProposalSupported -and
            $protocolReceipt.workspaceEditApprovalOwner -eq
                'desktop-user-only' -and
            $protocolReceipt.workspaceEditApprovalMode -eq
                'one-shot-explicit-operation-before-state-sha256' -and
            -not $protocolReceipt.workspaceEditExistingFilesOnly -and
            $protocolReceipt.workspacePatchSupported -and
            $protocolReceipt.workspacePatchMinimumHunks -eq 2 -and
            $protocolReceipt.workspacePatchMaximumHunks -eq 8 -and
            $protocolReceipt.workspacePatchMaximumPreviewBytes -eq 16384 -and
            $protocolReceipt.workspacePatchCommitMode -eq
                'single-file-atomic-replace-and-post-verify' -and
            $protocolReceipt.workspaceFileCreateSupported -and
            $protocolReceipt.workspaceFileCreateMode -eq
                'exclusive-existing-parent-owner-approved' -and
            $protocolReceipt.workspaceChangeSetSupported -and
            $protocolReceipt.workspaceChangeSetMinimumFiles -eq 2 -and
            $protocolReceipt.workspaceChangeSetMaximumFiles -eq 4 -and
            $protocolReceipt.workspaceChangeSetMaximumPreviewBytes -eq 32768 -and
            $protocolReceipt.workspaceChangeSetCommitMode -eq
                'durable-before-or-after-convergence-no-simultaneous-visibility-claim' -and
            $protocolReceipt.workspaceChangeSetRecovery -eq
                'strict-journal-before-tools-rollback-or-complete' -and
            -not $protocolReceipt.workspaceChangeSetRecoveryAvailableToModel -and
            $protocolReceipt.workspaceTransactionRecoveryResult -eq 'none' -and
            -not $protocolReceipt.unattendedSelfIteration -and
            -not $protocolReceipt.shellMutationSupported -and
            -not $protocolReceipt.explorerMutationSupported -and
            -not $protocolReceipt.activationPermitted -and
            $protocolReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Protocol exit $protocolExitCode; result " +
            "$protocolResult; records $recordCount.")

    $workspaceEditOutput = @(
        & $NodePath $workspaceEditTestPath 2>&1
    )
    $workspaceEditExitCode = $LASTEXITCODE
    $workspaceEditReceipt = $null
    try {
        $workspaceEditReceipt =
            ($workspaceEditOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $workspaceEditReceipt = $null
    }
    $workspaceEditResult = if ($null -ne $workspaceEditReceipt) {
        $workspaceEditReceipt.result
    }
    else {
        'unparsed'
    }
    Add-Check `
        -Name 'runtime.workspace-edit-approval-probe' `
        -Passed (
            $workspaceEditExitCode -eq 0 -and
            $null -ne $workspaceEditReceipt -and
            $workspaceEditReceipt.result -eq 'passed' -and
            (@($workspaceEditReceipt.activeTools) -join '|') -eq
                'read|grep|find|ls|propose_edit|propose_patch|propose_create_file|propose_change_set' -and
            -not $workspaceEditReceipt.proposalToolMutates -and
            -not $workspaceEditReceipt.existingTextFilesOnly -and
            $workspaceEditReceipt.newUtf8FileSupported -and
            $workspaceEditReceipt.multiHunkPatchSupported -and
            $workspaceEditReceipt.patchMinimumHunks -eq 2 -and
            $workspaceEditReceipt.patchMaximumHunks -eq 8 -and
            $workspaceEditReceipt.patchMaximumPreviewBytes -eq 16384 -and
            $workspaceEditReceipt.patchSingleFileOnly -and
            $workspaceEditReceipt.patchAtomicReplace -and
            $workspaceEditReceipt.patchOverlapRejected -and
            $workspaceEditReceipt.patchBinaryControlsRejected -and
            $workspaceEditReceipt.newFileMaxBytes -eq 16384 -and
            $workspaceEditReceipt.existingParentRequired -and
            $workspaceEditReceipt.exclusiveCreate -and
            $workspaceEditReceipt.overwriteRejected -and
            $workspaceEditReceipt.versionControlMetadataRejected -and
            $workspaceEditReceipt.windowsDeviceAliasesRejected -and
            $workspaceEditReceipt.exactBeforeSha256Bound -and
            $workspaceEditReceipt.oneShotApproval -and
            $workspaceEditReceipt.replayRejected -and
            $workspaceEditReceipt.driftRejected -and
            -not $workspaceEditReceipt.rejectMutates -and
            -not $workspaceEditReceipt.shellMutationSupported -and
            -not $workspaceEditReceipt.explorerMutationSupported -and
            -not $workspaceEditReceipt.unattendedSelfIteration -and
            $workspaceEditReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Workspace edit probe exit $workspaceEditExitCode; result " +
            "$workspaceEditResult.")

    $workspaceChangeSetOutput = @(
        & $NodePath $workspaceChangeSetTestPath 2>&1
    )
    $workspaceChangeSetExitCode = $LASTEXITCODE
    $workspaceChangeSetReceipt = $null
    try {
        $workspaceChangeSetReceipt =
            ($workspaceChangeSetOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $workspaceChangeSetReceipt = $null
    }
    $workspaceChangeSetResult = if (
        $null -ne $workspaceChangeSetReceipt
    ) {
        $workspaceChangeSetReceipt.result
    }
    else {
        'unparsed'
    }
    Add-Check `
        -Name 'runtime.workspace-change-set-approval-probe' `
        -Passed (
            $workspaceChangeSetExitCode -eq 0 -and
            $null -ne $workspaceChangeSetReceipt -and
            $workspaceChangeSetReceipt.result -eq 'passed' -and
            $workspaceChangeSetReceipt.minimumFiles -eq 2 -and
            $workspaceChangeSetReceipt.maximumFiles -eq 4 -and
            $workspaceChangeSetReceipt.maximumReviewBytes -eq 32768 -and
            $workspaceChangeSetReceipt.mixedReplacePatchCreateApplied -and
            -not $workspaceChangeSetReceipt.proposalMutates -and
            -not $workspaceChangeSetReceipt.wholeSetRejectMutates -and
            $workspaceChangeSetReceipt.repeatedPathRejected -and
            $workspaceChangeSetReceipt.windowsCaseAliasRejected -and
            $workspaceChangeSetReceipt.anyMemberDriftPreventsAllWrites -and
            $workspaceChangeSetReceipt.midCommitFailureRolledBack -and
            $workspaceChangeSetReceipt.preCommitCrashRecoveredBeforeTools -and
            $workspaceChangeSetReceipt.committedCrashCompletedCleanup -and
            $workspaceChangeSetReceipt.tamperedRecoveryFailedClosed -and
            -not $workspaceChangeSetReceipt.simultaneousVisibilityClaimed -and
            -not $workspaceChangeSetReceipt.shellAvailableToPi -and
            -not $workspaceChangeSetReceipt.recoveryAvailableToPi -and
            $workspaceChangeSetReceipt.liveExplorer -eq 'not-run') `
        -Detail (
            "Workspace change-set probe exit " +
            "$workspaceChangeSetExitCode; result " +
            "$workspaceChangeSetResult.")

    $reviewedIterationOutput = @(
        & $DotnetPath run `
            --project $bridgeProjectPath `
            --configuration Release `
            --no-build `
            -- `
            reviewed-iteration-probe `
            --node $NodePath `
            --sidecar $hostPath `
            --git $resolvedGitPath 2>&1
    )
    $reviewedIterationExitCode = $LASTEXITCODE
    $reviewedIterationReceipt = $null
    try {
        $reviewedIterationReceipt =
            ($reviewedIterationOutput -join [Environment]::NewLine) |
                ConvertFrom-Json
    }
    catch {
        $reviewedIterationReceipt = $null
    }
    $reviewedIterationResult = if (
        $null -ne $reviewedIterationReceipt
    ) {
        $reviewedIterationReceipt.result
    }
    else {
        'unparsed'
    }
    Add-Check `
        -Name 'runtime.reviewed-iteration-probe' `
        -Passed (
            $reviewedIterationExitCode -eq 0 -and
            $null -ne $reviewedIterationReceipt -and
            $reviewedIterationReceipt.result -eq 'passed' -and
            $reviewedIterationReceipt.cleanBaselineRequired -and
            $reviewedIterationReceipt.durableReceiptRoundTripPassed -and
            $reviewedIterationReceipt.durableReceiptCiphertextPassed -and
            $reviewedIterationReceipt.ownerPolicyPassed -and
            $reviewedIterationReceipt.firstProposalPausedForOwner -and
            $reviewedIterationReceipt.approvedEditValidated -and
            $reviewedIterationReceipt.approvedNewFileValidated -and
            $reviewedIterationReceipt.approvedPatchValidated -and
            $reviewedIterationReceipt.approvedChangeSetValidated -and
            $reviewedIterationReceipt.changeSetFileReceiptsPersisted -and
            $reviewedIterationReceipt.caseAliasedFileReceiptsRejected -and
            $reviewedIterationReceipt.proposalFreeWorkspaceReceiptsRejected -and
            $reviewedIterationReceipt.untrackedWhitespaceRejected -and
            $reviewedIterationReceipt.separateTrustedValidationApprovalRequired -and
            $reviewedIterationReceipt.trustedValidationPassed -and
            $reviewedIterationReceipt.modifiedTrustedTestRejected -and
            $reviewedIterationReceipt.trustedValidationCancellationPassed -and
            $reviewedIterationReceipt.automaticReasoningContinuationPassed -and
            $reviewedIterationReceipt.secondProposalPausedForOwner -and
            $reviewedIterationReceipt.rejectionStoppedLoop -and
            $reviewedIterationReceipt.shutdownSuspensionPassed -and
            $reviewedIterationReceipt.restartDidNotRestoreProposalCapability -and
            $reviewedIterationReceipt.explicitRearmPassed -and
            $reviewedIterationReceipt.repositoryDriftRejected -and
            -not $reviewedIterationReceipt.shellAvailableToPi -and
            -not $reviewedIterationReceipt.validationProcessAvailableToPi -and
            -not $reviewedIterationReceipt.unattendedApprovalAllowed -and
            $reviewedIterationReceipt.maximumApprovedEdits -eq 4 -and
            $reviewedIterationReceipt.policyLifetimeHours -eq 6 -and
            $reviewedIterationReceipt.durableReceiptFileCount -eq 2 -and
            $reviewedIterationReceipt.brokerRequestCount -eq 10 -and
            $reviewedIterationReceipt.brokerFaultCount -eq 0 -and
            $reviewedIterationReceipt.liveExplorer -eq 'not-run' -and
            -not $reviewedIterationReceipt.productionWorkspaceMutationPerformed) `
        -Detail (
            "Reviewed iteration probe exit $reviewedIterationExitCode; " +
            "result $reviewedIterationResult.")

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
                'read|grep|find|ls|propose_edit|propose_patch|propose_create_file|propose_change_set' -and
            (@($bridgeReceipt.deniedTools) -join '|') -eq
                'bash|edit|write' -and
            $bridgeReceipt.sessionCreationEnabled -and
            -not $bridgeReceipt.promptingEnabled -and
            -not $bridgeReceipt.sessionPersisted -and
            -not $bridgeReceipt.credentialTransportAllowed -and
            $bridgeReceipt.workspacePatchSupported -and
            $bridgeReceipt.workspacePatchMinimumHunks -eq 2 -and
            $bridgeReceipt.workspacePatchMaximumHunks -eq 8 -and
            $bridgeReceipt.workspacePatchMaximumPreviewBytes -eq 16384 -and
            $bridgeReceipt.workspaceChangeSetSupported -and
            $bridgeReceipt.workspaceChangeSetMinimumFiles -eq 2 -and
            $bridgeReceipt.workspaceChangeSetMaximumFiles -eq 4 -and
            $bridgeReceipt.workspaceChangeSetMaximumPreviewBytes -eq 32768 -and
            $bridgeReceipt.workspaceChangeSetRecoveryBeforeToolsPassed -and
            -not $bridgeReceipt.workspaceChangeSetRecoveryAvailableToModel -and
            -not $bridgeReceipt.simultaneousMultiPathVisibilityClaimed -and
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
                'quiesce-cancel-checkpoint-flush-sidecar-shutdown-broker-dispose' -and
            $desktopRuntimeReceipt.runtimeCompositionPassed -and
            $desktopRuntimeReceipt.multiTurnPassed -and
            $desktopRuntimeReceipt.toolRoundTripPassed -and
            $desktopRuntimeReceipt.workspaceEditProposalPassed -and
            $desktopRuntimeReceipt.workspaceEditApprovalPassed -and
            $desktopRuntimeReceipt.workspaceEditReplayRejected -and
            $desktopRuntimeReceipt.workspaceEditDriftRejected -and
            $desktopRuntimeReceipt.workspaceEditRejectionPassed -and
            $desktopRuntimeReceipt.workspaceEditShutdownExpirationPassed -and
            $desktopRuntimeReceipt.workspaceEditFixtureMutationPerformed -and
            $desktopRuntimeReceipt.workspaceChangeSetProposalPassed -and
            $desktopRuntimeReceipt.workspaceChangeSetApprovalPassed -and
            $desktopRuntimeReceipt.workspaceChangeSetFileReceiptsPassed -and
            $desktopRuntimeReceipt.checkpointExportPassed -and
            $desktopRuntimeReceipt.checkpointContextRestorePassed -and
            $desktopRuntimeReceipt.checkpointAdmissionPassed -and
            $desktopRuntimeReceipt.checkpointStoreRoundTripPassed -and
            $desktopRuntimeReceipt.checkpointStoreCiphertextPassed -and
            $desktopRuntimeReceipt.checkpointStoreBindingPassed -and
            $desktopRuntimeReceipt.checkpointStoreCorruptionRejected -and
            $desktopRuntimeReceipt.checkpointStoreFailureShutdownPassed -and
            $desktopRuntimeReceipt.checkpointTerminalAutosavePassed -and
            $desktopRuntimeReceipt.quiesceClosedSubmission -and
            $desktopRuntimeReceipt.shutdownCancelledActiveTurn -and
            $desktopRuntimeReceipt.orderlyShutdownPassed -and
            $desktopRuntimeReceipt.startupRollbackPassed -and
            $desktopRuntimeReceipt.credentialEnvironmentClean -and
            $desktopRuntimeReceipt.normalBrokerRequestCount -eq 4 -and
            $desktopRuntimeReceipt.resumeBrokerRequestCount -eq 1 -and
            $desktopRuntimeReceipt.abortBrokerRequestCount -eq 1 -and
            $desktopRuntimeReceipt.workspaceEditBrokerRequestCount -eq 8 -and
            $desktopRuntimeReceipt.workspaceChangeSetBrokerRequestCount -eq 2 -and
            $desktopRuntimeReceipt.exportedCheckpointTurnCount -eq 3 -and
            $desktopRuntimeReceipt.restoredCheckpointTurnCount -eq 3 -and
            $desktopRuntimeReceipt.persistedCheckpointTurnCount -eq 4 -and
            $desktopRuntimeReceipt.normalCheckpointSaveCount -eq 3 -and
            $desktopRuntimeReceipt.resumeCheckpointSaveCount -eq 1 -and
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
    desktopConversationCheckpointStore =
        'current-user-dpapi-atomic-workspace-bound'
    desktopConversationCheckpointStoreRoot =
        'local-appdata-jarvis2-pi-agent-conversations'
    desktopConversationCheckpointEnvelopeMaxBytes = 65536
    desktopConversationCheckpointSave =
        'ordered-terminal-autosave-and-shutdown-flush'
    desktopConversationCheckpointSaveTimeoutMilliseconds = 5000
    desktopConversationCheckpointFailure =
        'close-submissions-and-surface-on-shutdown'
    reviewedIterationImplemented = $true
    reviewedIterationPolicy =
        'desktop-owner-fixed-four-edits-six-hours'
    reviewedIterationContinuation =
        'automatic-only-after-separate-owner-approved-trusted-validation-pass'
    reviewedIterationStore =
        'current-user-dpapi-atomic-workspace-bound-durable-receipts'
    reviewedIterationEnvelopeMaxBytes = 262144
    reviewedIterationValidationProfile =
        'git-head-pathset-text-hash-diffcheck-structured-parse-v2'
    reviewedIterationRestart =
        'interrupted-explicit-rearm-no-proposal-or-process-restore'
    reviewedIterationTrustedValidation =
        'pinned-head-node-test-direct-no-shell-pre-post-repository-gate'
    reviewedIterationWorkspaceCodeExecution = $true
    reviewedIterationWorkspaceCodeExecutionAuthority =
        'desktop-owner-one-shot-only'
    reviewedIterationValidationProcessAvailableToPi = $false
    desktopRuntimeImplemented = $true
    desktopRuntimeOwnership =
        'desktop-owned-broker-sidecar-session-conversation'
    desktopRuntimeShutdown =
        'quiesce-cancel-checkpoint-flush-sidecar-shutdown-broker-dispose'
    asynchronousTurnsImplemented = $true
    activeTurnCancellationImplemented = $true
    sessionPersistence = 'in-memory'
    workspaceBinding = 'single-explicit-root'
    workspaceEditProposalSupported = $true
    workspaceEditProposalMutates = $false
    workspaceEditExistingFilesOnly = $false
    workspaceEditStrictUtf8 = $true
    workspaceEditSingleLinkOnly = $true
    workspaceEditApprovalOwner = 'desktop-user-only'
    workspaceEditApprovalMode =
        'one-shot-explicit-operation-before-state-sha256'
    workspacePatchSupported = $true
    workspacePatchMinimumHunks = 2
    workspacePatchMaximumHunks = 8
    workspacePatchMaximumPreviewBytes = 16384
    workspacePatchCommitMode =
        'single-file-atomic-replace-and-post-verify'
    workspaceChangeSetSupported = $true
    workspaceChangeSetMinimumFiles = 2
    workspaceChangeSetMaximumFiles = 4
    workspaceChangeSetMaximumPreviewBytes = 32768
    workspaceChangeSetCommitMode =
        'durable-before-or-after-convergence-no-simultaneous-visibility-claim'
    workspaceChangeSetRecovery =
        'strict-journal-before-tools-rollback-or-complete'
    workspaceChangeSetRecoveryAvailableToPi = $false
    simultaneousMultiPathVisibilityClaimed = $false
    workspaceEditNewFileSupported = $true
    workspaceEditNewFileMaxBytes = 16384
    workspaceEditNewFileParentPolicy =
        'existing-canonical-directory-no-auto-create'
    workspaceEditVersionControlMetadataMutation = $false
    workspaceEditDeleteSupported = $false
    reviewedSelfIteration = $true
    automaticReasoningContinuation = $true
    unattendedApproval = $false
    selfAuthoredPolicy = $false
    unattendedSelfIteration = $false
    desktopLaunchImplemented = $true
    productionProvider = 'openai-responses-opt-in'
    productionModel = 'gpt-5.6-sol'
    productionAuthenticationConfigured = $false
    providerProbeLiveModelNetworkCalled = $false
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
