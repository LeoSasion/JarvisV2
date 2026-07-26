# Phase 5 safety review

Review scope: recovery-terminal lease, Supervisor activation boundary,
`jarvis-taskbar-icon-size` runtime fail-closed behavior, offline fixtures and
the controlled-live handoff.

Review date: 2026-07-27 (Asia/Shanghai)

Live activation during review: **not performed**

## Findings

### P0 — Recovery heartbeat conflicted with the native state-root watcher

The first Phase 5 implementation atomically replaced
`%LOCALAPPDATA%\JARVIS2\m2-recovery-terminal.json` once per second. M2 watches
the JARVIS2 state root non-recursively for any direct file-name mutation and
latches pass-through on the first event. The heartbeat temporary-file creation
and rename would therefore quiesce the module immediately after activation and
make a live result meaningless.

Resolution:

- the lease moved to
  `%LOCALAPPDATA%\JARVIS2\Recovery\m2-recovery-terminal.json`;
- temporary heartbeat files are also created inside `Recovery`;
- the state-root watcher remains non-recursive and keeps treating any direct
  root file-name change as an emergency;
- the offline lab now performs three atomic child-directory heartbeat
  replacements while watching the parent non-recursively and requires zero
  parent file-name events.

Status: **resolved and covered by an executable regression**.

### P1 — Terminal loss after activation was not bounded in the native module

The first lease gate verified terminal liveness twice before deleting
`disabled.flag`, but a terminal could still be killed immediately after the
second check. The hook would then remain active until a human or external
observer armed the kill switch.

Resolution:

- M2 now requires a fresh recovery heartbeat before starting its watcher;
- the watcher polls the lease file once per second;
- missing, reparse-point, future-dated beyond two seconds, or older-than-six-
  seconds heartbeat state permanently latches the hook into pass-through;
- no process restart, Explorer restart, service mutation or automatic module
  reload is performed.

The remaining check/delete/load race is bounded: if the terminal disappears
after Supervisor's last check but before or after module load, the native
watchdog switches to pass-through no later than the six-second freshness
window. This is a bounded pass-through guarantee, not physical DLL unload.

Status: **resolved with a documented bounded residual window**.

### P1 — Lease and fixture paths followed reparse points

The first managed validator constrained the plan path and plan sources but read
the lease before applying an equivalent reparse-point boundary.

Resolution:

- the production lease must be the exact file below the JARVIS2 `Recovery`
  directory;
- the read-only `--lease-path` override is restricted to
  `artifacts/m2-recovery-lease-lab/runs`;
- the root and every existing path component must not be a reparse point.

Status: **resolved**.

### P1 — UTC plan timestamps were parsed as local time by PowerShell

The first controlled handoff correctly refused to open the recovery terminal,
but for the wrong reason. On this host PowerShell's `ConvertFrom-Json` first
materialized a `Z` timestamp as a UTC `DateTime`; casting that value back to
`string` discarded its `Kind`. The following parse then treated the unchanged
clock value as local time. In UTC+08:00 that shifted a new plan eight hours
backwards, so a fresh 30-minute plan appeared expired immediately.

Resolution:

- recovery-terminal plan expiry, process-start and heartbeat fields now use a
  type-aware converter: `DateTime` and `DateTimeOffset` retain their UTC
  identity, while strings must carry an explicit `Z` or numeric offset;
- the locked observation rehearsal uses the same converter;
- the project gate requires both controllers to use `DateTimeOffset` for these
  external string timestamps, exercises the `ConvertFrom-Json` coercion path
  and rejects the old string-cast `DateTime.Parse` form.

The refusal was fail-closed: no recovery terminal, permit, Windhawk process or
module mapping was created during the failed attempt.

Status: **resolved and covered by a static plus semantic regression**.

### P1 — Elevated readiness exposed a hidden non-Jarvis base mapping

The standard-token probe could not enumerate the modules of `wslservice.exe`,
so it reported zero global matches. The administrator probe correctly exposed
one mapping in PID 5936:
`C:\Program Files\Windhawk\Engine\1.7.3\64\windhawk.dll`. The target Explorer
PID had no matching module, `allJarvisMappings` was empty, Windhawk remained
Stopped / Manual / PID 0, and the only configured mod was the disabled,
Explorer-only M2. Stopping or terminating WSL merely to update two disabled M2
files would broaden the operation and violate the no-force recovery boundary.

Resolution:

- the receipt continues to count every module-enumeration exception;
- it separately counts safety-relevant errors for the verified Explorer PID
  and named Windhawk/Jarvis processes versus unrelated non-target errors;
- readiness and the controller require zero Jarvis mappings, zero Explorer
  mappings and zero unexpected Windhawk mappings;
- a non-Explorer residual is accepted only when its module name, exact path,
  version, size and SHA-256 match the reviewed Windhawk 1.7.3 base DLL and its
  host is not a Windhawk/Jarvis process;
