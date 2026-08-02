# Pi Agent reviewed iteration

JarvisV2 now has a desktop-owned workflow that can carry one owner mission
across several Pi reasoning turns without granting unattended writes. The
workflow reuses the `propose_edit` / `propose_patch` /
`propose_create_file` / `propose_change_set` one-shot owner boundary: Pi can
stage one exact replacement, one 2–8-hunk exact patch in a single existing
UTF-8 file, one missing UTF-8 file, or one ordered two-to-four-file set, but only
the human-operated WPF control can consume that proposal.

This milestone is reviewed self-iteration with a separately owner-approved,
baseline-pinned test runner, not autonomous shell execution. It does not add a
sidecar tool, a generic command runner, a persistent proposal or any approval
or process path callable by the model.

## Fixed owner policy

The initial policy is deliberately not configurable by Pi:

- the workspace must be the exact root of a clean Git repository;
- the current Git HEAD is pinned for the full workflow;
- the mission is the text currently present in the desktop composer;
- at most four applied owner approvals are admitted;
- the policy expires six hours after it is armed;
- only one Pi turn and one workspace write proposal may be active;
- continuation requires both an owner-approved write and a separate
  owner-approved pass of the pinned trusted test profile;
- the owner can stop the workflow at any time.

The owner action `START REVIEWED LOOP` creates the policy. A stopped, completed,
expired or faulted workflow is terminal. A new workflow again requires a clean
HEAD, so accumulated edits must be reviewed and resolved outside the loop before
another policy can start.

## Turn and decision sequence

```text
owner mission
    |
    +-- START REVIEWED LOOP
            |
            +-- clean Git HEAD baseline + DPAPI policy receipt
                    |
                    +-- Pi reasoning turn
                            |
                            +-- no proposal -> complete without write
                            |
                            +-- one single-file or 2-4-file proposal -> pause
                                    |
                                    +-- REJECT -> no write, stop
                                    |
                                    +-- one owner approval for the exact proposal digest
                                            |
                                            +-- exact one-shot sidecar commit
                                            +-- fixed repository gate
                                                    |
                                                    +-- fail -> stop closed
                                                    +-- pass -> pause again
                                                            |
                                                            +-- RUN PINNED TESTS ONCE
                                                                    |
                                                                    +-- direct Node, no shell
                                                                    +-- fixed pre/post repository gates
                                                                            |
                                                                            +-- fail/drift -> stop closed
                                                                            +-- pass + limit/expiry -> complete
                                                                            +-- pass -> next Pi reasoning turn
```

The next turn is submitted only by the desktop coordinator after the write
decision, repository receipt, separate test approval, test receipt and post-run
repository receipt are terminal. Pi never receives
`commit_workspace_edit`, `discard_workspace_edit`, policy mutation or loop
control as a tool.

## Durable receipts

`PiAgentReviewedIterationStore` writes one file per workflow below:

```text
%LOCALAPPDATA%\JARVIS2\PiAgent\reviewed-iterations\<workspace-id>\
```

The payload is protected with Windows CurrentUser DPAPI and entropy derived from
the canonical workspace root. The outer envelope binds schema, workspace id,
workflow id, revision and save time. Saves use a same-directory temporary file,
write-through flush and atomic replacement. Reparse points, copied envelopes,
unknown fields, invalid identifiers, oversized payloads and inconsistent step
counts fail closed.

Schema 3 records the producing turn, proposal id, top-level review/after digest,
one ordered receipt for every approved file (operation, path, before and after
SHA-256), owner decision, repository result, repository digest, trusted
validation result, exit code, output/receipt digests, error code and UTC
timestamps. A single-file step carries one file receipt; a change set carries
two to four. Older active schema-1/2 policies fail closed on open and restore no
capability. A proposal itself remains session-memory-only.

## Non-executing repository gate

`PiAgentReviewedIterationRepositoryGate` starts only an exact `git.exe` process
with `UseShellExecute=false`; it does not invoke `cmd`, PowerShell or a workspace
script. Policy admission requires:

The portable Control Center resolves its bundled
`runtime\git\cmd\git.exe` first. Developer builds may fall back to a standard
Git for Windows installation or an exact `git.exe` on `PATH`; every path still
uses the same direct, no-shell process boundary.

1. the admitted workspace equals `git rev-parse --show-toplevel`;
2. `HEAD` is a valid Git object id;
3. porcelain status is empty;
4. `git diff --check` passes.

After an approved write, the gate requires:

1. the repository root and pinned HEAD are unchanged;
2. every status entry is either one unstaged tracked-file modification or one
   untracked new file; staged, ignored, deleted, renamed and conflicted paths
   fail closed;
3. the changed path set exactly equals all files approved by this workflow;
4. every current file SHA-256 equals its latest approved after hash;
5. every approved file is at most 1 MiB, strictly valid UTF-8 and contains no
   NUL bytes;
