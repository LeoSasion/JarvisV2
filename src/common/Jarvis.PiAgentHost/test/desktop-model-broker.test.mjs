import assert from "node:assert/strict";
import { randomBytes } from "node:crypto";
import { once } from "node:events";
import {
  mkdtemp,
  realpath,
  rm,
  writeFile,
} from "node:fs/promises";
import { createServer } from "node:net";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  brokerModelId,
  brokerProtocol,
  brokerProviderId,
  validateDesktopBrokerPipe,
} from "../src/desktop-model-broker.mjs";
import {
  createReadOnlyAgentSession,
} from "../src/read-only-session.mjs";
import {
  handleRequest,
} from "../src/protocol.mjs";

if (process.platform !== "win32") {
  throw new Error(
    "The desktop model broker probe requires Windows named pipes.",
  );
}

const hostRoot = fileURLToPath(new URL("..", import.meta.url));
const temporaryRoot = await mkdtemp(
  join(await realpath(hostRoot), ".jarvis-pi-broker-"),
);
const workspaceFile = join(temporaryRoot, "context.txt");
const createPipePath = () => [
  "\\\\.\\pipe\\jarvis2-pi-model-",
  randomBytes(16).toString("hex"),
].join("");
const pipePath = createPipePath();
const requests = [];

function writeRecord(socket, record) {
  socket.write(`${JSON.stringify(record)}\n`, "utf8");
}

