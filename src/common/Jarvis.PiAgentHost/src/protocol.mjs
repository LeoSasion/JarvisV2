import {
  createReadOnlyAgentSession,
} from "./read-only-session.mjs";
import {
  WorkspacePolicyError,
} from "./workspace-policy.mjs";

export function createReadyEvent(contract, runtimeReceipt) {
  return {
    type: "ready",
    protocol: contract.contractId,
    package: runtimeReceipt.package,
    version: runtimeReceipt.installedVersion,
    credentialEnvironmentClean:
      runtimeReceipt.credentialEnvironmentClean,
    sessionCreationEnabled: true,
    promptingEnabled: false,
  };
}

const forbiddenCredentialFields = new Set([
  "apikey",
  "authorization",
  "credential",
  "credentials",
  "password",
  "secret",
  "token",
  "accesstoken",
  "refreshtoken",
]);

function containsCredentialField(value) {
  if (value === null || typeof value !== "object") {
    return false;
  }
  if (Array.isArray(value)) {
    return value.some(containsCredentialField);
  }
  return Object.entries(value).some(([key, nested]) => {
    const normalized = key.replaceAll(/[-_]/g, "").toLowerCase();
    return (
      forbiddenCredentialFields.has(normalized) ||
      containsCredentialField(nested)
    );
  });
}

export function createProtocolState() {
  return {
    sessionHandle: null,
  };
}

function failure(id, command, code, message) {
  return {
    response: {
      type: "response",
      id,
      command,
      success: false,
      error: { code, message },
    },
    shutdown: false,
  };
}

export async function handleRequest(
  request,
  contract,
  runtimeReceipt,
  state,
) {
  const id =
    typeof request?.id === "string" ? request.id : null;
  if (containsCredentialField(request)) {
    return failure(
      id,
      typeof request?.type === "string"
        ? request.type
        : "invalid",
      "credential-field-forbidden",
      "Credential fields are forbidden on the desktop host transport.",
    );
  }

  switch (request?.type) {
    case "hello":
      return {
        response: {
          type: "response",
          id,
          command: "hello",
          success: true,
          protocol: contract.contractId,
          runtime: runtimeReceipt.installedVersion,
        },
        shutdown: false,
      };
    case "capabilities":
      return {
        response: {
          type: "response",
          id,
          command: "capabilities",
          success: true,
          data: {
            integrationMode: contract.runtime.integrationMode,
            initialTools: [...contract.tools.initialAllowlist],
            deniedTools: [...contract.tools.initiallyDenied],
            sessionCreationEnabled: true,
            promptingEnabled: false,
            sessionPersistence: "in-memory",
            workspaceBinding: "single-explicit-root",
            resourceDiscoveryEnabled: false,
            modelNetworkAllowed: false,
            credentialTransportAllowed: false,
            shellMutationSupported: false,
            explorerMutationSupported: false,
            activationPermitted: false,
          },
        },
        shutdown: false,
      };
    case "start_session":
      if (state.sessionHandle !== null) {
        return failure(
          id,
          "start_session",
          "session-already-bound",
          "This sidecar is already bound to one workspace.",
        );
      }
      try {
        state.sessionHandle = await createReadOnlyAgentSession(
          request.workspaceRoot,
        );
        return {
          response: {
            type: "response",
            id,
            command: "start_session",
            success: true,
            data: {
              workspaceRoot:
                state.sessionHandle.admission.canonicalRoot,
              activeTools: state.sessionHandle.activeTools,
              sessionPersisted: state.sessionHandle.persisted,
              modelSelected:
                state.sessionHandle.modelSelected,
              promptingEnabled: false,
              resourceDiscoveryEnabled: false,
              modelNetworkAllowed: false,
            },
          },
          shutdown: false,
        };
      } catch (error) {
        state.sessionHandle = null;
        if (error instanceof WorkspacePolicyError) {
          return failure(
            id,
            "start_session",
            error.code,
            error.message,
          );
        }
        return failure(
          id,
          "start_session",
          "session-admission-failed",
          "The read-only Pi Agent session failed closed during admission.",
        );
      }
    case "shutdown":
      return {
        response: {
          type: "response",
          id,
          command: "shutdown",
          success: true,
        },
        shutdown: true,
      };
    default:
      return failure(
        id,
        typeof request?.type === "string"
          ? request.type
          : "invalid",
        "unsupported-request",
        "The request type is not in the host allowlist.",
      );
  }
}

export function disposeProtocolState(state) {
  state.sessionHandle?.session.dispose();
  state.sessionHandle = null;
}
