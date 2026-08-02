# Pi Agent workspace write approval

JarvisV2 can stage one exact existing-text replacement, one bounded multi-hunk
patch to a single existing UTF-8 file, or one new UTF-8 file and present it to
the desktop owner inside the conversation that produced it. Pi cannot approve
the proposal, and calling `propose_edit`, `propose_patch`,
`propose_create_file` or `propose_change_set` never writes a file.

This is the one-shot mutation primitive used by both ordinary conversation and
the durable reviewed-iteration workflow. It is not general shell access, a
generic file editor, or unattended approval.

## Authority split

The authority boundary is intentionally asymmetric:

```text
Pi model
  |
  +-- read / grep / find / ls
  |
  +-- propose_edit(path, oldText, newText)
        |
        +-- validates and stages one proposal in sidecar memory
        +-- emits proposal id + relative path + before SHA-256 + exact text
        +-- performs no write
  |
  +-- propose_patch(path, replacements[2..8])
        |
        +-- requires distinct oldText values, each matched exactly once
        +-- rejects overlapping source ranges and over 16 KiB review text
        +-- stages every exact remove/add pair with a stable review ordinal
        +-- performs no write
  |
  +-- propose_create_file(path, content)
        |
        +-- requires a missing target under an existing canonical parent
        +-- stages the complete 1-16384 byte UTF-8 content
        +-- emits operation=create + absent-state SHA-256 sentinel
        +-- performs no write and creates no directory
              |
              v
Desktop conversation surface
  |
  +-- REJECT       -> consume proposal, no write
  |
  +-- APPROVE ONCE -> send exact proposal id + before SHA-256
                          |
                          +-- re-admit root/path/reparse/file
                          +-- re-read and re-hash immediately before commit
                          +-- require oldText to occur exactly once
                          +-- atomic same-directory replacement
                          +-- read back and verify after SHA-256
  |
  +-- APPLY PATCH ONCE -> send exact proposal id + before SHA-256
                          |
                          +-- re-admit root/path/reparse/file
                          +-- re-read and re-hash immediately before commit
                          +-- reconstruct every reviewed hunk from the original
                          +-- one same-directory atomic replacement
                          +-- read back and verify exact result/after SHA-256
  |
  +-- CREATE ONCE  -> send exact proposal id + absent-state SHA-256
                          |
                          +-- re-admit root and exact parent identity
                          +-- require target still absent
                          +-- create with exclusive no-overwrite access
                          +-- flush, re-read and verify exact content/hash
```

Only the human-operated desktop control can send `commit_workspace_edit` or
`discard_workspace_edit`. Neither request is exposed as a Pi tool or model
provider function.

## Admitted proposals

All operations share these limits:

- one pending proposal per session;
- strictly valid UTF-8 text without NUL bytes;
- a workspace-relative review path no longer than 512 characters;
- no `.git`, `.hg`, or `.svn` path segment;
- no Windows device-name or trailing-dot/space alias;
- no links, junctions, aliases, protected roots, or paths outside the workspace.

`replace` additionally requires one existing single-link regular file no larger
than 1 MiB, a non-empty `oldText` of at most 4,096 UTF-8 bytes, a distinct
`newText` of at most 4,096 bytes, and exactly one occurrence of `oldText`
including overlapping matches.

`patch` has the same existing-file, per-text and exact-match constraints. It
requires 2–8 hunks, distinct `oldText` values, exactly one occurrence of each
old text in the original file and no overlapping source ranges. The combined
UTF-8 byte length of every old and new text is at most 16,384 bytes. Hunk order
in the request does not grant positional authority: the sidecar resolves every
range against the same reviewed original file, rejects overlap, sorts those
ranges by original-file position and reconstructs one complete result. Binary
control characters other than tab, CR and LF are rejected so the bounded
review payload also remains inside the 64 KiB JSONL frame after escaping.

`create` additionally requires a missing target, an already-existing canonical
parent directory, and complete non-empty content of at most 16,384 UTF-8 bytes.
Binary control characters other than tab, CR and LF are rejected. It never
creates parent directories and never overwrites an existing path.

No operation accepts deletes, renames, binary files, shell commands,
registry operations, Explorer operations, or system files.

The proposal object contains:

- schema version;
- random session-scoped proposal id;
- explicit `replace`, `patch` or `create` operation;
- normalized workspace-relative path;
- SHA-256 of the complete file before replacement, or the fixed domain-separated
  absent-state SHA-256 for creation;
- operation-specific exact text: one old/new pair, the ordered patch-hunk
  array, or complete new-file content.

The proposal is held only in the live sidecar session. It is not restored from
the encrypted conversation checkpoint. Shutdown clears any undecided proposal
without writing and renders its desktop state as
`Expired on Shutdown / No Write`. If an owner decision has already entered the
sidecar, shutdown waits for its terminal receipt before stopping the sidecar,
so the desktop cannot report an ambiguous half-decision.

## One-shot decision

