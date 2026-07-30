import {
  createAgentSession,
  createExtensionRuntime,
  createFindToolDefinition,
  createGrepToolDefinition,
  createLsToolDefinition,
  createReadToolDefinition,
  ModelRuntime,
  SessionManager,
  SettingsManager,
} from "@earendil-works/pi-coding-agent";
import {
  lstat,
  readFile,
  readdir,
} from "node:fs/promises";
import {
  join,
  relative,
  sep,
} from "node:path";
import {
  admitWorkspaceRoot,
  assertWorkspacePath,
  WorkspacePolicyError,
} from "./workspace-policy.mjs";
import {
  brokerModelId,
  brokerProviderId,
  registerDesktopBrokerProvider,
} from "./desktop-model-broker.mjs";

const maximumSearchFiles = 10_000;
const maximumFileBytes = 1_048_576;
const maximumOutputBytes = 65_536;
const maximumCheckpointTurns = 32;
const maximumCheckpointBytes = 32_768;
const maximumCheckpointTextBytes = 16_384;
const checkpointTurnIdPattern =
  /^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$/u;

function textResult(text) {
  return {
    content: [{ type: "text", text }],
    details: undefined,
  };
}

function throwIfAborted(signal) {
  if (signal?.aborted) {
    throw new Error("Operation aborted");
  }
}

function hasExactKeys(value, expectedKeys) {
  if (
    value === null ||
    typeof value !== "object" ||
    Array.isArray(value)
  ) {
    return false;
  }
  const actualKeys = Object.keys(value).sort();
  return (
    actualKeys.length === expectedKeys.length &&
    expectedKeys.every((key, index) => key === actualKeys[index])
  );
}

function validCheckpointText(value) {
  return (
    typeof value === "string" &&
    value.trim().length > 0 &&
    Buffer.byteLength(value, "utf8") <= maximumCheckpointTextBytes
  );
}

function admitConversationCheckpoint(checkpoint) {
  if (checkpoint === undefined || checkpoint === null) {
    return [];
  }
  if (
    !hasExactKeys(checkpoint, ["schemaVersion", "turns"]) ||
    checkpoint.schemaVersion !== 1 ||
    !Array.isArray(checkpoint.turns) ||
    checkpoint.turns.length > maximumCheckpointTurns ||
    Buffer.byteLength(
      JSON.stringify(checkpoint),
      "utf8",
    ) > maximumCheckpointBytes
  ) {
    throw new WorkspacePolicyError(
      "invalid-conversation-checkpoint",
      "The desktop conversation checkpoint failed its schema or size boundary.",
    );
  }

  const turnIds = new Set();
  for (const turn of checkpoint.turns) {
    if (
      !hasExactKeys(
        turn,
        ["assistantText", "turnId", "userText"],
      ) ||
      typeof turn.turnId !== "string" ||
      !checkpointTurnIdPattern.test(turn.turnId) ||
      turnIds.has(turn.turnId) ||
      !validCheckpointText(turn.userText) ||
      !validCheckpointText(turn.assistantText)
    ) {
      throw new WorkspacePolicyError(
        "invalid-conversation-checkpoint",
        "The desktop conversation checkpoint contains an invalid text turn.",
      );
    }
    turnIds.add(turn.turnId);
  }
  return checkpoint.turns.map((turn) => ({ ...turn }));
}

function emptyCheckpointUsage() {
  return {
    input: 0,
    output: 0,
    cacheRead: 0,
    cacheWrite: 0,
    totalTokens: 0,
    cost: {
      input: 0,
      output: 0,
      cacheRead: 0,
      cacheWrite: 0,
      total: 0,
    },
  };
}

function seedConversationCheckpoint(
  sessionManager,
  checkpointTurns,
  model,
) {
  if (checkpointTurns.length === 0) {
    return;
  }
  if (!model) {
    throw new WorkspacePolicyError(
      "checkpoint-requires-model-broker",
      "Conversation checkpoint restore requires the desktop model broker.",
    );
  }

  const firstTimestamp = Math.max(
    0,
    Date.now() - checkpointTurns.length * 2,
  );
  checkpointTurns.forEach((turn, index) => {
    const timestamp = firstTimestamp + index * 2;
    sessionManager.appendMessage({
      role: "user",
      content: turn.userText,
      timestamp,
    });
    sessionManager.appendMessage({
      role: "assistant",
      content: [{
        type: "text",
        text: turn.assistantText,
      }],
      api: model.api,
      provider: model.provider,
      model: model.id,
      usage: emptyCheckpointUsage(),
      stopReason: "stop",
      timestamp: timestamp + 1,
    });
  });
}

