import assert from "node:assert/strict";
import {
  mkdir,
  mkdtemp,
  readFile,
  realpath,
  rm,
  writeFile,
} from "node:fs/promises";
import {
  join,
} from "node:path";
import {
  fileURLToPath,
} from "node:url";
import {
  createReadOnlyAgentSession,
} from "../src/read-only-session.mjs";
import {
  handleRequest,
} from "../src/protocol.mjs";

const hostRoot = fileURLToPath(new URL("..", import.meta.url));
const temporaryRoot = await mkdtemp(
  join(await realpath(hostRoot), ".jarvis-pi-edit-"),
);
const workspaceRoot = join(temporaryRoot, "workspace");
const workspaceFile = join(workspaceRoot, "notes.txt");
const invalidUtf8File = join(workspaceRoot, "invalid.txt");
const overlappingFile = join(workspaceRoot, "overlapping.txt");

let sessionHandle;
try {
  await mkdir(workspaceRoot);
  await writeFile(
    workspaceFile,
    "alpha\nowner-reviewed\nomega\n",
    "utf8",
  );
  sessionHandle = await createReadOnlyAgentSession(
    workspaceRoot,
  );
  assert.deepEqual(
    sessionHandle.activeTools,
    ["read", "grep", "find", "ls", "propose_edit"],
  );
  const proposeTool =
    sessionHandle.session.getToolDefinition("propose_edit");
  assert.ok(proposeTool);

  const firstResult = await proposeTool.execute(
    "proposal-tool-1",
    {
      path: "notes.txt",
      oldText: "owner-reviewed",
      newText: "owner-approved",
    },
    undefined,
    undefined,
    undefined,
  );
  const first =
    firstResult.details.workspaceEditProposal;
  assert.equal(first.schemaVersion, 1);
  assert.equal(first.relativePath, "notes.txt");
  assert.match(first.proposalId, /^workspace-edit-[0-9a-f]{32}$/u);
  assert.match(first.beforeSha256, /^[0-9a-f]{64}$/u);
  assert.equal(
    await readFile(workspaceFile, "utf8"),
    "alpha\nowner-reviewed\nomega\n",
  );

  const state = {
    sessionHandle,
    modelBrokerPipe: null,
    activeTurn: null,
  };
  const committed = await handleRequest(
    {
      type: "commit_workspace_edit",
      id: "commit-1",
      proposalId: first.proposalId,
      beforeSha256: first.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(committed.response.success, true);
  assert.equal(committed.response.data.status, "applied");
  assert.equal(
    committed.response.data.mutationPerformed,
    true,
  );
  assert.match(
    committed.response.data.afterSha256,
    /^[0-9a-f]{64}$/u,
  );
  assert.notEqual(
    committed.response.data.afterSha256,
    first.beforeSha256,
  );
  assert.equal(
    await readFile(workspaceFile, "utf8"),
    "alpha\nowner-approved\nomega\n",
  );

  const replay = await handleRequest(
    {
      type: "commit_workspace_edit",
      id: "commit-replay",
      proposalId: first.proposalId,
      beforeSha256: first.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(replay.response.success, false);
  assert.equal(
    replay.response.error.code,
    "workspace-edit-not-pending",
  );

  const driftResult = await proposeTool.execute(
    "proposal-tool-drift",
    {
      path: "notes.txt",
      oldText: "owner-approved",
      newText: "model-wanted",
    },
    undefined,
    undefined,
    undefined,
  );
  const drift =
    driftResult.details.workspaceEditProposal;
  await writeFile(
    workspaceFile,
    "alpha\nowner-updated-elsewhere\nomega\n",
    "utf8",
  );
  const drifted = await handleRequest(
    {
      type: "commit_workspace_edit",
      id: "commit-drifted",
      proposalId: drift.proposalId,
      beforeSha256: drift.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(drifted.response.success, false);
  assert.equal(
    drifted.response.error.code,
    "workspace-edit-drifted",
  );
  assert.equal(
    await readFile(workspaceFile, "utf8"),
    "alpha\nowner-updated-elsewhere\nomega\n",
  );
  const driftReplay = await handleRequest(
    {
      type: "commit_workspace_edit",
      id: "commit-drift-replay",
      proposalId: drift.proposalId,
      beforeSha256: drift.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(
    driftReplay.response.error.code,
    "workspace-edit-not-pending",
  );

  const rejectedResult = await proposeTool.execute(
    "proposal-tool-reject",
    {
      path: "notes.txt",
      oldText: "owner-updated-elsewhere",
      newText: "model-wanted",
    },
    undefined,
    undefined,
    undefined,
  );
  const rejectedProposal =
    rejectedResult.details.workspaceEditProposal;
  const wrongCapability = await handleRequest(
    {
      type: "discard_workspace_edit",
      id: "reject-wrong-hash",
      proposalId: rejectedProposal.proposalId,
      beforeSha256: "0".repeat(64),
    },
    {},
    {},
    state,
  );
  assert.equal(wrongCapability.response.success, false);
  assert.equal(
    wrongCapability.response.error.code,
    "workspace-edit-proposal-mismatch",
  );
  const rejected = await handleRequest(
    {
      type: "discard_workspace_edit",
      id: "reject-exact",
      proposalId: rejectedProposal.proposalId,
      beforeSha256: rejectedProposal.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(rejected.response.success, true);
  assert.equal(rejected.response.data.status, "rejected");
  assert.equal(
    rejected.response.data.mutationPerformed,
    false,
  );
  assert.equal(rejected.response.data.afterSha256, null);
  assert.equal(
    await readFile(workspaceFile, "utf8"),
    "alpha\nowner-updated-elsewhere\nomega\n",
  );

  await assert.rejects(
    proposeTool.execute(
      "proposal-tool-missing",
      {
        path: "missing.txt",
        oldText: "missing",
        newText: "replacement",
      },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code === "workspace-path-not-found",
  );
  await assert.rejects(
    proposeTool.execute(
      "proposal-tool-ambiguous",
      {
        path: "notes.txt",
        oldText: "a",
        newText: "A",
      },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code ===
      "workspace-edit-match-not-unique",
  );
  await writeFile(
    invalidUtf8File,
    Buffer.from([0xc3, 0x28]),
  );
  await assert.rejects(
    proposeTool.execute(
      "proposal-tool-invalid-utf8",
      {
        path: "invalid.txt",
        oldText: "(",
        newText: ")",
      },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code === "workspace-file-not-utf8",
  );
  await writeFile(overlappingFile, "aaa", "utf8");
  await assert.rejects(
    proposeTool.execute(
      "proposal-tool-overlapping",
      {
        path: "overlapping.txt",
        oldText: "aa",
        newText: "AA",
      },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code ===
      "workspace-edit-match-not-unique",
  );

  process.stdout.write(
    `${JSON.stringify({
      schemaVersion: 1,
      receiptType:
        "jarvisv2-pi-workspace-edit-approval-probe",
      result: "passed",
      activeTools: sessionHandle.activeTools,
      proposalToolMutates: false,
      existingTextFilesOnly: true,
      strictUtf8Required: true,
      overlappingMatchesRejected: true,
      exactBeforeSha256Bound: true,
      oneShotApproval: true,
      replayRejected: true,
      driftRejected: true,
      rejectMutates: false,
      shellMutationSupported: false,
      explorerMutationSupported: false,
      unattendedSelfIteration: false,
      liveExplorer: "not-run",
    }, null, 2)}\n`,
  );
} finally {
  sessionHandle?.workspaceEditProposalManager.clear();
  sessionHandle?.session.dispose();
  await rm(temporaryRoot, {
    recursive: true,
    force: true,
  });
}
