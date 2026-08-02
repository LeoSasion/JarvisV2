import {
  isUtf8,
} from "node:buffer";
import {
  createHash,
  randomUUID,
} from "node:crypto";
import {
  lstat,
  link,
  open,
  readdir,
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
export const minimumWorkspacePatchHunks = 2;
export const maximumWorkspacePatchHunks = 8;
export const maximumWorkspacePatchPreviewBytes = 16_384;
export const maximumWorkspaceEditRelativePathCharacters = 512;
export const minimumWorkspaceChangeSetFiles = 2;
export const maximumWorkspaceChangeSetFiles = 4;
export const maximumWorkspaceChangeSetPreviewBytes = 32_768;

export const workspaceTransactionJournalName =
  ".jarvis2-workspace-transaction.json";
const workspaceTransactionArtifactPrefix =
  ".jarvis2-workspace-transaction-";

const proposalIdPattern =
  /^workspace-edit-[0-9a-f]{32}$/u;
const sha256Pattern = /^[0-9a-f]{64}$/u;

function sha256(content) {
  return createHash("sha256")
    .update(content, "utf8")
    .digest("hex");
}

function hasExactKeys(value, keys) {
  if (
    value === null ||
    typeof value !== "object" ||
    Array.isArray(value)
  ) {
    return false;
  }
  const actual = Object.keys(value).sort();
  const expected = [...keys].sort();
  return actual.length === expected.length &&
    actual.every((key, index) => key === expected[index]);
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

function containsBinaryControlCharacters(value) {
  return /[\u0001-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(
    value,
  );
}

function validatePatchHunks(replacements) {
  if (
    !Array.isArray(replacements) ||
    replacements.length < minimumWorkspacePatchHunks ||
    replacements.length > maximumWorkspacePatchHunks
  ) {
    throw new WorkspacePolicyError(
      "invalid-workspace-patch",
      "A workspace patch must contain 2-8 exact replacements in one file.",
    );
  }
  let previewBytes = 0;
  const oldTexts = new Set();
  const patchHunks = replacements.map((replacement, index) => {
    const oldText = replacement?.oldText;
    const newText = replacement?.newText;
    validateProposalText(oldText, newText);
    if (
      containsBinaryControlCharacters(oldText) ||
      containsBinaryControlCharacters(newText)
    ) {
      throw new WorkspacePolicyError(
        "invalid-workspace-patch",
        "Workspace patch text cannot contain binary control characters.",
      );
    }
    if (oldTexts.has(oldText)) {
      throw new WorkspacePolicyError(
        "invalid-workspace-patch",
        "Every workspace patch oldText must be distinct.",
      );
    }
    oldTexts.add(oldText);
    previewBytes +=
      Buffer.byteLength(oldText, "utf8") +
      Buffer.byteLength(newText, "utf8");
    return Object.freeze({
      ordinal: index + 1,
      oldText,
      newText,
    });
  });
  if (previewBytes > maximumWorkspacePatchPreviewBytes) {
    throw new WorkspacePolicyError(
      "invalid-workspace-patch",
      "The combined workspace patch review text must not exceed 16384 UTF-8 bytes.",
    );
  }
  return Object.freeze(patchHunks);
}

function applyPatchHunks(content, patchHunks) {
  const positioned = patchHunks.map(hunk => {
    if (countOccurrences(content, hunk.oldText) !== 1) {
      throw new WorkspacePolicyError(
        "workspace-patch-match-not-unique",
        "Every patch oldText must occur exactly once in the current file.",
      );
    }
    const start = content.indexOf(hunk.oldText);
    return Object.freeze({
      ...hunk,
      start,
      end: start + hunk.oldText.length,
    });
  }).sort((left, right) => left.start - right.start);
  for (let index = 1; index < positioned.length; index += 1) {
    if (positioned[index].start < positioned[index - 1].end) {
      throw new WorkspacePolicyError(
        "workspace-patch-overlap",
        "Workspace patch replacements must not overlap in the current file.",
      );
    }
  }
  let cursor = 0;
  let updated = "";
  for (const hunk of positioned) {
    updated += content.slice(cursor, hunk.start);
    updated += hunk.newText;
    cursor = hunk.end;
  }
  return updated + content.slice(cursor);
}

function validateCreateContent(content) {
  if (
    !isStrictUtf8Text(content) ||
    Buffer.byteLength(content, "utf8") === 0 ||
    Buffer.byteLength(content, "utf8") >
      maximumWorkspaceCreateFileBytes ||
    containsBinaryControlCharacters(content)
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
  if (relativePath.split("/").some(segment =>
    segment.toLowerCase() === workspaceTransactionJournalName ||
    segment.toLowerCase().startsWith(
      workspaceTransactionArtifactPrefix,
    ))) {
    throw new WorkspacePolicyError(
      "workspace-transaction-path-reserved",
      "Workspace proposals cannot target the reserved transaction journal or artifacts.",
    );
  }
  return relativePath;
}

function workspacePathIdentityKey(relativePath) {
  return process.platform === "win32"
    ? relativePath.toLowerCase()
    : relativePath;
}

function addUniqueWorkspacePath(paths, relativePath) {
  const identity = workspacePathIdentityKey(relativePath);
  if (paths.has(identity)) {
    return false;
  }
  paths.add(identity);
  return true;
}

function changeSetReviewDigest(changes) {
  const material = ["JARVIS2/workspace-change-set-review/v1"];
  for (const change of changes) {
    material.push(
      String(change.ordinal),
      change.operation,
      change.relativePath,
      change.beforeSha256,
      change.oldText,
      change.newText,
      String(change.patchHunks.length),
    );
    for (const hunk of change.patchHunks) {
      material.push(
        String(hunk.ordinal),
        hunk.oldText,
        hunk.newText,
      );
    }
  }
  return sha256(material.join("\u0000"));
}

function changeSetAfterDigest(changes) {
  return sha256(
    "JARVIS2/workspace-change-set-after/v1\u0000" +
      changes.map(change =>
        `${change.ordinal}\u0000${change.relativePath}\u0000${change.afterSha256}`,
      ).join("\u0000"),
  );
}

function validateChangeSetInput(changes) {
  if (
    !Array.isArray(changes) ||
    changes.length < minimumWorkspaceChangeSetFiles ||
    changes.length > maximumWorkspaceChangeSetFiles
  ) {
    throw new WorkspacePolicyError(
      "invalid-workspace-change-set",
      "A workspace change set must contain two to four files.",
    );
  }
  let previewBytes = 0;
  const admitted = changes.map((change, index) => {
    if (change?.operation === "replace") {
      if (!hasExactKeys(
        change,
        ["operation", "path", "oldText", "newText"],
      )) {
        throw new WorkspacePolicyError(
          "invalid-workspace-change-set",
          "A change-set replacement must contain only operation, path, oldText and newText.",
        );
      }
      validateProposalText(change.oldText, change.newText);
      previewBytes +=
        Buffer.byteLength(change.oldText, "utf8") +
        Buffer.byteLength(change.newText, "utf8");
      return Object.freeze({
        ordinal: index + 1,
        operation: "replace",
        path: change.path,
        oldText: change.oldText,
        newText: change.newText,
        patchHunks: Object.freeze([]),
      });
    }
    if (change?.operation === "patch") {
      if (!hasExactKeys(
        change,
        ["operation", "path", "replacements"],
      )) {
        throw new WorkspacePolicyError(
          "invalid-workspace-change-set",
          "A change-set patch must contain only operation, path and replacements.",
        );
      }
      const patchHunks = validatePatchHunks(change.replacements);
      previewBytes += patchHunks.reduce(
        (total, hunk) => total +
          Buffer.byteLength(hunk.oldText, "utf8") +
          Buffer.byteLength(hunk.newText, "utf8"),
        0,
      );
      return Object.freeze({
        ordinal: index + 1,
        operation: "patch",
        path: change.path,
        oldText: "",
        newText: "",
        patchHunks,
      });
    }
    if (change?.operation === "create") {
      if (!hasExactKeys(
        change,
        ["operation", "path", "content"],
      )) {
        throw new WorkspacePolicyError(
          "invalid-workspace-change-set",
          "A change-set creation must contain only operation, path and content.",
        );
      }
      validateCreateContent(change.content);
      previewBytes += Buffer.byteLength(change.content, "utf8");
      return Object.freeze({
        ordinal: index + 1,
        operation: "create",
        path: change.path,
        oldText: "",
        newText: change.content,
        patchHunks: Object.freeze([]),
      });
    }
    throw new WorkspacePolicyError(
      "invalid-workspace-change-set",
      "Every change-set file must use replace, patch or create.",
    );
  });
  if (previewBytes > maximumWorkspaceChangeSetPreviewBytes) {
    throw new WorkspacePolicyError(
      "workspace-change-set-review-too-large",
      "The complete change-set review text must not exceed 32768 UTF-8 bytes.",
    );
  }
  return Object.freeze(admitted);
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

async function pathExists(candidate) {
  try {
    await lstat(candidate);
    return true;
  } catch (error) {
    if (error?.code === "ENOENT") {
      return false;
    }
    throw error;
  }
}

async function readTransactionTextFile(
  absolutePath,
  maximumBytes = maximumWorkspaceEditFileBytes,
) {
  const handle = await open(absolutePath, "r");
  let stats;
  let bytes;
  try {
    stats = await handle.stat();
    if (
      !stats.isFile() ||
      stats.isSymbolicLink() ||
      stats.nlink < 1 ||
      stats.nlink > 2 ||
      stats.size > maximumBytes
    ) {
      throw new WorkspacePolicyError(
        "workspace-transaction-artifact-invalid",
        "A workspace transaction artifact failed its regular-file boundary.",
      );
    }
    const chunks = [];
    let total = 0;
    while (total <= maximumBytes) {
      const capacity = Math.min(
        65_536,
        maximumBytes + 1 - total,
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
    if (total > maximumBytes) {
      throw new WorkspacePolicyError(
        "workspace-transaction-artifact-invalid",
        "A workspace transaction artifact exceeded its byte boundary.",
      );
    }
    bytes = Buffer.concat(chunks, total);
  } finally {
    await handle.close();
  }
  const current = await lstat(absolutePath);
  if (
    !current.isFile() ||
    current.isSymbolicLink() ||
    String(current.dev) !== String(stats.dev) ||
    String(current.ino) !== String(stats.ino) ||
    !isUtf8(bytes)
  ) {
    throw new WorkspacePolicyError(
      "workspace-transaction-artifact-invalid",
      "A workspace transaction artifact changed identity or was not strict UTF-8.",
    );
  }
  const content = bytes.toString("utf8");
  if (content.includes("\u0000")) {
    throw new WorkspacePolicyError(
      "workspace-transaction-artifact-invalid",
      "A workspace transaction artifact contained binary NUL data.",
    );
  }
  return Object.freeze({
    stats: current,
    content,
  });
}

async function writeFlushedTransactionFile(
  absolutePath,
  content,
  mode,
) {
  const handle = await open(absolutePath, "wx", mode);
  try {
    await handle.writeFile(content, "utf8");
    await handle.sync();
    const stats = await handle.stat();
    if (!stats.isFile() || stats.nlink !== 1) {
      throw new WorkspacePolicyError(
        "workspace-transaction-artifact-invalid",
        "A workspace transaction artifact was not a single-link regular file.",
      );
    }
    return Object.freeze({
      device: String(stats.dev),
      inode: String(stats.ino),
    });
  } finally {
    await handle.close();
  }
}

function transactionArtifactRelativePath(
  relativePath,
  proposalId,
  ordinal,
  role,
) {
  const parent = dirname(relativePath);
  const name =
    `${workspaceTransactionArtifactPrefix}${proposalId}-${ordinal}-${role}.tmp`;
  return (parent === "." ? name : join(parent, name))
    .split(sep)
    .join("/");
}

function transactionAbsolutePath(admission, relativePath) {
  const absolutePath = join(
    admission.canonicalRoot,
    relativePath.split("/").join(sep),
  );
  const parent = dirname(absolutePath);
  const relativeParent = relative(
    admission.canonicalRoot,
    parent,
  );
  if (
    relativeParent.startsWith(`..${sep}`) ||
    relativeParent === ".." ||
    relativePath.includes("\\") ||
    relativePath.includes(":") ||
    relativePath.startsWith("/") ||
    relativePath.split("/").some(segment =>
      segment === "" || segment === "." || segment === "..")
  ) {
    throw new WorkspacePolicyError(
      "workspace-transaction-path-invalid",
      "A workspace transaction path escaped its admitted root.",
    );
  }
  return absolutePath;
}

function transactionJournalPath(admission) {
  return join(
    admission.canonicalRoot,
    workspaceTransactionJournalName,
  );
}

function serializeTransactionJournal(journal) {
  const text = `${JSON.stringify(journal)}\n`;
  if (Buffer.byteLength(text, "utf8") > 65_536) {
    throw new WorkspacePolicyError(
      "workspace-transaction-journal-too-large",
      "The workspace transaction journal exceeded 64 KiB.",
    );
  }
  return text;
}

async function writeInitialTransactionJournal(
  admission,
  journal,
) {
  const journalPath = transactionJournalPath(admission);
  const temporaryPath = join(
    admission.canonicalRoot,
    `${workspaceTransactionArtifactPrefix}${journal.proposalId}-${randomUUID()}.tmp`,
  );
  try {
    await writeFlushedTransactionFile(
      temporaryPath,
      serializeTransactionJournal(journal),
      0o600,
    );
    try {
      await link(temporaryPath, journalPath);
    } catch (error) {
      if (error?.code === "EEXIST") {
        throw new WorkspacePolicyError(
          "workspace-transaction-already-active",
          "A durable workspace transaction already requires recovery.",
        );
      }
      throw error;
    }
  } finally {
    await rm(temporaryPath, { force: true }).catch(() => {});
  }
}

async function replaceTransactionJournal(
  admission,
  journal,
) {
  const temporaryPath = join(
    admission.canonicalRoot,
    `${workspaceTransactionArtifactPrefix}${journal.proposalId}-${randomUUID()}.tmp`,
  );
  try {
    await writeFlushedTransactionFile(
      temporaryPath,
      serializeTransactionJournal(journal),
      0o600,
    );
    await rename(
      temporaryPath,
      transactionJournalPath(admission),
    );
  } finally {
    await rm(temporaryPath, { force: true }).catch(() => {});
  }
}

function validTransactionIdentity(value) {
  return typeof value === "string" && /^\d+$/u.test(value);
}

function parseTransactionJournal(text, admission) {
  let journal;
  try {
    journal = JSON.parse(text);
  } catch {
    throw new WorkspacePolicyError(
      "workspace-transaction-journal-invalid",
      "The workspace transaction journal was not strict JSON.",
    );
  }
  if (
    !hasExactKeys(
      journal,
      [
        "schemaVersion",
        "receiptType",
        "transactionId",
        "proposalId",
        "reviewDigest",
        "expectedAfterDigest",
        "workspaceDevice",
        "workspaceInode",
        "phase",
        "files",
      ],
    ) ||
    journal.schemaVersion !== 1 ||
    journal.receiptType !==
      "jarvis2-workspace-change-set-transaction" ||
    typeof journal.transactionId !== "string" ||
    !/^workspace-transaction-[0-9a-f]{32}$/u.test(
      journal.transactionId,
    ) ||
    typeof journal.proposalId !== "string" ||
    !proposalIdPattern.test(journal.proposalId) ||
    typeof journal.reviewDigest !== "string" ||
    !sha256Pattern.test(journal.reviewDigest) ||
    typeof journal.expectedAfterDigest !== "string" ||
    !sha256Pattern.test(journal.expectedAfterDigest) ||
    journal.workspaceDevice !== admission.device ||
    journal.workspaceInode !== admission.inode ||
    !["preparing", "staged", "committing", "committed"]
      .includes(journal.phase) ||
    !Array.isArray(journal.files) ||
    journal.files.length < minimumWorkspaceChangeSetFiles ||
    journal.files.length > maximumWorkspaceChangeSetFiles
  ) {
    throw new WorkspacePolicyError(
      "workspace-transaction-journal-invalid",
      "The workspace transaction journal failed its exact envelope boundary.",
    );
  }
  const paths = new Set();
  const identitiesRequired = journal.phase !== "preparing";
  for (const [index, file] of journal.files.entries()) {
    if (
      !hasExactKeys(
        file,
        [
          "ordinal",
          "operation",
          "relativePath",
          "beforeSha256",
          "afterSha256",
          "mode",
          "targetDevice",
          "targetInode",
          "parentDevice",
          "parentInode",
          "stagedRelativePath",
          "stagedDevice",
          "stagedInode",
          "backupRelativePath",
          "backupDevice",
          "backupInode",
        ],
      ) ||
      file.ordinal !== index + 1 ||
      !["replace", "patch", "create"].includes(file.operation) ||
      typeof file.relativePath !== "string" ||
      file.relativePath.length === 0 ||
      file.relativePath.length >
        maximumWorkspaceEditRelativePathCharacters ||
      file.relativePath.includes("\\") ||
      file.relativePath.includes(":") ||
      file.relativePath.startsWith("/") ||
      file.relativePath.endsWith("/") ||
      file.relativePath.split("/").some(segment =>
        segment === "" ||
        segment === "." ||
        segment === ".." ||
        [".git", ".hg", ".svn"].includes(segment.toLowerCase()) ||
        segment.toLowerCase() === workspaceTransactionJournalName ||
        segment.toLowerCase().startsWith(
          workspaceTransactionArtifactPrefix,
        )) ||
      !addUniqueWorkspacePath(paths, file.relativePath) ||
      typeof file.beforeSha256 !== "string" ||
      !sha256Pattern.test(file.beforeSha256) ||
      typeof file.afterSha256 !== "string" ||
      !sha256Pattern.test(file.afterSha256) ||
      file.afterSha256 === file.beforeSha256 ||
      !Number.isInteger(file.mode) ||
      file.mode < 0 ||
      file.mode > 0xffff ||
      !validTransactionIdentity(file.parentDevice) ||
      !validTransactionIdentity(file.parentInode) ||
      file.stagedRelativePath !== transactionArtifactRelativePath(
        file.relativePath,
        journal.proposalId,
        file.ordinal,
        "after",
      ) ||
      (identitiesRequired &&
        (!validTransactionIdentity(file.stagedDevice) ||
          !validTransactionIdentity(file.stagedInode))) ||
      (!identitiesRequired &&
        !(
          file.stagedDevice === null &&
          file.stagedInode === null
        ))
    ) {
      throw new WorkspacePolicyError(
        "workspace-transaction-journal-invalid",
        "A workspace transaction journal file entry failed admission.",
      );
    }
    if (file.operation === "create") {
      if (
        file.beforeSha256 !== workspaceFileAbsentSha256 ||
        file.targetDevice !== null ||
        file.targetInode !== null ||
        file.backupRelativePath !== null ||
        file.backupDevice !== null ||
        file.backupInode !== null
      ) {
        throw new WorkspacePolicyError(
          "workspace-transaction-journal-invalid",
          "A workspace creation journal entry carried existing-file authority.",
        );
      }
      continue;
    }
    if (
      !validTransactionIdentity(file.targetDevice) ||
      !validTransactionIdentity(file.targetInode) ||
      file.backupRelativePath !== transactionArtifactRelativePath(
        file.relativePath,
        journal.proposalId,
        file.ordinal,
        "before",
      ) ||
      (identitiesRequired &&
        (!validTransactionIdentity(file.backupDevice) ||
          !validTransactionIdentity(file.backupInode))) ||
      (!identitiesRequired &&
        !(
          file.backupDevice === null &&
          file.backupInode === null
        ))
    ) {
      throw new WorkspacePolicyError(
        "workspace-transaction-journal-invalid",
        "An existing-file transaction journal entry failed identity admission.",
      );
    }
  }
  if (
    changeSetAfterDigest(journal.files) !==
      journal.expectedAfterDigest
  ) {
    throw new WorkspacePolicyError(
      "workspace-transaction-journal-invalid",
      "The workspace transaction after digest did not match its file receipts.",
    );
  }
  return journal;
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
  #recoveryReceipt = Object.freeze({
    schemaVersion: 1,
    receiptType: "jarvis2-workspace-change-set-recovery",
    result: "none",
    proposalId: null,
    fileCount: 0,
    mutationPerformed: false,
  });
  #transactionHooks;

  constructor(admission, options = {}) {
    this.#admission = admission;
    this.#transactionHooks = options.transactionHooks;
  }

  static async create(admission, options = {}) {
    const manager = new WorkspaceEditProposalManager(
      admission,
      options,
    );
    await manager.#recoverPersistedChangeSet();
    return manager;
  }

  get hasPending() {
    return this.#pending !== null;
  }

  get recoveryReceipt() {
    return this.#recoveryReceipt;
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
      schemaVersion: 3,
      proposalId: `workspace-edit-${randomUUID().replaceAll("-", "")}`,
      operation: "replace",
      relativePath,
      beforeSha256: sha256(content),
      oldText,
      newText,
      patchHunks: Object.freeze([]),
    });
    this.#pending = Object.freeze({
      proposal,
      expectedAfterSha256: sha256(updated),
    });
    return proposal;
  }

  async proposePatch(
    { path, replacements },
    signal,
  ) {
    throwIfAborted(signal);
    if (this.#pending !== null) {
      throw new WorkspacePolicyError(
        "workspace-edit-review-pending",
        "Review the pending workspace proposal before proposing another change.",
      );
    }
    const patchHunks = validatePatchHunks(replacements);
    const { safePath, content } = await readWorkspaceTextFile(
      this.#admission,
      path,
    );
    throwIfAborted(signal);
    const updated = applyPatchHunks(content, patchHunks);
    if (
      Buffer.byteLength(updated, "utf8") >
        maximumWorkspaceEditFileBytes
    ) {
      throw new WorkspacePolicyError(
        "workspace-edit-result-too-large",
        "The proposed patched file would exceed one MiB.",
      );
    }
    const relativePath = reviewRelativePath(
      this.#admission,
      safePath,
    );
    const proposal = Object.freeze({
      schemaVersion: 3,
      proposalId: `workspace-edit-${randomUUID().replaceAll("-", "")}`,
      operation: "patch",
      relativePath,
      beforeSha256: sha256(content),
      oldText: "",
      newText: "",
      patchHunks,
    });
    this.#pending = Object.freeze({
      proposal,
      expectedAfterSha256: sha256(updated),
    });
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
      schemaVersion: 3,
      proposalId: `workspace-edit-${randomUUID().replaceAll("-", "")}`,
      operation: "create",
      relativePath,
      beforeSha256: workspaceFileAbsentSha256,
      oldText: "",
      newText: content,
      patchHunks: Object.freeze([]),
    });
    this.#pending = Object.freeze({
      proposal,
      parentDevice: creation.parentDevice,
      parentInode: creation.parentInode,
    });
    return proposal;
  }

  async proposeChangeSet(
    { changes },
    signal,
  ) {
    throwIfAborted(signal);
    if (this.#pending !== null) {
      throw new WorkspacePolicyError(
        "workspace-edit-review-pending",
        "Review the pending workspace proposal before proposing another change.",
      );
    }
    const admittedChanges = validateChangeSetInput(changes);
    const relativePaths = new Set();
    const publicChanges = [];
    const pendingChanges = [];
    for (const change of admittedChanges) {
      throwIfAborted(signal);
      if (change.operation === "create") {
        const creation = await assertWorkspaceCreationPath(
          this.#admission,
          change.path,
        );
        const relativePath = reviewRelativePath(
          this.#admission,
          creation.safePath,
        );
        if (!addUniqueWorkspacePath(relativePaths, relativePath)) {
          throw new WorkspacePolicyError(
            "workspace-change-set-path-repeated",
            "Every change-set file path must be unique.",
          );
        }
        const publicChange = Object.freeze({
          ordinal: change.ordinal,
          operation: "create",
          relativePath,
          beforeSha256: workspaceFileAbsentSha256,
          oldText: "",
          newText: change.newText,
          patchHunks: Object.freeze([]),
        });
        publicChanges.push(publicChange);
        pendingChanges.push(Object.freeze({
          proposal: publicChange,
          expectedAfterSha256: sha256(change.newText),
          parentDevice: creation.parentDevice,
          parentInode: creation.parentInode,
        }));
        continue;
      }

      const file = await readWorkspaceTextFile(
        this.#admission,
        change.path,
      );
      const relativePath = reviewRelativePath(
        this.#admission,
        file.safePath,
      );
      if (!addUniqueWorkspacePath(relativePaths, relativePath)) {
        throw new WorkspacePolicyError(
          "workspace-change-set-path-repeated",
          "Every change-set file path must be unique.",
        );
      }
      let updated;
      if (change.operation === "patch") {
        updated = applyPatchHunks(
          file.content,
          change.patchHunks,
        );
      } else {
        if (countOccurrences(file.content, change.oldText) !== 1) {
          throw new WorkspacePolicyError(
            "workspace-edit-match-not-unique",
            "Every change-set oldText must occur exactly once in its current file.",
          );
        }
        updated = file.content.replace(
          change.oldText,
          change.newText,
        );
      }
      if (
        Buffer.byteLength(updated, "utf8") >
          maximumWorkspaceEditFileBytes
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-result-too-large",
          "A proposed change-set file would exceed one MiB.",
        );
      }
      const publicChange = Object.freeze({
        ordinal: change.ordinal,
        operation: change.operation,
        relativePath,
        beforeSha256: sha256(file.content),
        oldText: change.oldText,
        newText: change.newText,
        patchHunks: change.patchHunks,
      });
      publicChanges.push(publicChange);
      pendingChanges.push(Object.freeze({
        proposal: publicChange,
        expectedAfterSha256: sha256(updated),
      }));
    }
    throwIfAborted(signal);
    const frozenChanges = Object.freeze(publicChanges);
    const proposal = Object.freeze({
      schemaVersion: 4,
      proposalId: `workspace-edit-${randomUUID().replaceAll("-", "")}`,
      operation: "change-set",
      beforeSha256: changeSetReviewDigest(frozenChanges),
      changes: frozenChanges,
    });
    this.#pending = Object.freeze({
      proposal,
      changes: Object.freeze(pendingChanges),
      expectedAfterSha256: changeSetAfterDigest(
        pendingChanges.map(change => ({
          ordinal: change.proposal.ordinal,
          relativePath: change.proposal.relativePath,
          afterSha256: change.expectedAfterSha256,
        })),
      ),
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

    if (proposal.operation === "change-set") {
      return this.#commitChangeSet(proposal, pending);
    }
    if (proposal.operation === "create") {
      return this.#commitCreate(proposal, pending);
    }
    return this.#commitExisting(proposal, pending);
  }

  async #commitExisting(proposal, pending) {

    let temporaryPath;
    try {
      const first = await readWorkspaceTextFile(
        this.#admission,
        proposal.relativePath,
      );
      if (
        sha256(first.content) !== proposal.beforeSha256
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-drifted",
          "The file changed after proposal review began; the one-shot approval was not applied.",
        );
      }
      let updated;
      try {
        updated = proposal.operation === "patch"
          ? applyPatchHunks(first.content, proposal.patchHunks)
          : first.content.replace(
              proposal.oldText,
              proposal.newText,
            );
      } catch (error) {
        if (
          error?.code === "workspace-patch-match-not-unique" ||
          error?.code === "workspace-patch-overlap"
        ) {
          throw new WorkspacePolicyError(
            "workspace-edit-drifted",
            "The patch no longer matches the reviewed file; the one-shot approval was not applied.",
          );
        }
        throw error;
      }
      if (
        Buffer.byteLength(updated, "utf8") >
          maximumWorkspaceEditFileBytes
      ) {
        throw new WorkspacePolicyError(
          "workspace-edit-result-too-large",
          "The approved file would exceed one MiB.",
        );
      }
      if (sha256(updated) !== pending.expectedAfterSha256) {
        throw new WorkspacePolicyError(
          "workspace-edit-commit-verification-failed",
          "The approved replacement set did not reproduce the exact reviewed result.",
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
        sha256(final.content) !== proposal.beforeSha256
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
        schemaVersion: 3,
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
        schemaVersion: 3,
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

  async #commitChangeSet(proposal, pending) {
    const transactionId =
      `workspace-transaction-${randomUUID().replaceAll("-", "")}`;
    const journal = {
      schemaVersion: 1,
      receiptType: "jarvis2-workspace-change-set-transaction",
      transactionId,
      proposalId: proposal.proposalId,
      reviewDigest: proposal.beforeSha256,
      expectedAfterDigest: pending.expectedAfterSha256,
      workspaceDevice: this.#admission.device,
      workspaceInode: this.#admission.inode,
      phase: "preparing",
      files: [],
    };
    let journalWritten = false;
    try {
      for (const pendingChange of pending.changes) {
        const change = pendingChange.proposal;
        if (change.operation === "create") {
          let creation;
          try {
            creation = await assertWorkspaceCreationPath(
              this.#admission,
              change.relativePath,
            );
          } catch (error) {
            if (error?.code === "workspace-file-already-exists") {
              throw new WorkspacePolicyError(
                "workspace-change-set-drifted",
                "A change-set creation target appeared before commit; no transaction write began.",
              );
            }
            throw error;
          }
          if (
            creation.parentDevice !== pendingChange.parentDevice ||
            creation.parentInode !== pendingChange.parentInode
          ) {
            throw new WorkspacePolicyError(
              "workspace-change-set-drifted",
              "A change-set creation parent changed before commit; no transaction write began.",
            );
          }
          journal.files.push({
            ordinal: change.ordinal,
            operation: "create",
            relativePath: change.relativePath,
            beforeSha256: change.beforeSha256,
            afterSha256: pendingChange.expectedAfterSha256,
            mode: 0o600,
            targetDevice: null,
            targetInode: null,
            parentDevice: pendingChange.parentDevice,
            parentInode: pendingChange.parentInode,
            stagedRelativePath: transactionArtifactRelativePath(
              change.relativePath,
              proposal.proposalId,
              change.ordinal,
              "after",
            ),
            stagedDevice: null,
            stagedInode: null,
            backupRelativePath: null,
            backupDevice: null,
            backupInode: null,
          });
          continue;
        }

        const current = await readWorkspaceTextFile(
          this.#admission,
          change.relativePath,
        );
        if (sha256(current.content) !== change.beforeSha256) {
          throw new WorkspacePolicyError(
            "workspace-change-set-drifted",
            "A change-set file changed before commit; no transaction write began.",
          );
        }
        let updated;
        try {
          updated = change.operation === "patch"
            ? applyPatchHunks(current.content, change.patchHunks)
            : current.content.replace(
                change.oldText,
                change.newText,
              );
        } catch (error) {
          if (
            error?.code === "workspace-patch-match-not-unique" ||
            error?.code === "workspace-patch-overlap"
          ) {
            throw new WorkspacePolicyError(
              "workspace-change-set-drifted",
              "A change-set patch no longer matches its reviewed file; no transaction write began.",
            );
          }
          throw error;
        }
        if (sha256(updated) !== pendingChange.expectedAfterSha256) {
          throw new WorkspacePolicyError(
            "workspace-change-set-verification-failed",
            "A change-set member did not reproduce its exact reviewed result.",
          );
        }
        const parent = await lstat(dirname(current.safePath));
        journal.files.push({
          ordinal: change.ordinal,
          operation: change.operation,
          relativePath: change.relativePath,
          beforeSha256: change.beforeSha256,
          afterSha256: pendingChange.expectedAfterSha256,
          mode: current.stats.mode,
          targetDevice: String(current.stats.dev),
          targetInode: String(current.stats.ino),
          parentDevice: String(parent.dev),
          parentInode: String(parent.ino),
          stagedRelativePath: transactionArtifactRelativePath(
            change.relativePath,
            proposal.proposalId,
            change.ordinal,
            "after",
          ),
          stagedDevice: null,
          stagedInode: null,
          backupRelativePath: transactionArtifactRelativePath(
            change.relativePath,
            proposal.proposalId,
            change.ordinal,
            "before",
          ),
          backupDevice: null,
          backupInode: null,
        });
      }

      await writeInitialTransactionJournal(
        this.#admission,
        journal,
      );
      journalWritten = true;

      for (const entry of journal.files) {
        const pendingChange = pending.changes[entry.ordinal - 1];
        const change = pendingChange.proposal;
        let updated;
        if (change.operation === "create") {
          updated = change.newText;
        } else {
          const current = await readWorkspaceTextFile(
            this.#admission,
            change.relativePath,
          );
          if (sha256(current.content) !== entry.beforeSha256) {
            throw new WorkspacePolicyError(
              "workspace-change-set-drifted",
              "A change-set file drifted while transaction artifacts were being prepared.",
            );
          }
          updated = change.operation === "patch"
            ? applyPatchHunks(current.content, change.patchHunks)
            : current.content.replace(
                change.oldText,
                change.newText,
              );
          const backupIdentity =
            await writeFlushedTransactionFile(
              transactionAbsolutePath(
                this.#admission,
                entry.backupRelativePath,
              ),
              current.content,
              entry.mode,
            );
          entry.backupDevice = backupIdentity.device;
          entry.backupInode = backupIdentity.inode;
        }
        const stagedIdentity = await writeFlushedTransactionFile(
          transactionAbsolutePath(
            this.#admission,
            entry.stagedRelativePath,
          ),
          updated,
          entry.mode,
        );
        entry.stagedDevice = stagedIdentity.device;
        entry.stagedInode = stagedIdentity.inode;
      }
      journal.phase = "staged";
      await replaceTransactionJournal(this.#admission, journal);

      for (const entry of journal.files) {
        if (entry.operation === "create") {
          let creation;
          try {
            creation = await assertWorkspaceCreationPath(
              this.#admission,
              entry.relativePath,
            );
          } catch (error) {
            if (error?.code === "workspace-file-already-exists") {
              throw new WorkspacePolicyError(
                "workspace-change-set-drifted",
                "A change-set creation target appeared immediately before commit.",
              );
            }
            throw error;
          }
          if (
            creation.parentDevice !== entry.parentDevice ||
            creation.parentInode !== entry.parentInode
          ) {
            throw new WorkspacePolicyError(
              "workspace-change-set-drifted",
              "A change-set creation parent changed immediately before commit.",
            );
          }
          continue;
        }
        const current = await readWorkspaceTextFile(
          this.#admission,
          entry.relativePath,
        );
        const parent = await lstat(dirname(current.safePath));
        if (
          sha256(current.content) !== entry.beforeSha256 ||
          String(current.stats.dev) !== entry.targetDevice ||
          String(current.stats.ino) !== entry.targetInode ||
          String(parent.dev) !== entry.parentDevice ||
          String(parent.ino) !== entry.parentInode
        ) {
          throw new WorkspacePolicyError(
            "workspace-change-set-drifted",
            "A change-set file or parent changed immediately before commit.",
          );
        }
      }

      journal.phase = "committing";
      await replaceTransactionJournal(this.#admission, journal);
      for (const entry of journal.files) {
        const targetPath = transactionAbsolutePath(
          this.#admission,
          entry.relativePath,
        );
        const stagedPath = transactionAbsolutePath(
          this.#admission,
          entry.stagedRelativePath,
        );
        if (entry.operation === "create") {
          try {
            await link(stagedPath, targetPath);
          } catch (error) {
            if (error?.code === "EEXIST") {
              throw new WorkspacePolicyError(
                "workspace-change-set-drifted",
                "A change-set creation target appeared during exclusive commit.",
              );
            }
            throw error;
          }
          await rm(stagedPath);
        } else {
          await rename(stagedPath, targetPath);
        }
        if (
          typeof this.#transactionHooks?.afterFileApplied ===
            "function"
        ) {
          await this.#transactionHooks.afterFileApplied(
            entry.ordinal,
            journal.files.length,
          );
        }
      }

      for (const entry of journal.files) {
        const committed = await readWorkspaceTextFile(
          this.#admission,
          entry.relativePath,
        );
        if (
          sha256(committed.content) !== entry.afterSha256 ||
          String(committed.stats.dev) !== entry.stagedDevice ||
          String(committed.stats.ino) !== entry.stagedInode
        ) {
          throw new WorkspacePolicyError(
            "workspace-change-set-verification-failed",
            "A committed change-set file did not match its staged identity and after hash.",
          );
        }
      }
      journal.phase = "committed";
      await replaceTransactionJournal(this.#admission, journal);
      if (
        typeof this.#transactionHooks?.afterCommitted ===
          "function"
      ) {
        await this.#transactionHooks.afterCommitted(
          journal.files.length,
        );
      }
      await this.#cleanupChangeSetArtifacts(journal);
      await rm(transactionJournalPath(this.#admission));
      return this.#changeSetReceipt(proposal, pending);
    } catch (error) {
      if (!journalWritten) {
        throw error;
      }
      if (error instanceof WorkspaceTransactionCrashForTest) {
        throw error;
      }
      try {
        const recovery = await this.#recoverChangeSetJournal(journal);
        if (recovery === "completed") {
          return this.#changeSetReceipt(proposal, pending);
        }
      } catch (recoveryError) {
        throw new WorkspacePolicyError(
          "workspace-change-set-recovery-required",
          "The multi-file transaction stopped with durable recovery evidence that did not admit automatic convergence.",
          { cause: recoveryError },
        );
      }
      throw new WorkspacePolicyError(
        "workspace-change-set-rolled-back",
        "The multi-file transaction failed and every member was restored to its exact before state.",
        { cause: error },
      );
    }
  }

  #changeSetReceipt(proposal, pending) {
    return Object.freeze({
      schemaVersion: 4,
      proposalId: proposal.proposalId,
      operation: "change-set",
      beforeSha256: proposal.beforeSha256,
      afterSha256: pending.expectedAfterSha256,
      status: "applied",
      mutationPerformed: true,
      transactionModel:
        "durable-before-or-after-convergence-no-simultaneous-visibility-claim",
      files: Object.freeze(pending.changes.map(change =>
        Object.freeze({
          ordinal: change.proposal.ordinal,
          operation: change.proposal.operation,
          relativePath: change.proposal.relativePath,
          beforeSha256: change.proposal.beforeSha256,
          afterSha256: change.expectedAfterSha256,
        }))),
    });
  }

  async #cleanupChangeSetArtifacts(journal) {
    for (const entry of journal.files) {
      for (const relativePath of [
        entry.stagedRelativePath,
        entry.backupRelativePath,
      ]) {
        if (relativePath === null) {
          continue;
        }
        const artifactPath = transactionAbsolutePath(
          this.#admission,
          relativePath,
        );
        if (await pathExists(artifactPath)) {
          const expectedHash = relativePath ===
              entry.stagedRelativePath
            ? entry.afterSha256
            : entry.beforeSha256;
          const artifact = await readTransactionTextFile(
            artifactPath,
          );
          if (sha256(artifact.content) !== expectedHash) {
            throw new WorkspacePolicyError(
              "workspace-transaction-artifact-drifted",
              "A transaction artifact changed before cleanup.",
            );
          }
          await rm(artifactPath);
        }
      }
    }
    const names = await readdir(this.#admission.canonicalRoot);
    const prefix =
      `${workspaceTransactionArtifactPrefix}${journal.proposalId}-`;
    for (const name of names.filter(candidate =>
      candidate.startsWith(prefix) && candidate.endsWith(".tmp"))) {
      const orphan = join(this.#admission.canonicalRoot, name);
      if (await pathExists(orphan)) {
        const artifact = await readTransactionTextFile(
          orphan,
          65_536,
        );
        if (!artifact.content.includes(journal.proposalId)) {
          throw new WorkspacePolicyError(
            "workspace-transaction-artifact-drifted",
            "A transaction journal update artifact failed ownership validation.",
          );
        }
        await rm(orphan);
      }
    }
  }

  async #recoverChangeSetJournal(journal) {
    if (journal.phase === "committed") {
      for (const entry of journal.files) {
        const target = await readWorkspaceTextFile(
          this.#admission,
          entry.relativePath,
        );
        if (
          sha256(target.content) !== entry.afterSha256 ||
          String(target.stats.dev) !== entry.stagedDevice ||
          String(target.stats.ino) !== entry.stagedInode
        ) {
          throw new WorkspacePolicyError(
            "workspace-transaction-committed-state-drifted",
            "A committed transaction target no longer matches its durable after receipt.",
          );
        }
      }
      await this.#cleanupChangeSetArtifacts(journal);
      await rm(transactionJournalPath(this.#admission));
      return "completed";
    }

    for (const entry of [...journal.files].reverse()) {
      const targetPath = transactionAbsolutePath(
        this.#admission,
        entry.relativePath,
      );
      if (entry.operation === "create") {
        if (await pathExists(targetPath)) {
          const target = await readTransactionTextFile(targetPath);
          if (
            sha256(target.content) !== entry.afterSha256 ||
            String(target.stats.dev) !== entry.stagedDevice ||
            String(target.stats.ino) !== entry.stagedInode
          ) {
            throw new WorkspacePolicyError(
              "workspace-transaction-target-drifted",
              "A created transaction target could not be proven safe to roll back.",
            );
          }
          const parent = await lstat(dirname(targetPath));
          if (
            String(parent.dev) !== entry.parentDevice ||
            String(parent.ino) !== entry.parentInode
          ) {
            throw new WorkspacePolicyError(
              "workspace-transaction-parent-drifted",
              "A transaction parent changed before rollback.",
            );
          }
          await rm(targetPath);
        }
        continue;
      }

      const target = await readWorkspaceTextFile(
        this.#admission,
        entry.relativePath,
      );
      const targetHash = sha256(target.content);
      if (targetHash === entry.afterSha256) {
        const backupPath = transactionAbsolutePath(
          this.#admission,
          entry.backupRelativePath,
        );
        if (!(await pathExists(backupPath))) {
          throw new WorkspacePolicyError(
            "workspace-transaction-backup-missing",
            "A transaction backup required for rollback was missing.",
          );
        }
        const backup = await readTransactionTextFile(backupPath);
        if (
          sha256(backup.content) !== entry.beforeSha256 ||
          (entry.backupDevice !== null &&
            String(backup.stats.dev) !== entry.backupDevice) ||
          (entry.backupInode !== null &&
            String(backup.stats.ino) !== entry.backupInode)
        ) {
          throw new WorkspacePolicyError(
            "workspace-transaction-backup-drifted",
            "A transaction backup failed exact rollback admission.",
          );
        }
        await rename(backupPath, targetPath);
      } else if (targetHash !== entry.beforeSha256) {
        throw new WorkspacePolicyError(
          "workspace-transaction-target-drifted",
          "A transaction target matched neither its before nor after receipt.",
        );
      }
    }

    for (const entry of journal.files) {
      if (entry.operation === "create") {
        if (await pathExists(transactionAbsolutePath(
          this.#admission,
          entry.relativePath,
        ))) {
          throw new WorkspacePolicyError(
            "workspace-transaction-rollback-verification-failed",
            "A created transaction member remained after rollback.",
          );
        }
      } else {
        const restored = await readWorkspaceTextFile(
          this.#admission,
          entry.relativePath,
        );
        if (sha256(restored.content) !== entry.beforeSha256) {
          throw new WorkspacePolicyError(
            "workspace-transaction-rollback-verification-failed",
            "An existing transaction member did not return to its before hash.",
          );
        }
      }
    }
    await this.#cleanupChangeSetArtifacts(journal);
    await rm(transactionJournalPath(this.#admission));
    return "rolled-back";
  }

  async #recoverPersistedChangeSet() {
    const journalPath = transactionJournalPath(this.#admission);
    if (!(await pathExists(journalPath))) {
      return;
    }
    let journal;
    try {
      const stored = await readTransactionTextFile(
        journalPath,
        65_536,
      );
      journal = parseTransactionJournal(
        stored.content,
        this.#admission,
      );
      const result = await this.#recoverChangeSetJournal(journal);
      this.#recoveryReceipt = Object.freeze({
        schemaVersion: 1,
        receiptType: "jarvis2-workspace-change-set-recovery",
        result,
        proposalId: journal.proposalId,
        fileCount: journal.files.length,
        mutationPerformed: result === "rolled-back",
      });
    } catch (error) {
      throw new WorkspacePolicyError(
        "workspace-change-set-recovery-required",
        "A durable multi-file transaction journal failed strict automatic recovery admission.",
        { cause: error },
      );
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
    if (proposal.operation === "change-set") {
      return Object.freeze({
        schemaVersion: 4,
        proposalId,
        operation: "change-set",
        beforeSha256: proposal.beforeSha256,
        afterSha256: null,
        status: "rejected",
        mutationPerformed: false,
        transactionModel:
          "durable-before-or-after-convergence-no-simultaneous-visibility-claim",
        files: Object.freeze(proposal.changes.map(change =>
          Object.freeze({
            ordinal: change.ordinal,
            operation: change.operation,
            relativePath: change.relativePath,
            beforeSha256: change.beforeSha256,
            afterSha256: null,
          }))),
      });
    }
    return Object.freeze({
      schemaVersion: 3,
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

export class WorkspaceTransactionCrashForTest extends Error {
  constructor() {
    super("Simulated process interruption after one transaction member.");
    this.name = "WorkspaceTransactionCrashForTest";
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

export function createWorkspacePatchProposalTool(
  admission,
  proposalManager,
) {
  return {
    name: "propose_patch",
    label: "Propose patch",
    description:
      "Stage 2-8 exact, non-overlapping replacements in one existing UTF-8 workspace file for explicit desktop-owner review. This tool never writes the file.",
    promptSnippet:
      "propose_patch: stage one existing-file multi-hunk patch for owner review (no write)",
    promptGuidelines: [
      "Use propose_patch only after reading the target file and only when 2-8 coherent replacements in that one file are required.",
      "Every oldText must be distinct, occur exactly once, and not overlap another replacement.",
      "A proposal pauses new turns until the desktop owner approves or rejects it; approval is never available to the model.",
    ],
    parameters: Type.Object(
      {
        path: Type.String({
          description:
            "Existing UTF-8 file path inside the admitted workspace",
          minLength: 1,
          maxLength: maximumWorkspaceEditRelativePathCharacters,
        }),
        replacements: Type.Array(
          Type.Object(
            {
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
          {
            minItems: minimumWorkspacePatchHunks,
            maxItems: maximumWorkspacePatchHunks,
          },
        ),
      },
      { additionalProperties: false },
    ),
    executionMode: "sequential",
    async execute(
      _toolCallId,
      args,
      signal,
    ) {
      const proposal = await proposalManager.proposePatch(
        args,
        signal,
      );
      return {
        content: [{
          type: "text",
          text:
            `Workspace patch ${proposal.proposalId} with ${proposal.patchHunks.length} hunks is staged for desktop-owner review. No file was changed.`,
        }],
        details: {
          workspaceEditProposal: proposal,
        },
      };
    },
  };
}

export function createWorkspaceChangeSetProposalTool(
  admission,
  proposalManager,
) {
  const replacement = Type.Object(
    {
      operation: Type.Literal("replace"),
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
  );
  const patch = Type.Object(
    {
      operation: Type.Literal("patch"),
      path: Type.String({
        description:
          "Existing UTF-8 file path inside the admitted workspace",
        minLength: 1,
        maxLength: maximumWorkspaceEditRelativePathCharacters,
      }),
      replacements: Type.Array(
        Type.Object(
          {
            oldText: Type.String({
              minLength: 1,
              maxLength: maximumWorkspaceEditSegmentBytes,
            }),
            newText: Type.String({
              maxLength: maximumWorkspaceEditSegmentBytes,
            }),
          },
          { additionalProperties: false },
        ),
        {
          minItems: minimumWorkspacePatchHunks,
          maxItems: maximumWorkspacePatchHunks,
        },
      ),
    },
    { additionalProperties: false },
  );
  const creation = Type.Object(
    {
      operation: Type.Literal("create"),
      path: Type.String({
        description:
          "Missing UTF-8 file path with an existing workspace parent",
        minLength: 1,
        maxLength: maximumWorkspaceEditRelativePathCharacters,
      }),
      content: Type.String({
        description: "Complete proposed UTF-8 file content",
        minLength: 1,
        maxLength: maximumWorkspaceCreateFileBytes,
      }),
    },
    { additionalProperties: false },
  );
  return {
    name: "propose_change_set",
    label: "Propose change set",
    description:
      "Stage one owner-reviewed transaction spanning 2-4 unique UTF-8 workspace files. The tool never writes; one desktop decision applies or rejects the whole set.",
    promptSnippet:
      "propose_change_set: stage one 2-4 file transaction for owner review (no write)",
    promptGuidelines: [
      "Use propose_change_set only after reading every existing target and only when one coherent task truly spans multiple files.",
      "The complete file set is one review digest and one owner decision; do not assume partial approval.",
      "Failure or a pre-commit crash converges to every before state; a completed durable commit converges to every after state.",
      "Approval and transaction recovery are desktop-owned and unavailable to the model.",
    ],
    parameters: Type.Object(
      {
        changes: Type.Array(
          Type.Union([replacement, patch, creation]),
          {
            minItems: minimumWorkspaceChangeSetFiles,
            maxItems: maximumWorkspaceChangeSetFiles,
          },
        ),
      },
      { additionalProperties: false },
    ),
    executionMode: "sequential",
    async execute(
      _toolCallId,
      args,
      signal,
    ) {
      const proposal = await proposalManager.proposeChangeSet(
        args,
        signal,
      );
      return {
        content: [{
          type: "text",
          text:
            `Workspace change set ${proposal.proposalId} with ${proposal.changes.length} files is staged for desktop-owner review. No file was changed.`,
        }],
        details: {
          workspaceEditProposal: proposal,
        },
      };
    },
  };
}

function extractWorkspaceChangeSetProposal(proposal) {
  if (
    !hasExactKeys(
      proposal,
      [
        "schemaVersion",
        "proposalId",
        "operation",
        "beforeSha256",
        "changes",
      ],
    ) ||
    proposal.schemaVersion !== 4 ||
    typeof proposal.proposalId !== "string" ||
    !proposalIdPattern.test(proposal.proposalId) ||
    proposal.operation !== "change-set" ||
    typeof proposal.beforeSha256 !== "string" ||
    !sha256Pattern.test(proposal.beforeSha256) ||
    !Array.isArray(proposal.changes) ||
    proposal.changes.length < minimumWorkspaceChangeSetFiles ||
    proposal.changes.length > maximumWorkspaceChangeSetFiles
  ) {
    return null;
  }
  const paths = new Set();
  let previewBytes = 0;
  for (const [index, change] of proposal.changes.entries()) {
    if (
      !hasExactKeys(
        change,
        [
          "ordinal",
          "operation",
          "relativePath",
          "beforeSha256",
          "oldText",
          "newText",
          "patchHunks",
        ],
      ) ||
      change.ordinal !== index + 1 ||
      !["replace", "patch", "create"].includes(change.operation) ||
      typeof change.relativePath !== "string" ||
      change.relativePath.length === 0 ||
      change.relativePath.length >
        maximumWorkspaceEditRelativePathCharacters ||
      change.relativePath.includes("\\") ||
      change.relativePath.includes(":") ||
      change.relativePath.includes("//") ||
      change.relativePath.startsWith("/") ||
      change.relativePath.endsWith("/") ||
      change.relativePath.split("/").some(segment =>
        segment === "." ||
        segment === ".." ||
        [".git", ".hg", ".svn"].includes(segment.toLowerCase()) ||
        segment.toLowerCase() === workspaceTransactionJournalName ||
        segment.toLowerCase().startsWith(
          workspaceTransactionArtifactPrefix,
        )) ||
      /[\u0000-\u001f\u007f]/u.test(change.relativePath) ||
      !addUniqueWorkspacePath(paths, change.relativePath) ||
      typeof change.beforeSha256 !== "string" ||
      !sha256Pattern.test(change.beforeSha256) ||
      !isStrictUtf8Text(change.oldText) ||
      !isStrictUtf8Text(change.newText) ||
      !Array.isArray(change.patchHunks)
    ) {
      return null;
    }
    if (change.operation === "replace") {
      if (
        change.oldText.length === 0 ||
        Buffer.byteLength(change.oldText, "utf8") >
          maximumWorkspaceEditSegmentBytes ||
        Buffer.byteLength(change.newText, "utf8") >
          maximumWorkspaceEditSegmentBytes ||
        change.oldText === change.newText ||
        change.patchHunks.length !== 0
      ) {
        return null;
      }
      previewBytes +=
        Buffer.byteLength(change.oldText, "utf8") +
        Buffer.byteLength(change.newText, "utf8");
      continue;
    }
    if (change.operation === "create") {
      if (
        change.beforeSha256 !== workspaceFileAbsentSha256 ||
        change.oldText.length !== 0 ||
        Buffer.byteLength(change.newText, "utf8") < 1 ||
        Buffer.byteLength(change.newText, "utf8") >
          maximumWorkspaceCreateFileBytes ||
        containsBinaryControlCharacters(change.newText) ||
        change.patchHunks.length !== 0
      ) {
        return null;
      }
      previewBytes += Buffer.byteLength(change.newText, "utf8");
      continue;
    }
    const oldTexts = new Set();
    if (
      change.oldText.length !== 0 ||
      change.newText.length !== 0 ||
      change.patchHunks.length < minimumWorkspacePatchHunks ||
      change.patchHunks.length > maximumWorkspacePatchHunks
    ) {
      return null;
    }
    for (const [hunkIndex, hunk] of change.patchHunks.entries()) {
      if (
        hunk?.ordinal !== hunkIndex + 1 ||
        !isStrictUtf8Text(hunk.oldText) ||
        hunk.oldText.length === 0 ||
        Buffer.byteLength(hunk.oldText, "utf8") >
          maximumWorkspaceEditSegmentBytes ||
        !isStrictUtf8Text(hunk.newText) ||
        Buffer.byteLength(hunk.newText, "utf8") >
          maximumWorkspaceEditSegmentBytes ||
        containsBinaryControlCharacters(hunk.oldText) ||
        containsBinaryControlCharacters(hunk.newText) ||
        hunk.oldText === hunk.newText ||
        oldTexts.has(hunk.oldText)
      ) {
        return null;
      }
      oldTexts.add(hunk.oldText);
      previewBytes +=
        Buffer.byteLength(hunk.oldText, "utf8") +
        Buffer.byteLength(hunk.newText, "utf8");
    }
  }
  if (
    previewBytes > maximumWorkspaceChangeSetPreviewBytes ||
    changeSetReviewDigest(proposal.changes) !==
      proposal.beforeSha256
  ) {
    return null;
  }
  return proposal;
}

export function extractWorkspaceEditProposal(result) {
  const proposal = result?.details?.workspaceEditProposal;
  if (proposal?.schemaVersion === 4) {
    return extractWorkspaceChangeSetProposal(proposal);
  }
  if (
    proposal?.schemaVersion !== 3 ||
    typeof proposal.proposalId !== "string" ||
    !proposalIdPattern.test(proposal.proposalId) ||
    !["replace", "create", "patch"].includes(
      proposal.operation,
    ) ||
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
    !Array.isArray(proposal.patchHunks) ||
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
    proposal.newText !== proposal.oldText &&
    proposal.patchHunks.length === 0;
  const createValid =
    proposal.operation === "create" &&
    proposal.beforeSha256 === workspaceFileAbsentSha256 &&
    proposal.oldText.length === 0 &&
    Buffer.byteLength(proposal.newText, "utf8") > 0 &&
    Buffer.byteLength(proposal.newText, "utf8") <=
      maximumWorkspaceCreateFileBytes &&
    proposal.patchHunks.length === 0 &&
    !/[\u0001-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(
      proposal.newText,
    );
  const patchOldTexts = new Set();
  let patchPreviewBytes = 0;
  const patchHunksValid = proposal.patchHunks.every(
    (hunk, index) => {
      if (
        hunk?.ordinal !== index + 1 ||
        !isStrictUtf8Text(hunk.oldText) ||
        hunk.oldText.length === 0 ||
        Buffer.byteLength(hunk.oldText, "utf8") >
          maximumWorkspaceEditSegmentBytes ||
        !isStrictUtf8Text(hunk.newText) ||
        Buffer.byteLength(hunk.newText, "utf8") >
          maximumWorkspaceEditSegmentBytes ||
        containsBinaryControlCharacters(hunk.oldText) ||
        containsBinaryControlCharacters(hunk.newText) ||
        hunk.newText === hunk.oldText ||
        patchOldTexts.has(hunk.oldText)
      ) {
        return false;
      }
      patchOldTexts.add(hunk.oldText);
      patchPreviewBytes +=
        Buffer.byteLength(hunk.oldText, "utf8") +
        Buffer.byteLength(hunk.newText, "utf8");
      return true;
    },
  );
  const patchValid =
    proposal.operation === "patch" &&
    proposal.oldText.length === 0 &&
    proposal.newText.length === 0 &&
    proposal.patchHunks.length >= minimumWorkspacePatchHunks &&
    proposal.patchHunks.length <= maximumWorkspacePatchHunks &&
    patchHunksValid &&
    patchPreviewBytes <= maximumWorkspacePatchPreviewBytes;
  if (!replaceValid && !createValid && !patchValid) {
    return null;
  }
  return proposal;
}