function createInertResourceLoader() {
  const extensions = {
    extensions: [],
    errors: [],
    runtime: createExtensionRuntime(),
  };
  return {
    getExtensions: () => extensions,
    getSkills: () => ({ skills: [], diagnostics: [] }),
    getPrompts: () => ({ prompts: [], diagnostics: [] }),
    getThemes: () => ({ themes: [], diagnostics: [] }),
    getAgentsFiles: () => ({ agentsFiles: [] }),
    getSystemPrompt: () =>
      "JARVIS desktop read-only workspace session. " +
      "Mutation and paths outside the admitted root are unavailable.",
    getAppendSystemPrompt: () => [],
    extendResources: () => {
      throw new WorkspacePolicyError(
        "resource-discovery-disabled",
        "Dynamic resource discovery is disabled.",
      );
    },
    reload: async () => {},
  };
}

function createCredentialDenyStore() {
  return {
    read: async () => undefined,
    list: async () => [],
    modify: async () => {
      throw new WorkspacePolicyError(
        "credential-store-disabled",
        "Credential mutation is disabled.",
      );
    },
    delete: async () => {
      throw new WorkspacePolicyError(
        "credential-store-disabled",
        "Credential mutation is disabled.",
      );
    },
  };
}

function wildcardMatcher(pattern) {
  if (
    typeof pattern !== "string" ||
    pattern.length === 0 ||
    pattern.length > 512
  ) {
    throw new WorkspacePolicyError(
      "invalid-search-pattern",
      "Search patterns must contain between 1 and 512 characters.",
    );
  }
  const escaped = pattern
    .replaceAll("\\", "/")
    .replace(/[.+^${}()|[\]\\]/gu, "\\$&")
    .replaceAll("**", "\u0000")
    .replaceAll("*", "[^/]*")
    .replaceAll("?", "[^/]")
    .replaceAll("\u0000", ".*");
  return new RegExp(`^${escaped}$`, "iu");
}

function matchesWildcard(matcher, pattern, relativePath) {
  const normalizedPattern = pattern.replaceAll("\\", "/");
  const candidate = normalizedPattern.includes("/")
    ? relativePath
    : relativePath.split("/").at(-1);
  return matcher.test(candidate);
}

async function walkWorkspace(admission, startPath, signal) {
  const results = [];
  const pending = [startPath];
  while (pending.length > 0 && results.length < maximumSearchFiles) {
    throwIfAborted(signal);
    const current = pending.shift();
    const entries = await readdir(current, { withFileTypes: true });
    entries.sort((left, right) =>
      left.name.localeCompare(right.name, undefined, {
        sensitivity: "base",
      }),
    );
    for (const entry of entries) {
      throwIfAborted(signal);
      if (
        entry.name === ".git" ||
        entry.name === "node_modules"
      ) {
        continue;
      }
      const candidate = join(current, entry.name);
      if (entry.isSymbolicLink()) {
        continue;
      }
      await assertWorkspacePath(admission, candidate);
      const relativePath = relative(
        admission.canonicalRoot,
        candidate,
      ).split(sep).join("/");
      results.push({
        absolutePath: candidate,
        relativePath,
        directory: entry.isDirectory(),
      });
      if (entry.isDirectory()) {
        pending.push(candidate);
      }
      if (results.length >= maximumSearchFiles) {
        break;
      }
    }
  }
  return results;
}

