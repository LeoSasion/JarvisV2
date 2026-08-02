# Pi Agent workspace edit approval

JarvisV2 can stage one exact workspace text edit and present it to the desktop
owner inside the conversation that produced it. Pi cannot approve the proposal,
and calling `propose_edit` never writes a file.

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
```

Only the human-operated desktop control can send `commit_workspace_edit` or
`discard_workspace_edit`. Neither request is exposed as a Pi tool or model
provider function.

## Admitted proposal

The first implementation accepts only:

- one pending proposal per session;
- one existing single-link regular file inside the admitted canonical workspace;
- strictly valid UTF-8 text without NUL bytes;
- a file no larger than 1 MiB;
- a non-empty `oldText` of at most 4,096 UTF-8 bytes;
- a distinct `newText` of at most 4,096 UTF-8 bytes;
- exactly one occurrence of `oldText` in the current file, counting overlapping
  matches;
- a workspace-relative review path no longer than 512 characters.

It does not accept new files, deletes, renames, binary files, multiple matches,
links, junctions, aliases, paths outside the workspace, protected roots, shell
commands, registry operations, Explorer operations, or system files.

The proposal object contains:

- schema version;
- random session-scoped proposal id;
- normalized workspace-relative path;
- SHA-256 of the complete file before the proposed edit;
- exact old text;
- exact replacement text.

The proposal is held only in the live sidecar session. It is not restored from
the encrypted conversation checkpoint. Shutdown clears any undecided proposal
without writing and renders its desktop state as
`Expired on Shutdown / No Write`. If an owner decision has already entered the
sidecar, shutdown waits for its terminal receipt before stopping the sidecar,
so the desktop cannot report an ambiguous half-decision.

## One-shot decision

Approval is a capability tied to both the random proposal id and the exact
lowercase before SHA-256. A mismatched id or hash is rejected without consuming
the valid proposal. Once an exact approval begins, the proposal is consumed
before file validation or writing. Therefore drift, commit failure, success,
and replay all require a fresh proposal.

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

If the current content or identity changes, the result is `Drifted / No Write`
and the owner must request a fresh proposal. If the desktop cannot establish
whether a decision completed, the conversation fails closed and stops accepting
new work until the session is restarted.

## Conversation behavior

The structured `workspace_edit_proposed` event is admitted only after a
successful `propose_edit` tool completion. It is folded into the same immutable,
revisioned conversation turn as the request, tool lifecycle, and assistant
response.

While a proposal is pending:

- the sidecar rejects `start_turn`;
- the desktop snapshot reports `CanSubmit=false`;
- the transcript shows path, exact before/after text, before hash, decision
  status, and the two owner controls;
- Reject appears before Approve Once in keyboard order;
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
- invalid UTF-8 and overlapping matches fail closed.

`PiAgentDesktopRuntimeProbe` additionally proves the complete
provider-to-Pi-to-JSONL-to-managed-state path, including inline proposal state,
submission blocking, approved fixture commit, replay rejection, drift handling,
explicit rejection, shutdown expiration, eight broker requests, and zero broker
faults. The fixture is created under the admitted JarvisV2 workspace and removed
after the probe.

These tests do not contact a live model, clear the kill switch, activate a shell
module, inject into Explorer, restart Explorer, modify the registry, or mutate a
production workspace file.

## Still unavailable

This milestone does not grant:

- `bash`, generic `edit`, or `write` tools;
- unattended or model-triggered approval;
- multi-file transactions;
- create, delete, rename, or binary mutation;
- persistent pending capabilities;
- self-authored approval policy;
- Shell, Explorer, registry, service, device, or system mutation.

The desktop-reviewed iteration layer is documented in
`PI-AGENT-REVIEWED-ITERATION.md`. It adds durable receipts, a fixed repository
test gate and an explicit owner policy without changing this one-shot decision
boundary. It does not restore pending proposals after restart and does not let
Pi operate either owner control.
