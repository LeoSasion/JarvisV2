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
  WorkspaceTransactionCrashForTest,
  workspaceTransactionJournalName,
} from "../src/workspace-edit-proposal.mjs";

const hostRoot = fileURLToPath(new URL("..", import.meta.url));
const temporaryRoot = await mkdtemp(
  join(await realpath(hostRoot), ".jarvis-pi-change-set-"),
);

async function createFixture(name) {
  const root = join(temporaryRoot, name);
  await mkdir(root);
  await mkdir(join(root, "docs"));
  await writeFile(
    join(root, "source.txt"),
    "const value = 1;\n",
    "utf8",
  );
  await writeFile(
    join(root, "test.txt"),
    "alpha\nbeta\ngamma\n",
    "utf8",
  );
  return root;
}

function twoFileChanges() {
  return [
    {
      operation: "replace",
      path: "source.txt",
      oldText: "value = 1",
      newText: "value = 2",
    },
    {
      operation: "replace",
      path: "test.txt",
      oldText: "beta",
      newText: "BETA",
    },
  ];
}

async function stageChangeSet(sessionHandle, changes) {
  const tool = sessionHandle.session.getToolDefinition(
    "propose_change_set",
  );
  assert.ok(tool);
  const result = await tool.execute(
    "change-set-tool",
    { changes },
    undefined,
    undefined,
    undefined,
  );
  return result.details.workspaceEditProposal;
}