async function withinTimeout(promise, label) {
  let timer;
  try {
    return await Promise.race([
      promise,
      new Promise((_, reject) => {
        timer = setTimeout(
          () => reject(new Error(`${label} timed out.`)),
          5_000,
        );
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}

async function expectBrokerFailure(workspaceRoot, mode) {
  const failurePipe = createPipePath();
  const failureServer = createServer((socket) => {
    socket.setEncoding("utf8");
    socket.on("error", () => {});
    let buffer = "";
    socket.on("data", (chunk) => {
      buffer += chunk;
      let newlineIndex = buffer.indexOf("\n");
      while (newlineIndex >= 0) {
        const line = buffer.slice(0, newlineIndex);
        buffer = buffer.slice(newlineIndex + 1);
        newlineIndex = buffer.indexOf("\n");
        if (line.length === 0) {
          continue;
        }
        const record = JSON.parse(line);
        if (record.type === "broker_hello") {
          writeRecord(socket, {
            type: "broker_ready",
            protocol: mode === "wrong-protocol"
              ? "wrong-protocol"
              : brokerProtocol,
          });
        } else if (record.type === "model_request") {
          if (mode === "disconnect") {
            socket.end();
          } else if (mode === "oversized-frame") {
            writeRecord(socket, {
              type: "model_delta",
              id: record.id,
              delta: "x".repeat(1_048_577),
            });
          }
        }
      }
    });
  });
  let failureSession;
  try {
    failureServer.listen(failurePipe);
    await once(failureServer, "listening");
    failureSession = await createReadOnlyAgentSession(
      workspaceRoot,
      { modelBrokerPipe: failurePipe },
    );
    let resolveTerminal;
    const terminalPromise = new Promise((resolve) => {
      resolveTerminal = resolve;
    });
    const result = await handleRequest(
      {
        type: "start_turn",
        id: `fault-${mode}`,
        text: `Exercise the ${mode} broker fault.`,
      },
      {},
      {},
      {
        sessionHandle: failureSession,
        modelBrokerPipe: failurePipe,
        activeTurn: null,
      },
      (event) => {
        if (event.event === "turn_completed") {
          resolveTerminal(event);
        }
      },
    );
    assert.equal(result.response.success, true);
    assert.equal(result.response.data.status, "started");
    const terminal = await terminalPromise;
    assert.equal(terminal.success, false);
    assert.equal(terminal.error.code, "prompt-failed");
  } finally {
    failureSession?.session.dispose();
    failureServer.close();
    if (failureServer.listening) {
      await once(failureServer, "close");
    }
  }
}

async function expectTurnAbort(workspaceRoot) {
  const abortPipe = createPipePath();
  let resolveRequestObserved;
  const requestObserved = new Promise((resolve) => {
    resolveRequestObserved = resolve;
  });
  const abortServer = createServer((socket) => {
    socket.setEncoding("utf8");
    socket.on("error", () => {});
    let buffer = "";
    socket.on("data", (chunk) => {
      buffer += chunk;
      let newlineIndex = buffer.indexOf("\n");
      while (newlineIndex >= 0) {
        const line = buffer.slice(0, newlineIndex);
        buffer = buffer.slice(newlineIndex + 1);
        newlineIndex = buffer.indexOf("\n");
        if (line.length === 0) {
          continue;
        }
        const record = JSON.parse(line);
        if (record.type === "broker_hello") {
          writeRecord(socket, {
            type: "broker_ready",
            protocol: brokerProtocol,
          });
        } else if (record.type === "model_request") {
          resolveRequestObserved();
        }
      }
    });
  });
  let abortSession;
  try {
    abortServer.listen(abortPipe);
    await once(abortServer, "listening");
    abortSession = await createReadOnlyAgentSession(
      workspaceRoot,
      { modelBrokerPipe: abortPipe },
    );
    const state = {
      sessionHandle: abortSession,
      modelBrokerPipe: abortPipe,
      activeTurn: null,
    };
    let resolveTerminal;
    const terminalPromise = new Promise((resolve) => {
      resolveTerminal = resolve;
    });
    const emitEvent = (event) => {
      if (event.event === "turn_completed") {
        resolveTerminal(event);
      }
    };
    const started = await handleRequest(
      {
        type: "start_turn",
        id: "abort-target",
        text: "Wait until this turn is cancelled.",
      },
      {},
      {},
      state,
      emitEvent,
    );
    assert.equal(started.response.success, true);
    await withinTimeout(requestObserved, "broker request");
    const aborted = await handleRequest(
      {
        type: "abort_turn",
        id: "abort-command",
        turnId: "abort-target",
      },
      {},
      {},
      state,
      emitEvent,
    );
    assert.equal(aborted.response.success, true);
    assert.equal(aborted.response.data.status, "abort-requested");
    const terminal = await withinTimeout(
      terminalPromise,
      "turn terminal event",
    );
    assert.equal(terminal.success, false);
    assert.equal(terminal.status, "aborted");
    assert.equal(terminal.error.code, "turn-aborted");
    assert.equal(state.activeTurn, null);
  } finally {
    abortSession?.session.dispose();
    abortServer.close();
    if (abortServer.listening) {
      await once(abortServer, "close");
    }
  }
}

const server = createServer((socket) => {
  socket.setEncoding("utf8");
  let buffer = "";
  socket.on("data", (chunk) => {
    buffer += chunk;
    let newlineIndex = buffer.indexOf("\n");
    while (newlineIndex >= 0) {
      const line = buffer.slice(0, newlineIndex);
      buffer = buffer.slice(newlineIndex + 1);
      newlineIndex = buffer.indexOf("\n");
      if (line.length === 0) {
        continue;
      }
      const record = JSON.parse(line);
      requests.push(record);
      if (record.type === "broker_hello") {
        writeRecord(socket, {
          type: "broker_ready",
          protocol: brokerProtocol,
        });
      } else if (record.type === "model_request") {
        writeRecord(socket, {
          type: "model_delta",
          id: record.id,
          delta: "JARVIS ",
        });
        writeRecord(socket, {
          type: "model_delta",
          id: record.id,
          delta: "broker online.",
        });
        writeRecord(socket, {
          type: "model_done",
          id: record.id,
          reason: "stop",
          usage: {
            input: 7,
            output: 3,
            cacheRead: 0,
            cacheWrite: 0,
          },
        });
      }
    }
  });
});

let sessionHandle;
try {
  await writeFile(
    workspaceFile,
    "Desktop broker context boundary.\n",
    "utf8",
  );
  server.listen(pipePath);
  await once(server, "listening");

  sessionHandle = await createReadOnlyAgentSession(
    temporaryRoot,
    { modelBrokerPipe: pipePath },
  );
  assert.equal(sessionHandle.promptingEnabled, true);
  assert.equal(sessionHandle.modelSelected, true);
  assert.equal(sessionHandle.modelProvider, brokerProviderId);
  assert.equal(sessionHandle.modelId, brokerModelId);

  const deltas = [];
  const unsubscribe = sessionHandle.session.subscribe((event) => {
    if (
      event.type === "message_update" &&
      event.assistantMessageEvent?.type === "text_delta"
    ) {
      deltas.push(event.assistantMessageEvent.delta);
    }
  });
  try {
    await sessionHandle.session.prompt(
      "Confirm the local desktop broker is online.",
    );
    await sessionHandle.session.waitForIdle();
  } finally {
    unsubscribe();
  }

  assert.equal(deltas.join(""), "JARVIS broker online.");
  assert.equal(requests[0].type, "broker_hello");
  assert.equal(requests[0].protocol, brokerProtocol);
  assert.equal(requests[1].type, "model_request");
  assert.equal(requests[1].protocol, brokerProtocol);
  assert.equal(
    requests[1].model.provider,
    brokerProviderId,
  );
  assert.equal(requests[1].model.id, brokerModelId);
  assert.ok(Array.isArray(requests[1].context.messages));
  assert.equal(
    JSON.stringify(requests).toLowerCase().includes("apikey"),
    false,
  );
  assert.equal(
    validateDesktopBrokerPipe(
      "\\\\.\\pipe\\unreviewed-model-broker",
    ),
    false,
  );
  await expectBrokerFailure(temporaryRoot, "wrong-protocol");
  await expectBrokerFailure(temporaryRoot, "disconnect");
  await expectBrokerFailure(temporaryRoot, "oversized-frame");
  await expectTurnAbort(temporaryRoot);

  process.stdout.write(
    `${JSON.stringify({
      schemaVersion: 1,
      receiptType: "jarvisv2-pi-desktop-model-broker-probe",
      result: "passed",
      protocol: brokerProtocol,
      provider: brokerProviderId,
      model: brokerModelId,
      namedPipeOnly: true,
      credentialTransportAllowed: false,
      promptingEnabled: true,
      deltaCount: deltas.length,
      response: deltas.join(""),
      faultScenarioCount: 5,
      invalidPipeRejected: true,
      wrongProtocolRejected: true,
      disconnectRejected: true,
      oversizedFrameRejected: true,
      activeTurnAbortPassed: true,
      liveModelNetwork: "not-run",
      liveExplorer: "not-run",
      mutationPerformed: false,
    }, null, 2)}\n`,
  );
} finally {
  sessionHandle?.session.dispose();
  server.close();
  if (server.listening) {
    await once(server, "close");
  }
  await rm(temporaryRoot, {
    recursive: true,
    force: true,
  });
}
