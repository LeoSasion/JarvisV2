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
const generatedRoot = join(workspaceRoot, "generated");
const createdFile = join(generatedRoot, "owner-note.txt");
const racedCreateFile = join(generatedRoot, "raced.txt");
const rejectedCreateFile = join(generatedRoot, "rejected.txt");
const vcsMetadataRoot = join(workspaceRoot, ".git");

let sessionHandle;
try {
  await mkdir(workspaceRoot);
  await mkdir(generatedRoot);
  await mkdir(vcsMetadataRoot);
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
    [
      "read",
      "grep",
      "find",
      "ls",
      "propose_edit",
      "propose_patch",
      "propose_create_file",
      "propose_change_set",
    ],
  );
  const proposeTool =
    sessionHandle.session.getToolDefinition("propose_edit");
  const patchTool =
    sessionHandle.session.getToolDefinition("propose_patch");
  const createTool =
    sessionHandle.session.getToolDefinition(
      "propose_create_file",
    );
  const changeSetTool =
    sessionHandle.session.getToolDefinition(
      "propose_change_set",
    );
  assert.ok(proposeTool);
  assert.ok(patchTool);
  assert.ok(createTool);
  assert.ok(changeSetTool);

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
  assert.equal(first.schemaVersion, 3);
  assert.equal(first.operation, "replace");
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
  assert.equal(committed.response.data.operation, "replace");
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

  const patchResult = await patchTool.execute(
    "proposal-tool-patch",
    {
      path: "notes.txt",
      replacements: [
        { oldText: "alpha", newText: "ALPHA" },
        { oldText: "omega", newText: "OMEGA" },
      ],
    },
    undefined,
    undefined,
    undefined,
  );
  const patchProposal =
    patchResult.details.workspaceEditProposal;
  assert.equal(patchProposal.schemaVersion, 3);
  assert.equal(patchProposal.operation, "patch");
  assert.equal(patchProposal.relativePath, "notes.txt");
  assert.equal(patchProposal.patchHunks.length, 2);
  assert.deepEqual(
    patchProposal.patchHunks.map(hunk => hunk.ordinal),
    [1, 2],
  );
  assert.equal(
    await readFile(workspaceFile, "utf8"),
    "alpha\nowner-updated-elsewhere\nomega\n",
  );
  const patched = await handleRequest(
    {
      type: "commit_workspace_edit",
      id: "commit-patch",
      proposalId: patchProposal.proposalId,
      beforeSha256: patchProposal.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(patched.response.success, true);
  assert.equal(patched.response.data.operation, "patch");
  assert.equal(patched.response.data.schemaVersion, 3);
  assert.equal(patched.response.data.mutationPerformed, true);
  assert.equal(
    await readFile(workspaceFile, "utf8"),
    "ALPHA\nowner-updated-elsewhere\nOMEGA\n",
  );

  const rejectedPatchResult = await patchTool.execute(
    "proposal-tool-patch-reject",
    {
      path: "notes.txt",
      replacements: [
        { oldText: "ALPHA", newText: "alpha-again" },
        { oldText: "OMEGA", newText: "omega-again" },
      ],
    },
    undefined,
    undefined,
    undefined,
  );
  const rejectedPatchProposal =
    rejectedPatchResult.details.workspaceEditProposal;
  const rejectedPatch = await handleRequest(
    {
      type: "discard_workspace_edit",
      id: "reject-patch",
      proposalId: rejectedPatchProposal.proposalId,
      beforeSha256: rejectedPatchProposal.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(rejectedPatch.response.success, true);
  assert.equal(rejectedPatch.response.data.operation, "patch");
  assert.equal(
    await readFile(workspaceFile, "utf8"),
    "ALPHA\nowner-updated-elsewhere\nOMEGA\n",
  );
  await assert.rejects(
    patchTool.execute(
      "proposal-tool-patch-control-character",
      {
        path: "notes.txt",
        replacements: [
          { oldText: "ALPHA", newText: "ALPHA\u0001" },
          { oldText: "OMEGA", newText: "omega-safe" },
        ],
      },
      undefined,
      undefined,
      undefined,
    ),
    error => error?.code === "invalid-workspace-patch",
  );
  assert.equal(
    await readFile(workspaceFile, "utf8"),
    "ALPHA\nowner-updated-elsewhere\nOMEGA\n",
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
    "ALPHA\nowner-updated-elsewhere\nOMEGA\n",
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
        oldText: "e",
        newText: "E",
      },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code ===
      "workspace-edit-match-not-unique",
  );

  const createResult = await createTool.execute(
    "proposal-tool-create",
    {
      path: "generated/owner-note.txt",
      content: "# Owner reviewed\n\nCreated once.\n",
    },
    undefined,
    undefined,
    undefined,
  );
  const createProposal =
    createResult.details.workspaceEditProposal;
  assert.equal(createProposal.schemaVersion, 3);
  assert.equal(createProposal.operation, "create");
  assert.equal(
    createProposal.relativePath,
    "generated/owner-note.txt",
  );
  assert.equal(createProposal.oldText, "");
  await assert.rejects(
    readFile(createdFile, "utf8"),
    (error) => error.code === "ENOENT",
  );
  const created = await handleRequest(
    {
      type: "commit_workspace_edit",
      id: "commit-create",
      proposalId: createProposal.proposalId,
      beforeSha256: createProposal.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(created.response.success, true);
  assert.equal(created.response.data.operation, "create");
  assert.equal(created.response.data.mutationPerformed, true);
  assert.equal(
    await readFile(createdFile, "utf8"),
    "# Owner reviewed\n\nCreated once.\n",
  );

  const racedResult = await createTool.execute(
    "proposal-tool-create-race",
    {
      path: "generated/raced.txt",
      content: "Pi proposal\n",
    },
    undefined,
    undefined,
    undefined,
  );
  const racedProposal =
    racedResult.details.workspaceEditProposal;
  await writeFile(racedCreateFile, "Owner file\n", "utf8");
  const raced = await handleRequest(
    {
      type: "commit_workspace_edit",
      id: "commit-create-race",
      proposalId: racedProposal.proposalId,
      beforeSha256: racedProposal.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(raced.response.success, false);
  assert.equal(
    raced.response.error.code,
    "workspace-edit-drifted",
  );
  assert.equal(
    await readFile(racedCreateFile, "utf8"),
    "Owner file\n",
  );

  const rejectCreateResult = await createTool.execute(
    "proposal-tool-create-reject",
    {
      path: "generated/rejected.txt",
      content: "Never written\n",
    },
    undefined,
    undefined,
    undefined,
  );
  const rejectCreateProposal =
    rejectCreateResult.details.workspaceEditProposal;
  const rejectedCreate = await handleRequest(
    {
      type: "discard_workspace_edit",
      id: "reject-create",
      proposalId: rejectCreateProposal.proposalId,
      beforeSha256: rejectCreateProposal.beforeSha256,
    },
    {},
    {},
    state,
  );
  assert.equal(rejectedCreate.response.success, true);
  assert.equal(rejectedCreate.response.data.operation, "create");
  await assert.rejects(
    readFile(rejectedCreateFile, "utf8"),
    (error) => error.code === "ENOENT",
  );

  await assert.rejects(
    createTool.execute(
      "proposal-tool-create-existing",
      { path: "notes.txt", content: "No overwrite\n" },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code === "workspace-file-already-exists",
  );
  await assert.rejects(
    createTool.execute(
      "proposal-tool-create-missing-parent",
      { path: "missing/new.txt", content: "No directories\n" },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code === "workspace-path-not-found",
  );
  await assert.rejects(
    createTool.execute(
      "proposal-tool-create-vcs-metadata",
      { path: ".git/config", content: "forbidden\n" },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code ===
      "workspace-vcs-metadata-forbidden",
  );
  if (process.platform === "win32") {
    await assert.rejects(
      createTool.execute(
        "proposal-tool-create-device-alias",
        { path: "generated/NUL.txt", content: "forbidden\n" },
        undefined,
        undefined,
        undefined,
      ),
      (error) => error.code === "invalid-workspace-path",
    );
    await assert.rejects(
      createTool.execute(
        "proposal-tool-create-trailing-dot-alias",
        { path: "generated/alias.", content: "forbidden\n" },
        undefined,
        undefined,
        undefined,
      ),
      (error) => error.code === "invalid-workspace-path",
    );
  }
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
  await writeFile(overlappingFile, "abcdef", "utf8");
  await assert.rejects(
    patchTool.execute(
      "proposal-tool-patch-overlapping",
      {
        path: "overlapping.txt",
        replacements: [
          { oldText: "abc", newText: "ABC" },
          { oldText: "bc", newText: "BC" },
        ],
      },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code === "workspace-patch-overlap",
  );
  await assert.rejects(
    patchTool.execute(
      "proposal-tool-patch-duplicate",
      {
        path: "overlapping.txt",
        replacements: [
          { oldText: "abc", newText: "ABC" },
          { oldText: "abc", newText: "ABCD" },
        ],
      },
      undefined,
      undefined,
      undefined,
    ),
    (error) => error.code === "invalid-workspace-patch",
  );

  process.stdout.write(
    `${JSON.stringify({
      schemaVersion: 1,
      receiptType:
        "jarvisv2-pi-workspace-edit-approval-probe",
      result: "passed",
      activeTools: sessionHandle.activeTools,
      proposalToolMutates: false,
      existingTextFilesOnly: false,
      newUtf8FileSupported: true,
      multiHunkPatchSupported: true,
      patchMinimumHunks: 2,
      patchMaximumHunks: 8,
      patchMaximumPreviewBytes: 16_384,
      patchSingleFileOnly: true,
      patchAtomicReplace: true,
      patchOverlapRejected: true,
      patchBinaryControlsRejected: true,
      newFileMaxBytes: 16_384,
      existingParentRequired: true,
      exclusiveCreate: true,
      overwriteRejected: true,
      versionControlMetadataRejected: true,
      windowsDeviceAliasesRejected: process.platform === "win32",
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
