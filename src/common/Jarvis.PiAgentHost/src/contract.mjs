import { readFile } from "node:fs/promises";

const contractUrls = [
  new URL("../config/pi-agent-desktop-host-contract.json", import.meta.url),
  new URL(
    "../../../../config/pi-agent-desktop-host-contract.json",
    import.meta.url,
  ),
];

export async function loadContract() {
  let contractText;
  for (const contractUrl of contractUrls) {
    try {
      contractText = await readFile(contractUrl, "utf8");
      break;
    } catch (error) {
      if (error?.code !== "ENOENT") {
        throw error;
      }
    }
  }
  if (contractText === undefined) {
    throw new Error("The Pi Agent desktop host contract is missing.");
  }
  const contract = JSON.parse(contractText);
  validateContract(contract);
  return contract;
}

export function validateContract(contract) {
  const failures = [];
  if (contract.schemaVersion !== 1) {
    failures.push("schemaVersion");
  }
  if (contract.contractId !== "jarvisv2-pi-agent-desktop-host-v1") {
    failures.push("contractId");
  }
  if (
    contract.upstream?.package !==
      "@earendil-works/pi-coding-agent" ||
    contract.upstream?.exactVersion !== "0.82.1"
  ) {
    failures.push("upstream");
  }
  if (
    contract.runtime?.nodeMinimumMajor < 22 ||
    contract.runtime?.integrationMode !== "sdk-sidecar-jsonl" ||
    contract.runtime?.sdkImportModel !==
      "pinned-package-core-module-adapter" ||
    contract.runtime?.launchState !==
      "read-only-session-admission" ||
    contract.runtime?.piOfflineRequired !== true ||
    contract.runtime?.sessionCreationEnabled !== true ||
    contract.runtime?.desktopLaunchImplemented !== true
  ) {
    failures.push("runtime");
  }
  if (
    contract.transport?.framing !== "lf-delimited-jsonl" ||
    contract.transport?.encoding !== "utf-8" ||
    contract.transport?.maxFrameBytes !== 65_536 ||
    contract.transport?.requestTypes?.join("|") !==
      "hello|capabilities|start_session|start_turn|" +
        "abort_turn|shutdown" ||
    contract.transport?.credentialFieldsAllowed !== false
  ) {
    failures.push("transport");
  }
  if (
    contract.session?.enabled !== true ||
    contract.session?.promptingEnabled !==
      "desktop-broker-required" ||
    contract.session?.modelAuthentication !==
      "desktop-process-only" ||
    contract.session?.modelTransport !== "local-named-pipe" ||
    contract.session?.modelBrokerProtocol !==
      "jarvisv2-pi-model-broker-v1" ||
    contract.session?.modelBrokerLifetime !==
      "desktop-owned-multi-request" ||
    contract.session?.modelBrokerMaxFrameBytes !== 1_048_576 ||
    contract.session?.modelBrokerMaxConcurrentConnections !== 4 ||
    contract.session?.desktopTurnEventStream !==
      "bounded-ordered-single-consumer" ||
    contract.session?.desktopTurnEventBufferCapacity !== 512 ||
    contract.session?.desktopTurnEventBackpressurePolicy !==
      "fail-closed-at-request-timeout" ||
    contract.session?.desktopConversationStateModel !==
      "immutable-revisioned-single-active-turn" ||
    contract.session?.desktopConversationRetainedTurns !== 128 ||
    contract.session?.desktopConversationMaxAssistantCharacters !== 262_144 ||
    contract.session?.desktopConversationNotificationDispatch !==
      "captured-synchronization-context" ||
    contract.session?.desktopConversationCheckpoint !==
      "bounded-completed-text-context-restore" ||
    contract.session?.desktopConversationCheckpointMaxTurns !== 32 ||
    contract.session?.desktopConversationCheckpointMaxBytes !== 32_768 ||
    contract.session?.desktopConversationCheckpointMaxTextBytes !==
      16_384 ||
    contract.session?.desktopConversationCheckpointPersistence !==
      "desktop-owned-external" ||
    contract.session?.desktopConversationCheckpointStore !==
      "current-user-dpapi-atomic-workspace-bound" ||
    contract.session?.desktopConversationCheckpointStoreRoot !==
      "local-appdata-jarvis2-pi-agent-conversations" ||
    contract.session?.desktopConversationCheckpointEnvelopeMaxBytes !==
      65_536 ||
    contract.session?.desktopConversationCheckpointSave !==
      "ordered-terminal-autosave-and-shutdown-flush" ||
    contract.session
      ?.desktopConversationCheckpointSaveTimeoutMilliseconds !== 5_000 ||
    contract.session?.desktopConversationCheckpointFailure !==
      "close-submissions-and-surface-on-shutdown" ||
    contract.session?.credentialTransport !== "forbidden" ||
    contract.session?.persistence !== "in-memory" ||
    contract.session?.workspaceBinding !==
      "single-explicit-root" ||
    contract.session?.resourceDiscovery !== "disabled" ||
    contract.session?.modelNetworkAllowed !== false
  ) {
    failures.push("session");
  }
  if (
    contract.desktopProvider?.mode !== "opt-in" ||
    contract.desktopProvider?.implementation !==
      "openai-responses-api" ||
    contract.desktopProvider?.endpoint !==
      "https://api.openai.com/v1/responses" ||
    contract.desktopProvider?.model !== "gpt-5.6-sol" ||
    contract.desktopProvider?.reasoningEffort !== "medium" ||
    contract.desktopProvider?.streaming !== "server-sent-events" ||
    contract.desktopProvider?.responseStorage !== false ||
    contract.desktopProvider?.networkOwner !== "desktop-process-only" ||
    contract.desktopProvider?.credentialStore !==
      "current-user-dpapi-atomic" ||
    contract.desktopProvider?.credentialRoot !==
      "local-appdata-jarvis2-credentials" ||
    contract.desktopProvider?.ambientCredentialAllowed !== false ||
    contract.desktopProvider?.credentialTransportToSidecar !== false
  ) {
    failures.push("desktopProvider");
  }
  if (
    contract.tools?.initialAllowlist?.join("|") !==
      "read|grep|find|ls" ||
    contract.tools?.initiallyDenied?.join("|") !==
      "bash|edit|write" ||
    contract.tools?.unattendedSelfIteration !== false
  ) {
    failures.push("tools");
  }
  if (
    contract.boundaries?.shellMutationSupported !== false ||
    contract.boundaries?.explorerMutationSupported !== false ||
    contract.boundaries?.systemMutationSupported !== false ||
    contract.boundaries?.deviceIntegrationSupported !== false ||
    contract.boundaries?.activationPermitted !== false ||
    contract.boundaries?.liveExplorer !== "not-run"
  ) {
    failures.push("boundaries");
  }
  if (failures.length !== 0) {
    throw new Error(
      `Pi Agent host contract rejected: ${failures.join(", ")}`,
    );
  }
}
