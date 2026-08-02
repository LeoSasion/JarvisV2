import {
  isUtf8,
} from "node:buffer";
import {
  createHash,
  randomUUID,
} from "node:crypto";
import {
  lstat,
  open,
  rename,
  rm,
} from "node:fs/promises";
import {
  dirname,
  join,
  relative,
  sep,
} from "node:path";
import {
  Type,
} from "./pi-sdk-adapter.mjs";
import {
  assertWorkspacePath,
  WorkspacePolicyError,
} from "./workspace-policy.mjs";

export const maximumWorkspaceEditFileBytes = 1_048_576;
export const maximumWorkspaceEditSegmentBytes = 4_096;
export const maximumWorkspaceEditRelativePathCharacters = 512;

const proposalIdPattern =
  /^workspace-edit-[0-9a-f]{32}$/u;
const sha256Pattern = /^[0-9a-f]{64}$/u;

function sha256(content) {
  return createHash("sha256")
    .update(content, "utf8")
    .digest("hex");
}

function throwIfAborted(signal) {
  if (signal?.aborted) {
    throw new WorkspacePolicyError(
      "workspace-edit-aborted",
      "The workspace edit proposal was aborted.",
    );
  }
}

function countOccurrences(content, search) {
  const first = content.indexOf(search);
  if (first < 0) {
    return 0;
  }
  return content.indexOf(search, first + 1) < 0 ? 1 : 2;
}

function validateProposalText(oldText, newText) {
  if (
    typeof oldText !== "string" ||
    oldText.length === 0 ||
    Buffer.byteLength(oldText, "utf8") >
      maximumWorkspaceEditSegmentBytes ||
    typeof newText !== "string" ||
    Buffer.byteLength(newText, "utf8") >
      maximumWorkspaceEditSegmentBytes ||
    oldText === newText
  ) {
    throw new WorkspacePolicyError(
      "invalid-workspace-edit",
      "An edit must replace 1-4096 UTF-8 bytes with a distinct value of at most 4096 bytes.",
    );
  }
}

async function readWorkspaceTextFile(admission, requestedPath) {
  const safePath = await assertWorkspacePath(
    admission,
    requestedPath,
  );
  const handle = await open(safePath, "r");
  let stats;
  let bytes;
  try {
    stats = await handle.stat();
    if (!stats.isFile() || stats.nlink !== 1) {
      throw new WorkspacePolicyError(
        "workspace-path-not-single-file",
        "Workspace edit proposals accept existing single-link files only.",
      );
    }
    if (stats.size > maximumWorkspaceEditFileBytes) {
      throw new WorkspacePolicyError(
        "workspace-file-too-large",
        "Workspace edit proposals are limited to one MiB text files.",
      );
    }
    bytes = await readBoundedFile(handle);
  } finally {
    await handle.close();
  }
  const pathStats = await lstat(safePath);
  if (
    !pathStats.isFile() ||
    pathStats.nlink !== 1 ||
    String(pathStats.dev) !== String(stats.dev) ||
    String(pathStats.ino) !== String(stats.ino)
  ) {
    throw new WorkspacePolicyError(
      "workspace-file-identity-changed",
      "The workspace file identity changed while it was being read.",
    );
  }
  if (!isUtf8(bytes)) {
    throw new WorkspacePolicyError(
      "workspace-file-not-utf8",
      "Workspace edit proposals require strictly valid UTF-8 text.",
    );
  }
  const content = bytes.toString("utf8");
  if (content.includes("\u0000")) {
    throw new WorkspacePolicyError(
      "binary-file-forbidden",
      "Workspace edit proposals accept UTF-8 text files only.",
    );
  }
  return { safePath, stats: pathStats, content };
}

async function readBoundedFile(handle) {
  const chunks = [];
  let total = 0;
  while (total <= maximumWorkspaceEditFileBytes) {
    const capacity = Math.min(
      65_536,
      maximumWorkspaceEditFileBytes + 1 - total,
    );
    const buffer = Buffer.allocUnsafe(capacity);
    const { bytesRead } = await handle.read(
      buffer,
      0,
      capacity,
      total,
    );
    if (bytesRead === 0) {
      break;
    }
    chunks.push(buffer.subarray(0, bytesRead));
    total += bytesRead;
  }
  if (total > maximumWorkspaceEditFileBytes) {
    throw new WorkspacePolicyError(
      "workspace-file-too-large",
      "Workspace edit proposals are limited to one MiB text files.",
    );
  }
  return Buffer.concat(chunks, total);
}

