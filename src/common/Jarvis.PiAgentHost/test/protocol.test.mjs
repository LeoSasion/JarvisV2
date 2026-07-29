import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";

const hostUrl = new URL("../src/host.mjs", import.meta.url);
const hostPath = fileURLToPath(hostUrl);
const hostRoot = fileURLToPath(new URL("..", import.meta.url));

async function runHost(lines) {
  const child = spawn(process.execPath, [hostPath, "serve"], {
    cwd: hostRoot,
    env: {
      ...process.env,
      PI_OFFLINE: "1",
    },
    shell: false,
    stdio: ["pipe", "pipe", "pipe"],
  });
  let stdout = "";
  let stderr = "";
  child.stdout.setEncoding("utf8");
  child.stderr.setEncoding("utf8");
  child.stdout.on("data", (chunk) => {
    stdout += chunk;
  });
  child.stderr.on("data", (chunk) => {
    stderr += chunk;
  });
  child.stdin.end(`${lines.join("\n")}\n`);
  const exitCode = await new Promise((resolve, reject) => {
    child.once("error", reject);
    child.once("close", resolve);
  });
  return {
    exitCode,
    stderr,
    records: stdout
      .split("\n")
      .filter((line) => line.length !== 0)
      .map((line) => JSON.parse(line)),
  };
}

const requests = [
  { type: "hello", id: "hello-1" },
  { type: "capabilities", id: "capabilities-1" },
  { type: "hello", id: "credential-1", apiKey: "do-not-send" },
  { type: "start_session", id: "session-1" },
  { type: "shutdown", id: "shutdown-1" },
];
const primary = await runHost(
  requests.map((request) => JSON.stringify(request)),
);
assert.equal(primary.exitCode, 0, primary.stderr);
const records = primary.records;
assert.equal(records.length, 6);
assert.equal(records[0].type, "ready");
assert.equal(records[0].sessionCreationEnabled, false);
assert.equal(records[1].command, "hello");
assert.equal(records[1].success, true);
assert.deepEqual(
  records[2].data.initialTools,
  ["read", "grep", "find", "ls"],
);
assert.equal(records[2].data.credentialTransportAllowed, false);
assert.equal(records[3].command, "hello");
assert.equal(records[3].success, false);
assert.equal(
  records[3].error.code,
  "credential-field-forbidden",
);
assert.equal(records[4].command, "start_session");
assert.equal(records[4].success, false);
assert.equal(records[4].error.code, "policy-disabled");
assert.equal(records[5].command, "shutdown");
assert.equal(records[5].success, true);

const batchLines = Array.from({ length: 80 }, (_, index) =>
  JSON.stringify({
    type: "hello",
    id: `batch-${index}`,
    padding: "x".repeat(900),
  }),
);
batchLines.push(JSON.stringify({ type: "shutdown", id: "batch-end" }));
const batch = await runHost(batchLines);
assert.equal(batch.exitCode, 0, batch.stderr);
assert.equal(batch.records.length, 82);
assert.equal(
  batch.records.some((record) => record.type === "fatal"),
  false,
);

const oversized = await runHost([
  JSON.stringify({
    type: "hello",
    id: "oversized",
    padding: "x".repeat(70_000),
  }),
]);
assert.equal(oversized.exitCode, 13, oversized.stderr);
assert.equal(oversized.records.length, 2);
assert.equal(oversized.records[1].type, "fatal");
assert.equal(
  oversized.records[1].error.code,
  "frame-too-large",
);

process.stdout.write(
  `${JSON.stringify({
    schemaVersion: 1,
    receiptType: "jarvisv2-pi-agent-host-protocol-test",
    result: "passed",
    recordCount: records.length,
    framing: "lf-delimited-jsonl",
    credentialFieldsRejected: true,
    batchedFramesAccepted: batch.records.length - 1,
    oversizedFrameRejected: true,
    sessionCreationEnabled: false,
    credentialTransportAllowed: false,
    initialTools: records[2].data.initialTools,
    shellMutationSupported: false,
    explorerMutationSupported: false,
    activationPermitted: false,
    liveExplorer: "not-run",
  }, null, 2)}\n`,
);
