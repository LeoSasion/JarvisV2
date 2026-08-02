# Pi Agent reviewed iteration

JarvisV2 now has a desktop-owned workflow that can carry one owner mission
across several Pi reasoning turns without granting unattended writes. The
workflow reuses the `propose_edit` / `propose_patch` / `propose_create_file`
and one-shot owner boundary: Pi can stage one exact replacement, one 2–8-hunk
exact patch in a single existing UTF-8 file, or one missing UTF-8 file, but only
the human-operated WPF control can consume that proposal.

This milestone is reviewed self-iteration, not autonomous shell execution. It
does not add a sidecar tool, a generic command runner, a persistent proposal or
an approval path callable by the model.

## Fixed owner policy

The initial policy is deliberately not configurable by Pi:

- the workspace must be the exact root of a clean Git repository;
- the current Git HEAD is pinned for the full workflow;
- the mission is the text currently present in the desktop composer;
- at most four applied owner approvals are admitted;
- the policy expires six hours after it is armed;
- only one Pi turn and one workspace write proposal may be active;
- automatic continuation means another reasoning turn, never automatic
  approval;
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
                            +-- one replace/patch/create proposal -> pause
                                    |
                                    +-- REJECT -> no write, stop
                                    |
                                    +-- APPROVE ONCE / APPLY PATCH ONCE / CREATE ONCE
                                            |
                                            +-- exact one-shot sidecar commit
                                            +-- fixed repository gate
                                                    |
                                                    +-- fail -> stop closed
                                                    +-- pass + limit/expiry -> complete
                                                    +-- pass -> next Pi reasoning turn
```

The next turn is submitted only by the desktop coordinator after both the owner
decision and validation receipt are terminal. Pi never receives
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

Each durable step records the producing turn, proposal id, path, before and
after SHA-256, owner decision, validation result, repository digest, error code
and UTC timestamp. A proposal itself remains session-memory-only.

## Repository test gate

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

This is a compiled repository-integrity and structured-text test gate. It does
not execute builds, test projects, MSBuild targets or repository-authored code.
A future executable test runner needs a separate trusted-command design and is
not implied by this milestone.

## Restart and shutdown

Shutdown first stores the policy as `Interrupted`, clears its current turn and
proposal identities, then the normal runtime quiesce expires any sidecar-memory
proposal and flushes the conversation checkpoint. Opening the same workspace
never restores a proposal capability.

`RE-ARM` is available only for an interrupted, unexpired policy. It reruns the
full fixed repository gate against the pinned HEAD and durable after hashes. A
mismatch changes the workflow to `Faulted`; a pass starts one fresh bounded Pi
turn. No prior proposal id is reused.

## Validation evidence

`PiAgentReviewedIterationProbe` creates an isolated nested Git fixture and
proves:

- clean-baseline admission;
- CurrentUser-DPAPI ciphertext and durable round trip;
- first new-file proposal pause, one-shot exclusive creation and passed
  repository gate;
- rejection of trailing whitespace in an untracked file;
- automatic next reasoning turn but no automatic approval;
- second proposal pause, explicit approval of a two-hunk patch in the created
  file and a passed repository gate;
- automatic next reasoning turn followed by a third proposal and explicit
  rejection;
- shutdown suspension and absence of restored proposal authority;
- explicit re-arm after repository revalidation;
- repository drift rejection;
- ten broker requests, zero broker faults, no live Explorer contact and no
  production workspace mutation.

The native surface evidence is
`docs/screenshots/jarvis-control-center-reviewed-multi-hunk-patch.png`; it is an
illustrative no-runtime preview of the explicit two-hunk patch and owner-only
decision state.

## Still unavailable

- shell, PowerShell, command-prompt or repository-script execution by Pi;
- unattended or model-triggered approval;
- policy authored or extended by Pi;
- more than four approved edits or more than six hours per policy;
- multi-file atomic transactions, delete, rename, directory creation, VCS
  metadata or binary mutation;
- restoring or replaying a pending proposal after restart;
- Git commit, push, merge or branch mutation from the reviewed loop;
- Explorer, registry, service, device or system mutation.