function validateDecisionIdentity(proposalId, beforeSha256) {
  if (
    typeof proposalId !== "string" ||
    !proposalIdPattern.test(proposalId) ||
    typeof beforeSha256 !== "string" ||
    !sha256Pattern.test(beforeSha256)
  ) {
    throw new WorkspacePolicyError(
      "invalid-workspace-edit-decision",
      "Workspace edit decisions require the exact proposal id and lowercase SHA-256.",
    );
  }
}

function validatePendingIdentity(
  pending,
  proposalId,
  beforeSha256,
) {
  if (pending === null) {
    throw new WorkspacePolicyError(
      "workspace-edit-not-pending",
      "No workspace edit proposal is pending review.",
    );
  }
  if (
    pending.proposalId !== proposalId ||
    pending.beforeSha256 !== beforeSha256
  ) {
    throw new WorkspacePolicyError(
      "workspace-edit-proposal-mismatch",
      "The decision did not match the pending proposal capability.",
    );
  }
}

export class WorkspaceEditProposalManager {
  #admission;
  #pending = null;

  constructor(admission) {
    this.#admission = admission;
  }

  get hasPending() {
    return this.#pending !== null;
  }

  async propose(
    { path, oldText, newText },
    signal,
  ) {
    throwIfAborted(signal);
    if (this.#pending !== null) {
      throw new WorkspacePolicyError(
        "workspace-edit-review-pending",
        "Review the pending workspace edit before proposing another change.",
      );
    }
    validateProposalText(oldText, newText);
    const { safePath, content } = await readWorkspaceTextFile(
      this.#admission,
      path,
    );
    throwIfAborted(signal);
    if (countOccurrences(content, oldText) !== 1) {
      throw new WorkspacePolicyError(
        "workspace-edit-match-not-unique",
        "The proposed oldText must occur exactly once in the current file.",
      );
    }
    const updated = content.replace(oldText, newText);
    if (
      Buffer.byteLength(updated, "utf8") >
        maximumWorkspaceEditFileBytes
    ) {
      throw new WorkspacePolicyError(
        "workspace-edit-result-too-large",
        "The proposed file would exceed one MiB.",
      );
    }
    const relativePath = relative(
      this.#admission.canonicalRoot,
      safePath,
    ).split(sep).join("/");
    if (
      relativePath.length === 0 ||
      relativePath.length >
        maximumWorkspaceEditRelativePathCharacters
    ) {
      throw new WorkspacePolicyError(
        "workspace-edit-path-too-long",
        "The proposed workspace-relative path is outside the review display boundary.",
      );
    }
    this.#pending = Object.freeze({
      schemaVersion: 1,
      proposalId: `workspace-edit-${randomUUID().replaceAll("-", "")}`,
      relativePath,
      beforeSha256: sha256(content),
      oldText,
      newText,
    });
    return this.#pending;
  }

  async commit(proposalId, beforeSha256) {
    validateDecisionIdentity(proposalId, beforeSha256);
    validatePendingIdentity(
      this.#pending,
      proposalId,
      beforeSha256,
    );
    const proposal = this.#pending;
    this.#pending = null;

    let temporaryPath;
    try {
      const first = await readWorkspaceTextFile(
        this.#admission,
        proposal.relativePath,
      );
      if (
        sha256(first.content) !== proposal.beforeSha256 ||
        countOccurrences(first.content, proposal.oldText) !== 1
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-drifted",
          "The file changed after proposal review began; the one-shot approval was not applied.",
        );
      }
      const updated = first.content.replace(
        proposal.oldText,
        proposal.newText,
      );
      if (
        Buffer.byteLength(updated, "utf8") >
          maximumWorkspaceEditFileBytes
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-result-too-large",
          "The approved file would exceed one MiB.",
        );
      }

      temporaryPath = join(
        dirname(first.safePath),
        `.jarvis2-${randomUUID()}.tmp`,
      );
      const temporary = await open(
        temporaryPath,
        "wx",
        first.stats.mode,
      );
      try {
        await temporary.writeFile(updated, "utf8");
        await temporary.sync();
      } finally {
        await temporary.close();
      }

      const final = await readWorkspaceTextFile(
        this.#admission,
        proposal.relativePath,
      );
      if (
        String(final.stats.dev) !== String(first.stats.dev) ||
        String(final.stats.ino) !== String(first.stats.ino) ||
        sha256(final.content) !== proposal.beforeSha256 ||
        countOccurrences(final.content, proposal.oldText) !== 1
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-drifted",
          "The file identity or content changed immediately before commit.",
        );
      }

      await rename(temporaryPath, first.safePath);
      temporaryPath = undefined;
      const committed = await readWorkspaceTextFile(
        this.#admission,
        proposal.relativePath,
      );
      const afterSha256 = sha256(committed.content);
      if (
        committed.content !== updated ||
        afterSha256 === proposal.beforeSha256
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-commit-verification-failed",
          "The approved edit did not reach the expected committed state.",
        );
      }
      return Object.freeze({
        schemaVersion: 1,
        proposalId,
        relativePath: proposal.relativePath,
        beforeSha256: proposal.beforeSha256,
        afterSha256,
        status: "applied",
        mutationPerformed: true,
      });
    } finally {
      if (temporaryPath !== undefined) {
        await rm(temporaryPath, { force: true }).catch(() => {});
      }
    }
  }

  discard(proposalId, beforeSha256) {
    validateDecisionIdentity(proposalId, beforeSha256);
    validatePendingIdentity(
      this.#pending,
      proposalId,
      beforeSha256,
    );
    const proposal = this.#pending;
    this.#pending = null;
    return Object.freeze({
      schemaVersion: 1,
      proposalId,
      relativePath: proposal.relativePath,
      beforeSha256: proposal.beforeSha256,
      afterSha256: null,
      status: "rejected",
      mutationPerformed: false,
    });
  }

  clear() {
    this.#pending = null;
  }
}

