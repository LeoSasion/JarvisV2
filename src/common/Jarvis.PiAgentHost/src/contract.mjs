import { readFile } from "node:fs/promises";

const contractUrl = new URL(
  "../../../../config/pi-agent-desktop-host-contract.json",
  import.meta.url,
);

export async function loadContract() {
  const contract = JSON.parse(await readFile(contractUrl, "utf8"));
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
