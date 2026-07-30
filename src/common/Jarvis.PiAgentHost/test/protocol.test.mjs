import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import {
  mkdir,
  mkdtemp,
  realpath,
  rm,
  symlink,
  writeFile,
} from "node:fs/promises";
import {
  join,
  parse,
} from "node:path";
import { fileURLToPath } from "node:url";
import {
  createReadOnlyAgentSession,
} from "../src/read-only-session.mjs";
import {
  admitWorkspaceRoot,
  assertWorkspacePath,
} from "../src/workspace-policy.mjs";

const hostUrl = new URL("../src/host.mjs", import.meta.url);
const hostPath = fileURLToPath(hostUrl);
const hostRoot = fileURLToPath(new URL("..", import.meta.url));

async function runHost(lines) {
  const childEnvironment = {
    ...process.env,
    PI_OFFLINE: "1",
  };
  for (const key of Object.keys(childEnvironment)) {
    const normalized = key.replaceAll(/[-_]/g, "").toLowerCase();
    if (
      [
        "accesskey",
        "apikey",
        "credential",
        "password",
        "secret",
        "token",
      ].some((shape) => normalized.includes(shape))
    ) {
      delete childEnvironment[key];
    }
  }
  const child = spawn(process.execPath, [hostPath, "serve"], {
    cwd: hostRoot,
    env: childEnvironment,
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

const temporaryRoot = await mkdtemp(
  join(await realpath(hostRoot), ".jarvis-pi-host-"),
);
const workspaceRoot = join(temporaryRoot, "workspace");
const outsideRoot = join(temporaryRoot, "outside");
const outsideFile = join(outsideRoot, "outside.txt");
const workspaceFile = join(workspaceRoot, "inside.txt");

try {
  await mkdir(workspaceRoot);
  await mkdir(outsideRoot);
  await writeFile(workspaceFile, "JARVIS workspace\n", "utf8");
  await writeFile(outsideFile, "outside boundary\n", "utf8");

  const requests = [
    { type: "hello", id: "hello-1" },
    { type: "capabilities", id: "capabilities-1" },
    {
      type: "hello",
      id: "credential-1",
      apiKey: "do-not-send",
    },
    {
      type: "start_session",
      id: "session-1",
      workspaceRoot,
    },
    {
      type: "start_session",
      id: "session-repeat",
      workspaceRoot,
    },
    { type: "shutdown", id: "shutdown-1" },
  ];
  const primary = await runHost(
    requests.map((request) => JSON.stringify(request)),
  );
  assert.equal(primary.exitCode, 0, primary.stderr);
  const records = primary.records;
  assert.equal(records.length, 7);
  assert.equal(records[0].type, "ready");
  assert.equal(records[0].credentialEnvironmentClean, true);
  assert.equal(records[0].sessionCreationEnabled, true);
  assert.equal(records[0].promptingEnabled, false);
  assert.equal(records[1].command, "hello");
  assert.equal(records[1].success, true);
  assert.deepEqual(
    records[2].data.initialTools,
    ["read", "grep", "find", "ls"],
  );
  assert.equal(records[2].data.sessionCreationEnabled, true);
  assert.equal(records[2].data.promptingEnabled, false);
  assert.equal(records[2].data.credentialTransportAllowed, false);
  assert.equal(records[2].data.sessionPersistence, "in-memory");
  assert.equal(records[2].data.resourceDiscoveryEnabled, false);
  assert.equal(records[2].data.modelNetworkAllowed, false);
  assert.equal(records[3].command, "hello");
  assert.equal(records[3].success, false);
  assert.equal(
    records[3].error.code,
    "credential-field-forbidden",
  );
  assert.equal(records[4].command, "start_session");
  assert.equal(records[4].success, true);
  assert.deepEqual(
    records[4].data.activeTools,
    ["read", "grep", "find", "ls"],
  );
  assert.equal(records[4].data.sessionPersisted, false);
  assert.equal(records[4].data.promptingEnabled, false);
  assert.equal(records[4].data.resourceDiscoveryEnabled, false);
  assert.equal(records[4].data.modelNetworkAllowed, false);
  assert.equal(records[5].success, false);
  assert.equal(
    records[5].error.code,
    "session-already-bound",
  );
  assert.equal(records[6].command, "shutdown");
  assert.equal(records[6].success, true);

  const invalid = await runHost([
    JSON.stringify({
      type: "start_session",
      id: "relative-root",
      workspaceRoot: "relative-workspace",
    }),
    JSON.stringify({
      type: "start_session",
      id: "drive-root",
      workspaceRoot: parse(workspaceRoot).root,
    }),
    JSON.stringify({
      type: "start_session",
      id: "missing-root",
      workspaceRoot: join(temporaryRoot, "missing"),
    }),
    JSON.stringify({ type: "shutdown", id: "invalid-end" }),
  ]);
  assert.equal(invalid.exitCode, 0, invalid.stderr);
  assert.equal(
    invalid.records[1].error.code,
    "invalid-workspace-root",
  );
  assert.equal(
    invalid.records[2].error.code,
    "protected-workspace-root",
  );
  assert.equal(
    invalid.records[3].error.code,
    "workspace-root-not-found",
  );

  if (process.platform === "win32") {
    const protectedPath = await realpath(
      join(process.env.LOCALAPPDATA, "Temp"),
    );
    await assert.rejects(
      admitWorkspaceRoot(protectedPath),
      (error) => error.code === "protected-workspace-root",
    );
  }

  const admission = await admitWorkspaceRoot(workspaceRoot);
  await assert.rejects(
    assertWorkspacePath(admission, outsideFile),
    (error) => error.code === "path-outside-workspace",
  );

  const sessionHandle = await createReadOnlyAgentSession(
    workspaceRoot,
  );
  try {
    const readTool =
      sessionHandle.session.getToolDefinition("read");
    assert.ok(readTool);
    const insideRead = await readTool.execute(
      "inside-read",
      { path: workspaceFile },
      undefined,
      undefined,
      undefined,
    );
    assert.match(
      insideRead.content[0].text,
      /JARVIS workspace/u,
    );
    await assert.rejects(
      readTool.execute(
        "outside-read",
        { path: outsideFile },
        undefined,
        undefined,
        undefined,
      ),
      (error) => error.code === "path-outside-workspace",
    );
  } finally {
    sessionHandle.session.dispose();
  }

  const linkPath = join(workspaceRoot, "outside-link");
  await symlink(
    outsideRoot,
    linkPath,
    process.platform === "win32" ? "junction" : "dir",
  );
  await assert.rejects(
    assertWorkspacePath(admission, linkPath),
    (error) => error.code === "reparse-point-forbidden",
  );

  const aliasRoot = join(temporaryRoot, "workspace-alias");
  await symlink(
    workspaceRoot,
    aliasRoot,
    process.platform === "win32" ? "junction" : "dir",
  );
  const alias = await runHost([
    JSON.stringify({
      type: "start_session",
      id: "alias-root",
      workspaceRoot: aliasRoot,
    }),
    JSON.stringify({ type: "shutdown", id: "alias-end" }),
  ]);
  assert.equal(alias.exitCode, 0, alias.stderr);
  assert.equal(alias.records[1].success, false);
  assert.equal(
    alias.records[1].error.code,
    "workspace-root-alias-forbidden",
  );

  const batchLines = Array.from({ length: 80 }, (_, index) =>
    JSON.stringify({
      type: "hello",
      id: `batch-${index}`,
      padding: "x".repeat(900),
    }),
  );
  batchLines.push(JSON.stringify({
    type: "shutdown",
    id: "batch-end",
  }));
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
      credentialEnvironmentClean: true,
      batchedFramesAccepted: batch.records.length - 1,
      oversizedFrameRejected: true,
      sessionCreationEnabled: true,
      promptingEnabled: false,
      sessionPersistence: "in-memory",
      workspaceBinding: "single-explicit-root",
      protectedRootRejected: true,
      workspaceEscapeRejected: true,
      reparsePointRejected: true,
      repeatedBindingRejected: true,
      resourceDiscoveryEnabled: false,
      modelNetworkAllowed: false,
      credentialTransportAllowed: false,
      initialTools: records[2].data.initialTools,
      shellMutationSupported: false,
      explorerMutationSupported: false,
      activationPermitted: false,
      liveExplorer: "not-run",
    }, null, 2)}\n`,
  );
} finally {
  await rm(temporaryRoot, {
    recursive: true,
    force: true,
  });
}