function createReadTool(admission) {
  const base = createReadToolDefinition(
    admission.canonicalRoot,
  );
  return {
    ...base,
    description:
      "Read UTF-8 text inside the admitted workspace. " +
      "Binary files and paths outside the root are rejected.",
    async execute(
      _toolCallId,
      { path, offset = 1, limit = 2_000 },
      signal,
    ) {
      throwIfAborted(signal);
      const safePath = await assertWorkspacePath(
        admission,
        path,
      );
      const stats = await lstat(safePath);
      if (!stats.isFile()) {
        throw new WorkspacePolicyError(
          "workspace-path-not-file",
          "The read tool accepts files only.",
        );
      }
      if (stats.size > maximumFileBytes) {
        throw new WorkspacePolicyError(
          "workspace-file-too-large",
          "The read tool limit is one MiB per file.",
        );
      }
      const content = await readFile(safePath, "utf8");
      if (content.includes("\u0000")) {
        throw new WorkspacePolicyError(
          "binary-file-forbidden",
          "The read tool accepts UTF-8 text files only.",
        );
      }
      const start = Math.max(0, Math.trunc(offset) - 1);
      const count = Math.max(1, Math.min(
        Math.trunc(limit),
        2_000,
      ));
      const lines = content.split(/\r?\n/u);
      const selected = lines.slice(start, start + count);
      const output = selected.length === 0
        ? "(offset beyond end of file)"
        : selected
          .map((line, index) => `${start + index + 1}: ${line}`)
          .join("\n");
      const encoded = Buffer.from(output, "utf8");
      return textResult(
        encoded.byteLength <= maximumOutputBytes
          ? output
          : `${encoded.subarray(0, maximumOutputBytes).toString("utf8")}\n[Output truncated]`,
      );
    },
  };
}

function createLsTool(admission) {
  return createLsToolDefinition(admission.canonicalRoot, {
    operations: {
      exists: async (absolutePath) => {
        try {
          await assertWorkspacePath(admission, absolutePath);
          return true;
        } catch (error) {
          if (
            error instanceof WorkspacePolicyError &&
            error.code === "workspace-path-not-found"
          ) {
            return false;
          }
          throw error;
        }
      },
      stat: async (absolutePath) => {
        const safePath = await assertWorkspacePath(
          admission,
          absolutePath,
        );
        return lstat(safePath);
      },
      readdir: async (absolutePath) => {
        const safePath = await assertWorkspacePath(
          admission,
          absolutePath,
        );
        return readdir(safePath);
      },
    },
  });
}

function createFindTool(admission) {
  return createFindToolDefinition(admission.canonicalRoot, {
    operations: {
      exists: async (absolutePath) => {
        try {
          await assertWorkspacePath(admission, absolutePath);
          return true;
        } catch (error) {
          if (
            error instanceof WorkspacePolicyError &&
            error.code === "workspace-path-not-found"
          ) {
            return false;
          }
          throw error;
        }
      },
      glob: async (pattern, searchRoot, options) => {
        const safeRoot = await assertWorkspacePath(
          admission,
          searchRoot,
        );
        const matcher = wildcardMatcher(pattern);
        const entries = await walkWorkspace(
          admission,
          safeRoot,
        );
        return entries
          .filter((entry) => {
            const relativePath = relative(
              safeRoot,
              entry.absolutePath,
            ).split(sep).join("/");
            return matchesWildcard(
              matcher,
              pattern,
              relativePath,
            );
          })
          .slice(0, options.limit)
          .map((entry) => entry.absolutePath);
      },
    },
  });
}

function createGrepTool(admission) {
  const base = createGrepToolDefinition(
    admission.canonicalRoot,
  );
  return {
    ...base,
    description:
      "Search UTF-8 text files inside the admitted workspace. " +
      "Patterns are treated as literal text.",
    async execute(
      _toolCallId,
      {
        pattern,
        path = ".",
        glob,
        ignoreCase = false,
        limit = 100,
      },
      signal,
    ) {
      throwIfAborted(signal);
      if (
        typeof pattern !== "string" ||
        pattern.length === 0 ||
        pattern.length > 512
      ) {
        throw new WorkspacePolicyError(
          "invalid-search-pattern",
          "Search text must contain between 1 and 512 characters.",
        );
      }
      const safePath = await assertWorkspacePath(
        admission,
        path,
      );
      const stats = await lstat(safePath);
      const matcher = glob ? wildcardMatcher(glob) : null;
      const effectiveLimit = Number.isFinite(limit)
        ? Math.max(1, Math.min(Math.trunc(limit), 500))
        : 100;
      const candidates = stats.isDirectory()
        ? (await walkWorkspace(admission, safePath, signal))
          .filter((entry) => !entry.directory)
        : [{
          absolutePath: safePath,
          relativePath: relative(
            admission.canonicalRoot,
            safePath,
          ).split(sep).join("/"),
          directory: false,
        }];
      const needle = ignoreCase ? pattern.toLowerCase() : pattern;
      const matches = [];
      for (const candidate of candidates) {
        throwIfAborted(signal);
        const relativeToSearch = relative(
          stats.isDirectory() ? safePath : admission.canonicalRoot,
          candidate.absolutePath,
        ).split(sep).join("/");
        if (
          matcher &&
          !matchesWildcard(matcher, glob, relativeToSearch)
        ) {
          continue;
        }
        const candidateStats = await lstat(candidate.absolutePath);
        if (candidateStats.size > maximumFileBytes) {
          continue;
        }
        let content;
        try {
          content = await readFile(candidate.absolutePath, "utf8");
        } catch {
          continue;
        }
        if (content.includes("\u0000")) {
          continue;
        }
        const lines = content.split(/\r?\n/u);
        for (let index = 0; index < lines.length; index += 1) {
          const haystack = ignoreCase
            ? lines[index].toLowerCase()
            : lines[index];
          if (haystack.includes(needle)) {
            matches.push(
              `${candidate.relativePath}:${index + 1}:${lines[index]}`,
            );
            if (matches.length >= effectiveLimit) {
              break;
            }
          }
        }
        if (matches.length >= effectiveLimit) {
          break;
        }
      }
      const output = matches.length === 0
        ? "No matches found"
        : matches.join("\n");
      const encoded = Buffer.from(output, "utf8");
      return textResult(
        encoded.byteLength <= maximumOutputBytes
          ? output
          : `${encoded.subarray(0, maximumOutputBytes).toString("utf8")}\n[Output truncated]`,
      );
    },
  };
}

