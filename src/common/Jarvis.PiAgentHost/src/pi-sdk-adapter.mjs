const packageName = "@earendil-works/pi-coding-agent";
const packageEntry = import.meta.resolve(packageName);
const packageEntryUrl = new URL(packageEntry);

if (
  packageEntryUrl.protocol !== "file:" ||
  !packageEntryUrl.pathname.endsWith("/dist/index.js")
) {
  throw new Error(
    "The pinned Pi SDK package entry did not match the reviewed layout.",
  );
}

const [
  sdk,
  extensions,
  modelRuntime,
  sessionManager,
  settingsManager,
  tools,
] = await Promise.all([
  import(new URL("./core/sdk.js", packageEntryUrl)),
  import(new URL("./core/extensions/index.js", packageEntryUrl)),
  import(new URL("./core/model-runtime.js", packageEntryUrl)),
  import(new URL("./core/session-manager.js", packageEntryUrl)),
  import(new URL("./core/settings-manager.js", packageEntryUrl)),
  import(new URL("./core/tools/index.js", packageEntryUrl)),
]);

export const createAgentSession = sdk.createAgentSession;
export const createAgentSessionRuntime =
  sdk.createAgentSessionRuntime;
export const createExtensionRuntime =
  extensions.createExtensionRuntime;
export const ModelRuntime = modelRuntime.ModelRuntime;
export const SessionManager = sessionManager.SessionManager;
export const SettingsManager = settingsManager.SettingsManager;
export const createFindToolDefinition =
  tools.createFindToolDefinition;
export const createGrepToolDefinition =
  tools.createGrepToolDefinition;
export const createLsToolDefinition =
  tools.createLsToolDefinition;
export const createReadToolDefinition =
  tools.createReadToolDefinition;

for (const [name, value] of Object.entries({
  createAgentSession,
  createAgentSessionRuntime,
  createExtensionRuntime,
  ModelRuntime,
  SessionManager,
  SettingsManager,
  createFindToolDefinition,
  createGrepToolDefinition,
  createLsToolDefinition,
  createReadToolDefinition,
})) {
  if (typeof value !== "function") {
    throw new Error(
      `The pinned Pi SDK core adapter is missing ${name}.`,
    );
  }
}