let applySession;
let rejectionSession;
let caseAliasSession;
let driftSession;
let rollbackSession;
let crashSession;
let recoveredSession;
let committedCrashSession;
let completedRecoverySession;
let tamperSession;
try {
  const applyRoot = await createFixture("apply");
  applySession = await createReadOnlyAgentSession(applyRoot);
  assert.equal(
    applySession.workspaceTransactionRecovery.result,
    "none",
  );
  const proposal = await stageChangeSet(
    applySession,
    [
      {
        operation: "replace",
        path: "source.txt",
        oldText: "value = 1",
        newText: "value = 2",
      },
      {
        operation: "patch",
        path: "test.txt",
        replacements: [
          { oldText: "alpha", newText: "ALPHA" },
          { oldText: "gamma", newText: "GAMMA" },
        ],
      },
      {
        operation: "create",
        path: "docs/change.md",
        content: "# Owner-reviewed change\n",
      },
    ],
  );
  assert.equal(proposal.schemaVersion, 4);
  assert.equal(proposal.operation, "change-set");
  assert.equal(proposal.changes.length, 3);
  assert.match(proposal.beforeSha256, /^[0-9a-f]{64}$/u);
  assert.equal(
    await readFile(join(applyRoot, "source.txt"), "utf8"),
    "const value = 1;\n",
  );
  await assert.rejects(
    readFile(join(applyRoot, "docs/change.md"), "utf8"),
    error => error?.code === "ENOENT",
  );
  const applied =
    await applySession.workspaceEditProposalManager.commit(
      proposal.proposalId,
      proposal.beforeSha256,
    );
  assert.equal(applied.schemaVersion, 4);
  assert.equal(applied.status, "applied");
  assert.equal(applied.files.length, 3);
  assert.equal(
    applied.transactionModel,
    "durable-before-or-after-convergence-no-simultaneous-visibility-claim",
  );
  assert.equal(
    await readFile(join(applyRoot, "source.txt"), "utf8"),
    "const value = 2;\n",
  );
  assert.equal(
    await readFile(join(applyRoot, "test.txt"), "utf8"),
    "ALPHA\nbeta\nGAMMA\n",
  );
  assert.equal(
    await readFile(join(applyRoot, "docs/change.md"), "utf8"),
    "# Owner-reviewed change\n",
  );
  await assert.rejects(
    applySession.workspaceEditProposalManager.commit(
      proposal.proposalId,
      proposal.beforeSha256,
    ),
    error => error?.code === "workspace-edit-not-pending",
  );

  const rejectionRoot = await createFixture("reject");
  rejectionSession = await createReadOnlyAgentSession(
    rejectionRoot,
  );
  const rejectedProposal = await stageChangeSet(
    rejectionSession,
    twoFileChanges(),
  );
  const rejected =
    rejectionSession.workspaceEditProposalManager.discard(
      rejectedProposal.proposalId,
      rejectedProposal.beforeSha256,
    );
  assert.equal(rejected.schemaVersion, 4);
  assert.equal(rejected.status, "rejected");
  assert.equal(rejected.files.length, 2);
  assert.equal(rejected.mutationPerformed, false);
  assert.equal(
    await readFile(join(rejectionRoot, "source.txt"), "utf8"),
    "const value = 1;\n",
  );

  const caseAliasRoot = await createFixture("case-alias");
  caseAliasSession = await createReadOnlyAgentSession(
    caseAliasRoot,
  );
  await assert.rejects(
    stageChangeSet(
      caseAliasSession,
      [
        {
          operation: "create",
          path: "docs/repeated.txt",
          content: "first\n",
        },
        {
          operation: "create",
          path: "docs/repeated.txt",
          content: "second\n",
        },
      ],
    ),
    error =>
      error?.code === "workspace-change-set-path-repeated",
  );
  if (process.platform === "win32") {
    await assert.rejects(
      stageChangeSet(
        caseAliasSession,
        [
          {
            operation: "create",
            path: "docs/CaseAlias.txt",
            content: "first\n",
          },
          {
            operation: "create",
            path: "docs/casealias.txt",
            content: "second\n",
          },
        ],
      ),
      error =>
        error?.code === "workspace-change-set-path-repeated",
    );
  }

  const driftRoot = await createFixture("drift");
  driftSession = await createReadOnlyAgentSession(driftRoot);
  const driftProposal = await stageChangeSet(
    driftSession,
    twoFileChanges(),
  );
  await writeFile(
    join(driftRoot, "test.txt"),
    "external drift\n",
    "utf8",
  );
  await assert.rejects(
    driftSession.workspaceEditProposalManager.commit(
      driftProposal.proposalId,
      driftProposal.beforeSha256,
    ),
    error => error?.code === "workspace-change-set-drifted",
  );
  assert.equal(
    await readFile(join(driftRoot, "source.txt"), "utf8"),
    "const value = 1;\n",
  );
  assert.equal(
    await readFile(join(driftRoot, "test.txt"), "utf8"),
    "external drift\n",
  );

  const rollbackRoot = await createFixture("rollback");
  rollbackSession = await createReadOnlyAgentSession(
    rollbackRoot,
    {
      workspaceTransactionHooks: {
        afterFileApplied(ordinal) {
          if (ordinal === 1) {
            throw new Error("injected mid-commit failure");
          }
        },
      },
    },
  );
  const rollbackProposal = await stageChangeSet(
    rollbackSession,
    twoFileChanges(),
  );
  await assert.rejects(
    rollbackSession.workspaceEditProposalManager.commit(
      rollbackProposal.proposalId,
      rollbackProposal.beforeSha256,
    ),
    error => error?.code === "workspace-change-set-rolled-back",
  );
  assert.equal(
    await readFile(join(rollbackRoot, "source.txt"), "utf8"),
    "const value = 1;\n",
  );
  assert.equal(
    await readFile(join(rollbackRoot, "test.txt"), "utf8"),
    "alpha\nbeta\ngamma\n",
  );
  await assert.rejects(
    readFile(
      join(rollbackRoot, workspaceTransactionJournalName),
      "utf8",
    ),
    error => error?.code === "ENOENT",
  );

  const crashRoot = await createFixture("crash-rollback");
  crashSession = await createReadOnlyAgentSession(
    crashRoot,
    {
      workspaceTransactionHooks: {
        afterFileApplied(ordinal) {
          if (ordinal === 1) {
            throw new WorkspaceTransactionCrashForTest();
          }
        },
      },
    },
  );
  const crashProposal = await stageChangeSet(
    crashSession,
    twoFileChanges(),
  );
  await assert.rejects(
    crashSession.workspaceEditProposalManager.commit(
      crashProposal.proposalId,
      crashProposal.beforeSha256,
    ),
    WorkspaceTransactionCrashForTest,
  );
  crashSession.session.dispose();
  crashSession = undefined;
  recoveredSession = await createReadOnlyAgentSession(crashRoot);
  assert.equal(
    recoveredSession.workspaceTransactionRecovery.result,
    "rolled-back",
  );
  assert.equal(
    recoveredSession.workspaceTransactionRecovery.fileCount,
    2,
  );
  assert.equal(
    await readFile(join(crashRoot, "source.txt"), "utf8"),
    "const value = 1;\n",
  );
  assert.equal(
    await readFile(join(crashRoot, "test.txt"), "utf8"),
    "alpha\nbeta\ngamma\n",
  );

  const committedRoot = await createFixture("crash-committed");
  committedCrashSession = await createReadOnlyAgentSession(
    committedRoot,
    {
      workspaceTransactionHooks: {
        afterCommitted() {
          throw new WorkspaceTransactionCrashForTest();
        },
      },
    },
  );
  const committedProposal = await stageChangeSet(
    committedCrashSession,
    twoFileChanges(),
  );
  await assert.rejects(
    committedCrashSession.workspaceEditProposalManager.commit(
      committedProposal.proposalId,
      committedProposal.beforeSha256,
    ),
    WorkspaceTransactionCrashForTest,
  );
  committedCrashSession.session.dispose();
  committedCrashSession = undefined;
  completedRecoverySession = await createReadOnlyAgentSession(
    committedRoot,
  );
  assert.equal(
    completedRecoverySession.workspaceTransactionRecovery.result,
    "completed",
  );
  assert.equal(
    await readFile(join(committedRoot, "source.txt"), "utf8"),
    "const value = 2;\n",
  );
  assert.equal(
    await readFile(join(committedRoot, "test.txt"), "utf8"),
    "alpha\nBETA\ngamma\n",
  );

  const tamperRoot = await createFixture("tamper");
  tamperSession = await createReadOnlyAgentSession(
    tamperRoot,
    {
      workspaceTransactionHooks: {
        afterFileApplied(ordinal) {
          if (ordinal === 1) {
            throw new WorkspaceTransactionCrashForTest();
          }
        },
      },
    },
  );
  const tamperProposal = await stageChangeSet(
    tamperSession,
    twoFileChanges(),
  );
  await assert.rejects(
    tamperSession.workspaceEditProposalManager.commit(
      tamperProposal.proposalId,
      tamperProposal.beforeSha256,
    ),
    WorkspaceTransactionCrashForTest,
  );
  tamperSession.session.dispose();
  tamperSession = undefined;
  await writeFile(
    join(tamperRoot, "source.txt"),
    "tampered after interruption\n",
    "utf8",
  );
  await assert.rejects(
    createReadOnlyAgentSession(tamperRoot),
    error =>
      error?.code === "workspace-change-set-recovery-required",
  );
  assert.equal(
    await readFile(join(tamperRoot, "source.txt"), "utf8"),
    "tampered after interruption\n",
  );
  assert.equal(
    await readFile(join(tamperRoot, "test.txt"), "utf8"),
    "alpha\nbeta\ngamma\n",
  );

  console.log(JSON.stringify({
    schemaVersion: 1,
    receiptType:
      "jarvisv2-pi-workspace-change-set-approval-probe",
    result: "passed",
    minimumFiles: 2,
    maximumFiles: 4,
    maximumReviewBytes: 32768,
    mixedReplacePatchCreateApplied: true,
    proposalMutates: false,
    wholeSetRejectMutates: false,
    repeatedPathRejected: true,
    windowsCaseAliasRejected: process.platform === "win32",
    anyMemberDriftPreventsAllWrites: true,
    midCommitFailureRolledBack: true,
    preCommitCrashRecoveredBeforeTools: true,
    committedCrashCompletedCleanup: true,
    tamperedRecoveryFailedClosed: true,
    simultaneousVisibilityClaimed: false,
    shellAvailableToPi: false,
    recoveryAvailableToPi: false,
    liveExplorer: "not-run",
  }, null, 2));
} finally {
  for (const session of [
    applySession,
    rejectionSession,
    caseAliasSession,
    driftSession,
    rollbackSession,
    crashSession,
    recoveredSession,
    committedCrashSession,
    completedRecoverySession,
    tamperSession,
  ]) {
    session?.session.dispose();
  }
  await rm(temporaryRoot, { recursive: true, force: true });
}
