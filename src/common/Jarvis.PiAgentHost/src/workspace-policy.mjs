import {
  lstat,
  realpath,
} from "node:fs/promises";
import {
  isAbsolute,
  join,
  parse,
  relative,
  resolve,
  sep,
} from "node:path";

export class WorkspacePolicyError extends Error {
  constructor(code, message) {
    super(message);
    this.name = "WorkspacePolicyError";
    this.code = code;
  }
}

function normalizedPath(value) {
  const normalized = resolve(value);
  return process.platform === "win32"
    ? normalized.toLowerCase()
    : normalized;
}

function isWithin(root, candidate) {
  const relativePath = relative(root, candidate);
  return (
    relativePath === "" ||
    (!relativePath.startsWith(`..${sep}`) &&
      relativePath !== ".." &&
      !isAbsolute(relativePath))
  );
}

function rejectsWindowsPathShape(value) {
  if (process.platform !== "win32") {
    return false;
  }
  if (
    value.startsWith("\\\\") ||
    value.startsWith("//") ||
    /^[\\/]{2}[?.][\\/]/u.test(value)
  ) {
    return true;
  }
  const resolved = resolve(value);
  return resolved.slice(2).includes(":");
}

function protectedRoots() {
  const windowsVolume =
    typeof process.env.SystemRoot === "string"
      ? parse(process.env.SystemRoot).root
      : undefined;
  const candidates = [
    process.env.SystemRoot,
    process.env.WINDIR,
    process.env.ProgramFiles,
    process.env["ProgramFiles(x86)"],
    process.env.ProgramData,
    process.env.USERPROFILE,
    process.env.APPDATA,
    process.env.LOCALAPPDATA,
    windowsVolume
      ? join(windowsVolume, "Program Files")
      : undefined,
    windowsVolume
      ? join(windowsVolume, "Program Files (x86)")
      : undefined,
    windowsVolume
      ? join(windowsVolume, "ProgramData")
      : undefined,
    windowsVolume
      ? join(windowsVolume, "Users")
      : undefined,
  ];
  return candidates
    .filter((value) => typeof value === "string" && value.length > 0)
    .map((value) => resolve(value));
}

function exactProtectedRoots() {
  return new Set(
    protectedRoots().map(normalizedPath),
  );
}

function isInsideProtectedTree(candidate) {
  const profileRoot =
    typeof process.env.USERPROFILE === "string"
      ? normalizedPath(process.env.USERPROFILE)
      : null;
  return protectedRoots().some((protectedRoot) => {
    if (
      profileRoot !== null &&
      normalizedPath(protectedRoot) === profileRoot
    ) {
      return false;
    }
    return isWithin(protectedRoot, candidate);
  });
}

async function rejectReparseComponents(root, candidate) {
  const relativePath = relative(root, candidate);
  let current = root;
  const segments =
    relativePath === "" ? [] : relativePath.split(sep);
  const paths = [root];
  for (const segment of segments) {
    current = join(current, segment);
    paths.push(current);
  }
  for (const component of paths) {
    const stats = await lstat(component);
    if (stats.isSymbolicLink()) {
      throw new WorkspacePolicyError(
        "reparse-point-forbidden",
        "Workspace paths may not traverse a symbolic link or junction.",
      );
    }
  }
}

export async function admitWorkspaceRoot(workspaceRoot) {
  if (
    typeof workspaceRoot !== "string" ||
    workspaceRoot.trim() !== workspaceRoot ||
    workspaceRoot.length === 0 ||
    !isAbsolute(workspaceRoot) ||
    rejectsWindowsPathShape(workspaceRoot)
  ) {
    throw new WorkspacePolicyError(
      "invalid-workspace-root",
      "workspaceRoot must be a conventional absolute local path.",
    );
  }

  const resolvedRoot = resolve(workspaceRoot);
  const volumeRoot = parse(resolvedRoot).root;
  if (
    normalizedPath(resolvedRoot) === normalizedPath(volumeRoot) ||
    exactProtectedRoots().has(normalizedPath(resolvedRoot)) ||
    isInsideProtectedTree(resolvedRoot)
  ) {
    throw new WorkspacePolicyError(
      "protected-workspace-root",
      "Drive, profile-root, application-data, program, and operating-system locations cannot be admitted.",
    );
  }

  let stats;
  let canonicalRoot;
  try {
    stats = await lstat(resolvedRoot);
    canonicalRoot = await realpath(resolvedRoot);
  } catch {
    throw new WorkspacePolicyError(
      "workspace-root-not-found",
      "workspaceRoot must name an existing directory.",
    );
  }
  if (
    stats.isSymbolicLink() ||
    normalizedPath(canonicalRoot) !== normalizedPath(resolvedRoot)
  ) {
    throw new WorkspacePolicyError(
      "workspace-root-alias-forbidden",
      "workspaceRoot must be the canonical path and cannot be a link or junction.",
    );
  }
  if (!stats.isDirectory()) {
    throw new WorkspacePolicyError(
      "workspace-root-not-directory",
      "workspaceRoot must name a directory.",
    );
  }
  await rejectReparseComponents(volumeRoot, resolvedRoot);

  return Object.freeze({
    canonicalRoot: resolvedRoot,
    device: String(stats.dev),
    inode: String(stats.ino),
  });
}

async function assertWorkspaceIdentity(admission) {
  const stats = await lstat(admission.canonicalRoot);
  const canonicalRoot = await realpath(admission.canonicalRoot);
  if (
    String(stats.dev) !== admission.device ||
    String(stats.ino) !== admission.inode ||
    normalizedPath(canonicalRoot) !==
      normalizedPath(admission.canonicalRoot) ||
    stats.isSymbolicLink()
  ) {
    throw new WorkspacePolicyError(
      "workspace-identity-changed",
      "The admitted workspace root changed after session creation.",
    );
  }
}

export async function assertWorkspacePath(
  admission,
  requestedPath,
) {
  if (
    typeof requestedPath !== "string" ||
    requestedPath.length === 0 ||
    rejectsWindowsPathShape(requestedPath)
  ) {
    throw new WorkspacePolicyError(
      "invalid-workspace-path",
      "Tool paths must use a conventional local path.",
    );
  }
  await assertWorkspaceIdentity(admission);

  const candidate = resolve(
    admission.canonicalRoot,
    requestedPath,
  );
  if (!isWithin(admission.canonicalRoot, candidate)) {
    throw new WorkspacePolicyError(
      "path-outside-workspace",
      "The requested path is outside the admitted workspace.",
    );
  }

  let canonicalCandidate;
  try {
    await rejectReparseComponents(
      admission.canonicalRoot,
      candidate,
    );
    canonicalCandidate = await realpath(candidate);
  } catch (error) {
    if (error instanceof WorkspacePolicyError) {
      throw error;
    }
    throw new WorkspacePolicyError(
      "workspace-path-not-found",
      "The requested workspace path does not exist.",
    );
  }
  if (
    !isWithin(admission.canonicalRoot, canonicalCandidate) ||
    normalizedPath(canonicalCandidate) !== normalizedPath(candidate)
  ) {
    throw new WorkspacePolicyError(
      "path-alias-forbidden",
      "Tool paths cannot traverse links, junctions, or aliases.",
    );
  }
  return candidate;
}