- the disabled installer requires the accepted residual set to be identical
  before and after its atomic file update and verified rollback window;
- no WSL stop, process termination, Explorer restart or force-unload path is
  introduced.

Both observed failures were fail-closed and reported
`mutationPerformed=false`.

Status: **strict source fix applied; elevated read-only regression pending**.

### P1 — Fixed toolchain still emitted volatile PE timestamps

A canonical rebuild after the readiness fix produced the same M2 source size
and semantics but a different DLL SHA-256. Binary comparison found only two
copies of the link timestamp changed: the PE/COFF header and debug-directory
timestamp. Accepting a new hash after every rebuild would make the live
controller's exact DLL allowlist operationally unstable.

Resolution:

- both native modules pass the locked MinGW/LLD linker
  `--no-insert-timestamp` option through Windhawk metadata;
- the project gate requires the option for both sources;
- two independent canonical builds must produce the same M2 DLL SHA-256 before
  that hash is admitted to the live controller.

The two proof runs produced the exact same M2 DLL SHA-256,
`C2DB007E2FDCDA145463E2D0355BD4F7E18ACC9CE414D77652EED33DD5532865`.
M1 still differed by 22 non-timestamp bytes between those runs. M1 remains
build-only and outside this live-validation allowlist; its remaining
reproducibility work cannot be used to broaden the M2 session.

Status: **M2 resolved with a two-run reproducibility proof; M1 remains
build-only**.

### P2 — Offline gates are intentionally serialized

Two simultaneous `Test-Project.ps1` executions can compete for the shared
locked portable toolchain cache. One parallel review attempt failed closed;
the same gate passed when rerun sequentially. This is expected lock behavior,
not a native build failure.

Resolution:

- release/runbook verification remains explicitly sequential;
- a lock-contention result is not accepted as a passing receipt and must be
  rerun only after the competing process exits.

Status: **accepted operational constraint**.

### P1 — Live mutation tooling was outside the session-plan identity

The recovery lease originally bound the planner, readiness probe, recovery
terminal, locked observer, native source and Supervisor assembly, but no
repository controller represented the later service start, one-shot registry
enable or normal cleanup. A temporary operator script could therefore drift
after the plan was created without invalidating the lease.

Resolution:

- `Invoke-M2ControlledLiveValidation.ps1` is inert by default and separates
  `UpdateDisabledInstallation`, `StartDisabledHost`, `EnableOnce`, `Observe`
  and `Recover`;
- each mutating action requires its own exact confirmation switch;
- the disabled-installation action accepts only the reviewed old hashes,
  creates verified backups, atomically installs only the canonical new hashes
  and rolls back on failure while Windhawk is stopped and M2 is disabled;
- the controller never executes `clear-kill-switch`, never launches the
  Windhawk application, never changes service start mode, never terminates a
  process and never restarts Explorer;
- `EnableOnce` re-arms first, disables M2 and normally stops Windhawk on any
  failed preflight or load check;
- `Recover` deliberately remains usable after terminal or plan loss, but
  requires `disabled.flag` present and the permit absent before disabling M2
  and normally stopping the service;
- the plan schema, planner and Supervisor now bind the controller's exact path,
  size and SHA-256;
- every controller result is validated before publication against the
  action-specific `m2-controlled-live-controller-receipt.schema.json`, which
  fixes the non-activation claim and forbids Explorer restart, process
  termination and service start-mode mutation in every receipt; the receipt
  schema is bound into the same session plan.

Status: **resolved and covered by an executable static controller audit**.

## Preserved safety properties

- `disabled.flag` remains armed throughout review and offline fixes.
- `active-module.txt` remains absent.
- Windhawk remains Stopped / Manual and no module is loaded.
- M1 remains build-only and outside the live allowlist.
- The hook hot path still uses only atomics and performs no file I/O.
- Lease expiry only latches pass-through; it does not terminate Explorer,
  restart a process, or claim DLL unload.
- `--lease-path` is diagnostic-only; real activation always consumes the
  default production lease path.

## Required closure evidence

- managed Release build: 0 warnings / 0 errors;
- recovery lease lab: 7/7;
- canonical fixed-toolchain build for the reviewed M2 source;
- full project gate and publication boundary run sequentially;
- fresh read-only host receipt with 23/23 compatibility, kill switch armed,
  permit absent, Windhawk Stopped / Manual and zero matching mappings;
- a reviewed diff and one narrow commit before any live activation.

## Live boundary

This review does not authorize `clear-kill-switch`, Windhawk configuration,
service start, module load or Explorer restart. A new short-lived session plan,
visible recovery terminal and fresh lease inspection remain mandatory. The
user's explicit approval of the exact activation command plus loading only M2
also remains mandatory.
