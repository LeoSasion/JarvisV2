import {
  createReadOnlyAgentSession,
} from "./read-only-session.mjs";
import {
  WorkspacePolicyError,
} from "./workspace-policy.mjs";
import {
  validateDesktopBrokerPipe,
} from "./desktop-model-broker.mjs";

export function createReadyEvent(
  contract,
  runtimeReceipt,
  state,
) {
  return {
    type: "ready",
    protocol: contract.contractId,
    package: runtimeReceipt.package,
    version: runtimeReceipt.installedVersion,
    credentialEnvironmentClean:
      runtimeReceipt.credentialEnvironmentClean,
    sessionCreationEnabled: true,
    promptingEnabled: state.modelBrokerPipe !== null,
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

export function createProtocolState(options = {}) {
  const candidate = options.modelBrokerPipe;
  if (
    candidate !== undefined &&
    candidate !== null &&
    !validateDesktopBrokerPipe(candidate)
  ) {
    throw new WorkspacePolicyError(
      "invalid-model-broker-pipe",
      "The desktop model broker pipe failed admission.",
    );
  }
  return {
    sessionHandle: null,
    modelBrokerPipe: candidate ?? null,
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
  emitEvent = () => {},
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
            promptingEnabled: state.modelBrokerPipe !== null,
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
          {
            modelBrokerPipe: state.modelBrokerPipe,
          },
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
              promptingEnabled:
                state.sessionHandle.promptingEnabled,
              modelProvider:
                state.sessionHandle.modelProvider,
              modelId: state.sessionHandle.modelId,
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
    case "prompt":
      if (state.sessionHandle === null) {
        return failure(
          id,
          "prompt",
          "session-not-bound",
          "A workspace session must be admitted before prompting.",
        );
      }
      if (!state.sessionHandle.promptingEnabled) {
        return failure(
          id,
          "prompt",
          "prompting-disabled",
          "Prompting requires a desktop-owned model broker.",
        );
      }
      if (
        typeof request.text !== "string" ||
        request.text.trim().length === 0 ||
        Buffer.byteLength(request.text, "utf8") > 16_384
      ) {
        return failure(
          id,
          "prompt",
          "invalid-prompt",
          "Prompt text must contain between 1 and 16384 UTF-8 bytes.",
        );
      }
      {
        let deltaCount = 0;
        let toolExecutionCount = 0;
        let assistantStopReason = null;
        const unsubscribe =
          state.sessionHandle.session.subscribe((event) => {
            if (
              event.type === "message_update" &&
              event.assistantMessageEvent?.type === "text_delta"
            ) {
              deltaCount += 1;
              emitEvent({
                type: "event",
                event: "assistant_text_delta",
                requestId: id,
                delta: event.assistantMessageEvent.delta,
              });
            } else if (event.type === "tool_execution_start") {
              toolExecutionCount += 1;
              emitEvent({
                type: "event",
                event: "tool_execution_start",
                requestId: id,
                toolCallId: event.toolCallId,
                toolName: event.toolName,
              });
            } else if (event.type === "tool_execution_end") {
              emitEvent({
                type: "event",
                event: "tool_execution_end",
                requestId: id,
                toolCallId: event.toolCallId,
                toolName: event.toolName,
                isError: event.isError === true,
              });
            } else if (
              event.type === "message_end" &&
              event.message?.role === "assistant"
            ) {
              assistantStopReason = event.message.stopReason;
            }
          });
        try {
          await state.sessionHandle.session.prompt(request.text);
          await state.sessionHandle.session.waitForIdle();
          if (
            assistantStopReason === null ||
            assistantStopReason === "error" ||
            assistantStopReason === "aborted"
          ) {
            return failure(
              id,
              "prompt",
              "prompt-failed",
              "The desktop-brokered Pi prompt failed closed.",
            );
          }
          return {
            response: {
              type: "response",
              id,
              command: "prompt",
              success: true,
              data: {
                status: "completed",
                deltaCount,
                toolExecutionCount,
              },
            },
            shutdown: false,
          };
        } catch {
          return failure(
            id,
            "prompt",
            "prompt-failed",
            "The desktop-brokered Pi prompt failed closed.",
          );
        } finally {
          unsubscribe();
        }
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