export function createWorkspaceEditProposalTool(
  admission,
  proposalManager,
) {
  return {
    name: "propose_edit",
    label: "Propose edit",
    description:
      "Stage one exact replacement in an existing UTF-8 workspace file for explicit desktop-owner review. This tool never writes the file.",
    promptSnippet:
      "propose_edit: stage one existing-file text replacement for owner review (no write)",
    promptGuidelines: [
      "Use propose_edit only after reading the target file.",
      "A proposal pauses new turns until the desktop owner approves or rejects it.",
      "Approval is not available to the model and cannot be assumed.",
    ],
    parameters: Type.Object(
      {
        path: Type.String({
          description:
            "Existing UTF-8 file path inside the admitted workspace",
          minLength: 1,
          maxLength: maximumWorkspaceEditRelativePathCharacters,
        }),
        oldText: Type.String({
          description:
            "Exact non-empty text occurring once in the current file",
          minLength: 1,
          maxLength: maximumWorkspaceEditSegmentBytes,
        }),
        newText: Type.String({
          description:
            "Replacement text; may be empty and must differ from oldText",
          maxLength: maximumWorkspaceEditSegmentBytes,
        }),
      },
      { additionalProperties: false },
    ),
    executionMode: "sequential",
    async execute(
      _toolCallId,
      args,
      signal,
    ) {
      const proposal = await proposalManager.propose(
        args,
        signal,
      );
      return {
        content: [{
          type: "text",
          text:
            `Workspace edit ${proposal.proposalId} is staged for desktop-owner review. No file was changed.`,
        }],
        details: {
          workspaceEditProposal: proposal,
        },
      };
    },
  };
}

export function extractWorkspaceEditProposal(result) {
  const proposal = result?.details?.workspaceEditProposal;
  if (
    proposal?.schemaVersion !== 1 ||
    typeof proposal.proposalId !== "string" ||
    !proposalIdPattern.test(proposal.proposalId) ||
    typeof proposal.relativePath !== "string" ||
    proposal.relativePath.length === 0 ||
    proposal.relativePath.length >
      maximumWorkspaceEditRelativePathCharacters ||
    proposal.relativePath.includes("\\") ||
    proposal.relativePath.includes(":") ||
    proposal.relativePath.includes("//") ||
    proposal.relativePath.startsWith("/") ||
    proposal.relativePath.endsWith("/") ||
    typeof proposal.beforeSha256 !== "string" ||
    !sha256Pattern.test(proposal.beforeSha256) ||
    typeof proposal.oldText !== "string" ||
    proposal.oldText.length === 0 ||
    Buffer.byteLength(proposal.oldText, "utf8") >
      maximumWorkspaceEditSegmentBytes ||
    typeof proposal.newText !== "string" ||
    Buffer.byteLength(proposal.newText, "utf8") >
      maximumWorkspaceEditSegmentBytes ||
    proposal.newText === proposal.oldText ||
    proposal.relativePath.split("/").some(segment =>
      segment === "." || segment === "..") ||
    /[\u0000-\u001f\u007f]/u.test(proposal.relativePath)
  ) {
    return null;
  }
  return proposal;
}