Approval is a capability tied to the random proposal id, explicit operation and
exact lowercase before-state SHA-256. A mismatched id or hash is rejected
without consuming the valid proposal. Once an exact approval begins, the
proposal is consumed before file validation or writing. Therefore drift, commit
failure, success, and replay all require a fresh proposal.

Immediately before replacement, the sidecar:

1. revalidates the admitted workspace identity;
2. revalidates every target path component against links and junctions;
3. requires the same existing regular text file identity;
4. re-reads the full file;
5. requires the full SHA-256 to match the proposal;
6. requires the exact old text to occur once;
7. writes and flushes a new temporary file in the same directory;
8. repeats the identity, hash, and unique-match checks;
9. atomically replaces the target;
10. reads the committed file back and returns its after SHA-256.

Immediately before a patch, the sidecar performs the same root, path, identity
and full-file SHA-256 checks. It then reconstructs the result solely from the
reviewed original plus the staged 2–8 hunk ranges, requires the reconstructed
SHA-256 to equal the proposal's expected after hash, writes and flushes one
same-directory temporary file, repeats the identity and before-hash checks,
atomically replaces the target once, and reads back the exact committed result.
No hunk is committed independently, so a failure before the rename leaves the
target unchanged.

Immediately before creation, the sidecar:

1. revalidates the admitted workspace identity;
2. revalidates the exact existing parent path and its device/inode identity;
3. requires the target to remain absent;
4. opens the target with exclusive create access (`wx`);
5. writes and flushes the complete reviewed UTF-8 content;
6. rechecks the parent and created file identity;
7. reads the file back and verifies exact content plus after SHA-256;
8. on failure, removes only the file created by this attempt and only while its
   identity and parent path remain safely admitted.

If the current content or identity changes, the result is `Drifted / No Write`
and the owner must request a fresh proposal. If the desktop cannot establish
whether a decision completed, the conversation fails closed and stops accepting
new work until the session is restarted.

## Conversation behavior

The structured `workspace_edit_proposed` event is admitted only after a
successful `propose_edit`, `propose_patch`, `propose_create_file` or
`propose_change_set` completion. A single-file schema-v3 payload carries the
explicit operation; schema v4 carries the exact ordered file set and review
digest. Both are folded into the same immutable,
revisioned conversation turn as the request, tool lifecycle, and assistant
response.

While a proposal is pending:

- the sidecar rejects `start_turn`;
- the desktop snapshot reports `CanSubmit=false`;
- the transcript shows operation, path, exact before/after state, before-state
  hash, decision status, and the two owner controls;
- Reject appears before Approve Once, Apply Patch Once or Create Once in
  keyboard order;
- both controls have explicit accessibility names;
- status is communicated with text in addition to amber, cyan, or coral.

Applied, rejected, expired, drifted, and failed decisions remain visible in the
retained turn. Pending proposal data is deliberately excluded from conversation
checkpoint restore.

## Validation

`test/workspace-edit-approval.test.mjs` uses an isolated workspace fixture to
prove:

- proposal creation performs no write;
- exact approval applies once;
- replay fails;
- content drift fails without overwriting external work;
- the exact mismatch capability fails closed;
- rejection performs no write;
- missing and ambiguous targets fail closed;
- invalid UTF-8 and overlapping matches fail closed;
- an approved two-hunk patch changes both exact ranges through one atomic
  replacement and replay fails;
- duplicate, ambiguous, overlapping or binary-control patch hunks fail closed
  without mutation;
- approved creation is exclusive and exact;
- a racing owner-created target is preserved;
- rejection creates no file;
- existing targets and missing parents fail closed.

`PiAgentDesktopRuntimeProbe` additionally proves the complete
provider-to-Pi-to-JSONL-to-managed-state path, including inline proposal state,
submission blocking, approved fixture commit, replay rejection, drift handling,
explicit rejection, shutdown expiration, and a mixed replace/patch/create
three-file change set with exact managed file receipts. The fixtures are
created under the admitted JarvisV2 workspace and removed after the probe.

`PiAgentReviewedIterationProbe` adds the desktop coordinator path: it approves
creation, approves a three-file change set containing a two-hunk patch, runs the
fixed repository gate after each, then rejects a third proposal. It requires ten
broker requests, zero broker faults and exact durable per-file after hashes.

These tests do not contact a live model, clear the kill switch, activate a shell
module, inject into Explorer, restart Explorer, modify the registry, or mutate a
production workspace file.

## Still unavailable

This milestone does not grant:

- `bash`, generic `edit`, or `write` tools;
- unattended or model-triggered approval;
- simultaneous cross-path atomic visibility (change sets instead guarantee
  durable all-before or all-committed-after convergence);
- delete, rename, directory creation, VCS metadata or binary mutation;
- persistent pending capabilities;
- self-authored approval policy;
- Shell, Explorer, registry, service, device, or system mutation.

The desktop-reviewed iteration layer is documented in
`PI-AGENT-REVIEWED-ITERATION.md`. The bounded multi-file extension is specified
in `PI-AGENT-MULTI-FILE-TRANSACTION.md`. It adds durable receipts, a fixed
repository test gate and an explicit owner policy without giving Pi either
owner control or recovery authority.