6. `git diff --check` passes for tracked files and fixed
   `git diff --no-index --check -- NUL <path>` passes for every untracked file;
7. changed JSON parses with comments and trailing commas disabled;
8. changed XML, XAML, project, props and targets files parse with DTD processing
   prohibited and no resolver.

This is a compiled repository-integrity and structured-text gate. It does not
execute builds, test projects, MSBuild targets or repository-authored code.

## Separately approved trusted validation

`config/pi-agent-trusted-validation.json` is loaded from the pinned clean Git
HEAD, never from the newly edited worktree. The strict schema admits one
`node-test` profile, a 5-120 second timeout and one to eight unique relative
`.mjs` test paths that already exist in the baseline commit. Unknown fields,
absolute paths, traversal, VCS metadata and duplicate paths fail admission.
Every named test file is also protected from reviewed-iteration edits: if its
path appears in the accumulated approved change set, validation fails closed
before Node starts. This keeps the displayed pinned-test boundary literal.

After the non-executing gate passes, the coordinator enters
`AwaitingTrustedValidation`; it does not start another Pi turn. The owner sees
the exact normalized command and may choose `RUN PINNED TESTS ONCE`. Only then
does the desktop:

1. rerun the full repository gate against all durable approved hashes;
2. start the desktop-admitted absolute `node.exe` directly with
   `UseShellExecute=false` and fixed `--test` arguments;
3. replace the inherited environment with a small OS allowlist plus explicit
   offline/CI markers;
4. enforce the profile timeout, a 262,144-character output boundary and
   owned-process-tree termination on timeout or failure;
5. hash output and the full execution receipt without persisting raw output;
6. rerun the repository gate after the process exits.

Pi has no tool or IPC operation for the process, profile or owner control. A
zero exit code alone is insufficient: repository drift after the tests also
faults the workflow. The initial JarvisV2 profile runs the existing protocol,
desktop broker and workspace-edit approval Node tests.

## Restart and shutdown

Shutdown first stores the policy as `Interrupted`, clears its current turn and
proposal identities, then the normal runtime quiesce expires any sidecar-memory
proposal and flushes the conversation checkpoint. Opening the same workspace
never restores a proposal capability.

`RE-ARM` is available only for an interrupted, unexpired policy. It reruns the
full fixed repository gate against the pinned HEAD and durable after hashes and
recaptures the test profile from that HEAD. A mismatch changes the workflow to
`Faulted`. If shutdown interrupted a pending test decision, re-arm restores only
the `AwaitingTrustedValidation` state; the owner must approve a fresh one-shot
run. Otherwise a pass starts one fresh bounded Pi turn. No proposal id or
process authority is reused.

## Validation evidence

`PiAgentReviewedIterationProbe` creates an isolated nested Git fixture and
proves:

- clean-baseline admission;
- CurrentUser-DPAPI ciphertext and durable round trip;
- first new-file proposal pause, one-shot exclusive creation and passed
  repository gate;
- rejection of trailing whitespace in an untracked file;
- pause after the repository gate with no process started;
- separate owner authorization of the baseline-pinned Node test profile;
- rejection of a modified trusted-test path before any process starts;
- trusted test pass plus an exact post-run repository receipt before the next
  reasoning turn;
- active trusted-validation cancellation terminates the owned Node process tree
  before its delayed completion marker can be written;
- second proposal pause, explicit whole-set approval of one replace, one
  two-hunk patch and one exclusive create across three files, schema-3 durable
  per-file receipts and a passed exact-path-set repository gate;
- automatic next reasoning turn followed by a third proposal and explicit
  rejection;
- shutdown suspension and absence of restored proposal authority;
- explicit re-arm after repository revalidation;
- repository drift rejection;
- ten broker requests, zero broker faults, no live Explorer contact and no
  production workspace mutation.

The native surface evidence is
`docs/screenshots/jarvis-control-center-trusted-validation.png`; it is an
illustrative no-runtime preview of the inspector command disclosure and
separate test-once action. The multi-file owner decision is captured in
`docs/screenshots/jarvis-control-center-reviewed-change-set.png`; the earlier
single-file two-hunk decision remains in
`docs/screenshots/jarvis-control-center-reviewed-multi-hunk-patch.png`.

## Still unavailable

- shell, PowerShell, command-prompt, generic commands or any process execution
  by Pi;
- model-selected test files, arguments, runner, environment or timeout;
- unattended or model-triggered approval;
- policy authored or extended by Pi;
- more than four approved edits or more than six hours per policy;
- simultaneous cross-path atomic visibility, delete, rename, directory
  creation, VCS metadata or binary mutation;
- restoring or replaying a pending proposal after restart;
- Git commit, push, merge or branch mutation from the reviewed loop;
- Explorer, registry, service, device or system mutation.
