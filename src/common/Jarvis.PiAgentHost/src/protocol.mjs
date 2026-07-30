export function createReadyEvent(contract, runtimeReceipt) {
  return {
    type: "ready",
    protocol: contract.contractId,
    package: runtimeReceipt.package,
    version: runtimeReceipt.installedVersion,
    credentialEnvironmentClean:
      runtimeReceipt.credentialEnvironmentClean,
    sessionCreationEnabled: false,
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

export function handleRequest(request, contract, runtimeReceipt) {
  const id =
    typeof request?.id === "string" ? request.id : null;
  if (containsCredentialField(request)) {
    return {
      response: {
        type: "response",
        id,
        command:
          typeof request?.type === "string"
            ? request.type
            : "invalid",
        success: false,
        error: {
          code: "credential-field-forbidden",
          message:
            "Credential fields are forbidden on the desktop host transport.",
        },
      },
      shutdown: false,
    };
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
            sessionCreationEnabled: false,
            credentialTransportAllowed: false,
            shellMutationSupported: false,
            explorerMutationSupported: false,
            activationPermitted: false,
          },
        },
        shutdown: false,
      };
    case "start_session":
      return {
        response: {
          type: "response",
          id,
          command: "start_session",
          success: false,
          error: {
            code: "policy-disabled",
            message:
              "Pi Agent session creation is disabled until the desktop policy gate is implemented.",
          },
        },
        shutdown: false,
      };
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
      return {
        response: {
          type: "response",
          id,
          command:
            typeof request?.type === "string"
              ? request.type
              : "invalid",
          success: false,
          error: {
            code: "unsupported-request",
            message: "The request type is not in the host allowlist.",
          },
        },
        shutdown: false,
      };
  }
}