export async function createReadOnlyAgentSession(
  workspaceRoot,
  options = {},
) {
  const checkpointTurns = admitConversationCheckpoint(
    options.conversationCheckpoint,
  );
  const admission = await admitWorkspaceRoot(workspaceRoot);
  const modelRuntime = await ModelRuntime.create({
    credentials: createCredentialDenyStore(),
    modelsPath: null,
    allowModelNetwork: false,
  });
  const model = options.modelBrokerPipe
    ? registerDesktopBrokerProvider(
      modelRuntime,
      options.modelBrokerPipe,
    )
    : undefined;
  const settingsManager = SettingsManager.inMemory({
    compaction: { enabled: false },
    retry: { enabled: false },
    enableAnalytics: false,
    enableInstallTelemetry: false,
    images: {
      autoResize: false,
      blockImages: true,
    },
  }, {
    projectTrusted: false,
  });
  const sessionManager = SessionManager.inMemory(
    admission.canonicalRoot,
  );
  seedConversationCheckpoint(
    sessionManager,
    checkpointTurns,
    model,
  );
  const tools = [
    createReadTool(admission),
    createGrepTool(admission),
    createFindTool(admission),
    createLsTool(admission),
  ];
  const result = await createAgentSession({
    cwd: admission.canonicalRoot,
    ...(model ? { model } : {}),
    modelRuntime,
    tools: ["read", "grep", "find", "ls"],
    excludeTools: ["bash", "edit", "write"],
    customTools: tools,
    resourceLoader: createInertResourceLoader(),
    sessionManager,
    settingsManager,
    thinkingLevel: "off",
  });
  const activeTools = result.session.getActiveToolNames();
  const persisted = result.session.sessionManager.isPersisted();
  const promptingEnabled = model !== undefined;
  const restoredContextMessageCount =
    checkpointTurns.length * 2;
  const modelBoundaryPreserved = !promptingEnabled || (
    result.session.model?.provider === brokerProviderId &&
    result.session.model?.id === brokerModelId
  );
  if (
    activeTools.join("|") !== "read|grep|find|ls" ||
    persisted ||
    result.session.messages.length !==
      restoredContextMessageCount ||
    !modelBoundaryPreserved
  ) {
    result.session.dispose();
    throw new WorkspacePolicyError(
      "session-policy-invariant-failed",
      "The Pi SDK session did not preserve the reviewed tool or persistence boundary.",
    );
  }
  return {
    admission,
    session: result.session,
    activeTools,
    persisted,
    modelSelected: typeof result.session.model !== "undefined",
    promptingEnabled,
    modelProvider: result.session.model?.provider ?? null,
    modelId: result.session.model?.id ?? null,
    restoredTurnCount: checkpointTurns.length,
    restoredContextMessageCount,
  };
}

export {
  maximumCheckpointBytes,
  maximumCheckpointTextBytes,
  maximumCheckpointTurns,
};
