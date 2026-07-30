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
    activeTurn: null,
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
            conversationCheckpoint:
              "bounded-completed-text-context-restore",
            conversationCheckpointMaxTurns: 32,
            conversationCheckpointMaxBytes: 32_768,
            conversationCheckpointMaxTextBytes: 16_384,
            conversationCheckpointPersistence:
              "desktop-owned-external",
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
            conversationCheckpoint:
              request.conversationCheckpoint,
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
              restoredTurnCount:
                state.sessionHandle.restoredTurnCount,
              restoredContextMessageCount:
                state.sessionHandle
                  .restoredContextMessageCount,
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
    case "start_turn":
      if (state.sessionHandle === null) {
        return failure(
          id,
          "start_turn",
          "session-not-bound",
          "A workspace session must be admitted before prompting.",
        );
      }
      if (!state.sessionHandle.promptingEnabled) {
        return failure(
          id,
          "start_turn",
          "prompting-disabled",
          "Prompting requires a desktop-owned model broker.",
        );
      }
      if (state.activeTurn !== null) {
        return failure(
          id,
          "start_turn",
          "turn-already-active",
          "Only one Pi Agent turn may run at a time.",
        );
      }
      if (
        typeof request.text !== "string" ||
        request.text.trim().length === 0 ||
        Buffer.byteLength(request.text, "utf8") > 16_384
      ) {
        return failure(
          id,
          "start_turn",
          "invalid-prompt",
          "Prompt text must contain between 1 and 16384 UTF-8 bytes.",
        );
      }
      {
        const turn = {
          id,
          completion: null,
        };
        state.activeTurn = turn;
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
                requestId: turn.id,
                delta: event.assistantMessageEvent.delta,
              });
            } else if (event.type === "tool_execution_start") {
              toolExecutionCount += 1;
              emitEvent({
                type: "event",
                event: "tool_execution_start",
                requestId: turn.id,
                toolCallId: event.toolCallId,
                toolName: event.toolName,
              });
            } else if (event.type === "tool_execution_end") {
              emitEvent({
                type: "event",
                event: "tool_execution_end",
                requestId: turn.id,
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
        turn.completion = (async () => {
          let terminal;
          try {
            await state.sessionHandle.session.prompt(request.text);
            await state.sessionHandle.session.waitForIdle();
            const success = ![
              null,
              "error",
              "aborted",
            ].includes(assistantStopReason);
            terminal = {
              type: "event",
              event: "turn_completed",
              requestId: turn.id,
              success,
              status: success
                ? "completed"
                : assistantStopReason === "aborted"
                  ? "aborted"
                  : "failed",
              stopReason: assistantStopReason,
              deltaCount,
              toolExecutionCount,
              ...(success ? {} : {
                error: {
                  code: assistantStopReason === "aborted"
                    ? "turn-aborted"
                    : "prompt-failed",
                  message: assistantStopReason === "aborted"
                    ? "The Pi Agent turn was aborted."
                    : "The desktop-brokered Pi prompt failed closed.",
                },
              }),
            };
          } catch {
            terminal = {
              type: "event",
              event: "turn_completed",
              requestId: turn.id,
              success: false,
              status: "failed",
              stopReason: assistantStopReason,
              deltaCount,
              toolExecutionCount,
              error: {
                code: "prompt-failed",
                message:
                  "The desktop-brokered Pi prompt failed closed.",
              },
            };
          } finally {
            unsubscribe();
          }
          try {
            emitEvent(terminal);
          } finally {
            if (state.activeTurn === turn) {
              state.activeTurn = null;
            }
          }
        })();
        return {
          response: {
            type: "response",
            id,
            command: "start_turn",
            success: true,
            data: {
              turnId: turn.id,
              status: "started",
            },
          },
          shutdown: false,
        };
      }
    case "abort_turn":
      if (
        state.activeTurn === null ||
        request.turnId !== state.activeTurn.id
      ) {
        return failure(
          id,
          "abort_turn",
          "turn-not-active",
          "The requested Pi Agent turn is not active.",
        );
      }
      {
        const activeTurn = state.activeTurn;
        try {
          await state.sessionHandle.session.abort();
          return {
            response: {
              type: "response",
              id,
              command: "abort_turn",
              success: true,
              data: {
                turnId: activeTurn.id,
                status: "abort-requested",
              },
            },
            shutdown: false,
          };
        } catch {
          return failure(
            id,
            "abort_turn",
            "abort-failed",
            "The Pi Agent turn could not be aborted cleanly.",
          );
        }
      }
    case "shutdown":
      if (state.activeTurn !== null) {
        const activeTurn = state.activeTurn;
        try {
          await state.sessionHandle.session.abort();
          await activeTurn.completion;
        } catch {
          // Shutdown still disposes the owned in-memory session below.
        }
      }
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

export async function disposeProtocolState(state) {
  if (state.activeTurn !== null) {
    const activeTurn = state.activeTurn;
    try {
      await state.sessionHandle?.session.abort();
      await activeTurn.completion;
    } catch {
      // Disposal remains scoped to the owned in-memory session.
    }
  }
  state.sessionHandle?.session.dispose();
  state.sessionHandle = null;
  state.activeTurn = null;
}
