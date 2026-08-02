# Pi Agent reviewed multi-file transaction

Status: **IMPLEMENTED AND DIAGNOSTICALLY VERIFIED**

## Purpose

Real self-iteration usually changes a coherent source, test and documentation
set. The desktop now admits that coherent unit through one structured
`propose_change_set` capability without
giving Pi a generic writer, process runner, approval control or recovery
control.

## Admitted proposal

One model turn may stage exactly one change set containing two to four unique
workspace-relative UTF-8 text files. Each file is one of:

- an exact replacement in an existing file;
- a two-to-eight-hunk exact patch in an existing file; or
- an exclusive new file whose parent directory already exists.

Because the host is Windows, path uniqueness is case-insensitive at the
sidecar, managed bridge, durable receipt and trusted-validation boundaries.

Existing files remain limited to one MiB. Every review segment remains limited
to 4 KiB, every new file to 16 KiB, and the complete cross-file review payload
to 32 KiB. Paths containing aliases, reparse points, VCS metadata or reserved
transaction names fail admission. Delete, rename, directory creation, binary
mutation and VCS mutation remain unavailable.

The proposal performs no write. It carries one random proposal capability, one
canonical review digest over the ordered file set, complete per-file before
hashes and complete review text. Only the native desktop owner may consume that
capability once.

## Transaction guarantee

Windows does not provide one linearizable rename spanning unrelated paths. The
product therefore promises durable convergence, not simultaneous visibility:

- a successful decision leaves every admitted path at its exact reviewed
  after hash;
- a rejected decision writes nothing;
- a detected drift writes nothing;
- an in-process failure rolls every touched path back to its exact before
  state;
- a crash before the durable committed phase is recovered to the complete
  before state when the next session starts;
- a crash after the durable committed phase completes cleanup while retaining
  the complete after state.

No Pi tool becomes available until startup recovery finishes. Any target,
backup, staged file, parent identity, hash or journal mismatch fails session
admission without guessing or performing a partial recovery.

## Durable state machine

The owner-approved sidecar uses one reserved root journal and unique same-
directory artifacts:

```text
owner approves exact proposal id + review digest
        |
        +-- revalidate every target / parent / before hash
        +-- journal: preparing
        +-- create and flush exact before backups + after stages
        +-- journal: staged
        +-- revalidate the complete set again
        +-- journal: committing
        +-- replace/link each target, recording no model-visible authority
        +-- verify every after hash
        +-- journal: committed
        +-- remove backups/stages, then remove journal
```

The journal contains identities, relative paths and hashes, never model
credentials or owner approval authority. The reserved names cannot be proposed
as workspace changes.

## Desktop review surface

The proposal remains inside its producing transcript turn. One amber header
names `MULTI-FILE CHANGE SET`, the exact file count, review digest and recovery
guarantee. Each file receives an ordered near-square sub-plane containing its
operation, path, before/after state and complete review segments. The transcript
owns scrolling; no file is hidden behind a nested review scroller.

`REJECT ALL` precedes `APPLY CHANGE SET ONCE` in keyboard order. Both operate on
the entire set. There are no per-file approval toggles because they would make
the reviewed digest and transaction guarantee ambiguous.

## Reviewed iteration integration

One approved change set consumes one of the four owner decisions, regardless of
file count. The non-executing Git gate requires its changed path set to equal
the union of every approved transaction file and requires every current hash to
match its durable per-file receipt. The separate pinned trusted-test approval
remains mandatory. A change set that includes a pinned test file is rejected
before Node starts, exactly like a single-file edit.

## Proof obligations

The executable diagnostics must prove:

- proposal-only behavior and exact cross-file review digest;
- duplicate/path/size/VCS/reserved-name rejection;
- one-shot whole-set rejection with zero writes;
- whole-set apply for replace, patch and create members;
- drift in any member prevents every write;
- injected mid-commit failure restores every before state;
- startup recovery chooses rollback before `committed` and cleanup after it;
- tampered journals or artifacts fail session admission without mutation;
- decision replay rejection and shutdown expiration;
- reviewed iteration records every file and admits no next turn before the
  normal repository and trusted-validation gates pass;
- Pi still has no shell, generic writer, decision or recovery capability.
