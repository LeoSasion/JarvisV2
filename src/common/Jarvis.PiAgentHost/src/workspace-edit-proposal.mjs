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
  assertWorkspaceCreationPath,
  WorkspacePolicyError,
} from "./workspace-policy.mjs";

export const maximumWorkspaceEditFileBytes = 1_048_576;
export const maximumWorkspaceEditSegmentBytes = 4_096;
export const maximumWorkspaceCreateFileBytes = 16_384;
export const maximumWorkspaceEditRelativePathCharacters = 512;

const proposalIdPattern =
  /^workspace-edit-[0-9a-f]{32}$/u;
const sha256Pattern = /^[0-9a-f]{64}$/u;

function sha256(content) {
  return createHash("sha256")
    .update(content, "utf8")
    .digest("hex");
}

export const workspaceFileAbsentSha256 = sha256(
  "JARVIS2/workspace-file-absent/v1",
);

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

function isStrictUtf8Text(value) {
  return (
    typeof value === "string" &&
    Buffer.from(value, "utf8").toString("utf8") === value &&
    !value.includes("\u0000")
  );
}

function validateProposalText(oldText, newText) {
  if (
    !isStrictUtf8Text(oldText) ||
    oldText.length === 0 ||
    Buffer.byteLength(oldText, "utf8") >
      maximumWorkspaceEditSegmentBytes ||
    !isStrictUtf8Text(newText) ||
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

function validateCreateContent(content) {
  if (
    !isStrictUtf8Text(content) ||
    Buffer.byteLength(content, "utf8") === 0 ||
    Buffer.byteLength(content, "utf8") >
      maximumWorkspaceCreateFileBytes ||
    /[\u0001-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(content)
  ) {
    throw new WorkspacePolicyError(
      "invalid-workspace-create",
      "A new workspace file must contain 1-16384 bytes of strictly valid UTF-8 text without binary control characters.",
    );
  }
}

function reviewRelativePath(admission, safePath) {
  const relativePath = relative(
    admission.canonicalRoot,
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
  if (relativePath.split("/").some(segment =>
    [".git", ".hg", ".svn"].includes(segment.toLowerCase()))) {
    throw new WorkspacePolicyError(
      "workspace-vcs-metadata-forbidden",
      "Workspace proposals cannot mutate version-control metadata.",
    );
  }
  return relativePath;
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
    pending.proposal.proposalId !== proposalId ||
    pending.proposal.beforeSha256 !== beforeSha256
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
    const relativePath = reviewRelativePath(
      this.#admission,
      safePath,
    );
    const proposal = Object.freeze({
      schemaVersion: 2,
      proposalId: `workspace-edit-${randomUUID().replaceAll("-", "")}`,
      operation: "replace",
      relativePath,
      beforeSha256: sha256(content),
      oldText,
      newText,
    });
    this.#pending = Object.freeze({ proposal });
    return proposal;
  }

  async proposeCreate(
    { path, content },
    signal,
  ) {
    throwIfAborted(signal);
    if (this.#pending !== null) {
      throw new WorkspacePolicyError(
        "workspace-edit-review-pending",
        "Review the pending workspace proposal before proposing another change.",
      );
    }
    validateCreateContent(content);
    const creation = await assertWorkspaceCreationPath(
      this.#admission,
      path,
    );
    throwIfAborted(signal);
    const relativePath = reviewRelativePath(
      this.#admission,
      creation.safePath,
    );
    const proposal = Object.freeze({
      schemaVersion: 2,
      proposalId: `workspace-edit-${randomUUID().replaceAll("-", "")}`,
      operation: "create",
      relativePath,
      beforeSha256: workspaceFileAbsentSha256,
      oldText: "",
      newText: content,
    });
    this.#pending = Object.freeze({
      proposal,
      parentDevice: creation.parentDevice,
      parentInode: creation.parentInode,
    });
    return proposal;
  }

  async commit(proposalId, beforeSha256) {
    validateDecisionIdentity(proposalId, beforeSha256);
    validatePendingIdentity(
      this.#pending,
      proposalId,
      beforeSha256,
    );
    const pending = this.#pending;
    const proposal = pending.proposal;
    this.#pending = null;

    if (proposal.operation === "create") {
      return this.#commitCreate(proposal, pending);
    }
    return this.#commitReplace(proposal);
  }

  async #commitReplace(proposal) {

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
        schemaVersion: 2,
        proposalId: proposal.proposalId,
        operation: proposal.operation,
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

  async #commitCreate(proposal, pending) {
    let createdIdentity;
    let committed = false;
    let safePath;
    try {
      let creation;
      try {
        creation = await assertWorkspaceCreationPath(
          this.#admission,
          proposal.relativePath,
        );
      } catch (error) {
        if (error?.code === "workspace-file-already-exists") {
          throw new WorkspacePolicyError(
            "workspace-edit-drifted",
            "The target path appeared after proposal review began; the one-shot approval was not applied.",
          );
        }
        throw error;
      }
      safePath = creation.safePath;
      if (
        creation.parentDevice !== pending.parentDevice ||
        creation.parentInode !== pending.parentInode
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-drifted",
          "The target parent directory changed after proposal review began; the one-shot approval was not applied.",
        );
      }

      let created;
      try {
        created = await open(safePath, "wx", 0o600);
      } catch (error) {
        if (error?.code === "EEXIST") {
          throw new WorkspacePolicyError(
            "workspace-edit-drifted",
            "The target path appeared before the exclusive create; the one-shot approval was not applied.",
          );
        }
        throw error;
      }
      try {
        const stats = await created.stat();
        if (!stats.isFile() || stats.nlink !== 1) {
          throw new WorkspacePolicyError(
            "workspace-path-not-single-file",
            "New workspace files must be created as regular single-link files.",
          );
        }
        createdIdentity = Object.freeze({
          device: String(stats.dev),
          inode: String(stats.ino),
        });
        await created.writeFile(proposal.newText, "utf8");
        await created.sync();
      } finally {
        await created.close();
      }

      const parent = await lstat(creation.parentPath);
      if (
        String(parent.dev) !== pending.parentDevice ||
        String(parent.ino) !== pending.parentInode
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-drifted",
          "The target parent directory changed during the approved creation.",
        );
      }
      const createdFile = await readWorkspaceTextFile(
        this.#admission,
        proposal.relativePath,
      );
      const afterSha256 = sha256(createdFile.content);
      if (
        String(createdFile.stats.dev) !== createdIdentity.device ||
        String(createdFile.stats.ino) !== createdIdentity.inode ||
        createdFile.content !== proposal.newText ||
        afterSha256 === proposal.beforeSha256
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-commit-verification-failed",
          "The approved new file did not reach the exact reviewed state.",
        );
      }
      committed = true;
      return Object.freeze({
        schemaVersion: 2,
        proposalId: proposal.proposalId,
        operation: proposal.operation,
        relativePath: proposal.relativePath,
        beforeSha256: proposal.beforeSha256,
        afterSha256,
        status: "applied",
        mutationPerformed: true,
      });
    } finally {
      if (!committed && createdIdentity !== undefined && safePath !== undefined) {
        try {
          const creation = await assertWorkspaceCreationPath(
            this.#admission,
            proposal.relativePath,
          );
          void creation;
        } catch (error) {
          if (error?.code === "workspace-file-already-exists") {
            const currentParent = await lstat(
              dirname(safePath),
            ).catch(() => null);
            const current = await lstat(safePath).catch(() => null);
            if (
              currentParent !== null &&
              String(currentParent.dev) === pending.parentDevice &&
              String(currentParent.ino) === pending.parentInode &&
              current !== null &&
              String(current.dev) === createdIdentity.device &&
              String(current.ino) === createdIdentity.inode
            ) {
              await rm(safePath, { force: true }).catch(() => {});
            }
          }
        }
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
    const proposal = this.#pending.proposal;
    this.#pending = null;
    return Object.freeze({
      schemaVersion: 2,
      proposalId,
      operation: proposal.operation,
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

export function createWorkspaceFileProposalTool(
  admission,
  proposalManager,
) {
  return {
    name: "propose_create_file",
    label: "Propose new file",
    description:
      "Stage one new UTF-8 workspace file for explicit desktop-owner review. The parent directory must already exist; this tool never writes the file.",
    promptSnippet:
      "propose_create_file: stage one new UTF-8 file for owner review (no write)",
    promptGuidelines: [
      "Use propose_create_file only when the target does not exist and its parent directory already exists.",
      "A proposal pauses new turns until the desktop owner approves or rejects it.",
      "Approval is not available to the model and cannot be assumed.",
    ],
    parameters: Type.Object(
      {
        path: Type.String({
          description:
            "Missing file path inside the admitted workspace with an existing parent directory",
          minLength: 1,
          maxLength: maximumWorkspaceEditRelativePathCharacters,
        }),
        content: Type.String({
          description:
            "Complete UTF-8 text content for the proposed new file",
          minLength: 1,
          maxLength: maximumWorkspaceCreateFileBytes,
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
      const proposal = await proposalManager.proposeCreate(
        args,
        signal,
      );
      return {
        content: [{
          type: "text",
          text:
            `New workspace file ${proposal.proposalId} is staged for desktop-owner review. No file was created.`,
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
    proposal?.schemaVersion !== 2 ||
    typeof proposal.proposalId !== "string" ||
    !proposalIdPattern.test(proposal.proposalId) ||
    !["replace", "create"].includes(proposal.operation) ||
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
    !isStrictUtf8Text(proposal.oldText) ||
    !isStrictUtf8Text(proposal.newText) ||
    proposal.relativePath.split("/").some(segment =>
      segment === "." ||
      segment === ".." ||
      [".git", ".hg", ".svn"].includes(segment.toLowerCase())) ||
    /[\u0000-\u001f\u007f]/u.test(proposal.relativePath)
  ) {
    return null;
  }
  const replaceValid =
    proposal.operation === "replace" &&
    proposal.oldText.length !== 0 &&
    Buffer.byteLength(proposal.oldText, "utf8") <=
      maximumWorkspaceEditSegmentBytes &&
    Buffer.byteLength(proposal.newText, "utf8") <=
      maximumWorkspaceEditSegmentBytes &&
    proposal.newText !== proposal.oldText;
  const createValid =
    proposal.operation === "create" &&
    proposal.beforeSha256 === workspaceFileAbsentSha256 &&
    proposal.oldText.length === 0 &&
    Buffer.byteLength(proposal.newText, "utf8") > 0 &&
    Buffer.byteLength(proposal.newText, "utf8") <=
      maximumWorkspaceCreateFileBytes &&
    !/[\u0001-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(
      proposal.newText,
    );
  if (!replaceValid && !createValid) {
    return null;
  }
  return proposal;
}
