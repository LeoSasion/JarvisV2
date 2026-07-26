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
