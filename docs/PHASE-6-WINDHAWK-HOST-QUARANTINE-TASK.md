# Phase 6 — Windhawk host quarantine and Explorer-only host task

Status: **ACTIVE — LIVE ACTIVATION QUARANTINED**

This task supersedes the Phase 5 live-activation handoff. It does not authorize
starting Windhawk, clearing the kill switch, loading M2 or restarting Explorer.

## Controlled-session finding

On 2026-07-27, the plan-bound `StartDisabledHost` action started the Windhawk
service while M2 remained disabled, the kill switch remained armed and the
one-shot permit remained absent. The action itself passed with:

- Explorer PID 11640;
- Windhawk Running / Manual / service PID 37448;
- M2 `Disabled=1`;
- target M2 mapping count 0.

The immediate read-only inventory then found the reviewed Windhawk 1.7.3 base
runtime mapped into Explorer and many unrelated processes, including terminal,
browser, ChatGPT and application hosts. No Jarvis DLL was mapped and M2 was
never enabled, but the base-runtime spread violates the repository prohibition
on global injection and broad process targeting.

The controlled session was aborted before `clear-kill-switch`. `Recover`
disabled M2 again, normally stopped the service, preserved Manual start mode
and kept Explorer PID 11640 stable. Its elevated receipt reported no recovery
errors, zero target M2 mappings and two non-Jarvis base-runtime residuals:
ChatGPT PID 4024 and `wslservice` PID 5936. Neither process was terminated.

Evidence:

- start receipt:
  `artifacts/m2-controlled-live/runs/phase5-start-disabled-host-20260727-132334.json`;
- recovery receipt:
  `artifacts/m2-controlled-live/runs/phase5-emergency-recover-broad-runtime-20260727-132923.json`;
- recovery result:
  `passed-locked-runtime-residual-recorded`;
- Windhawk base DLL:
  `C:\Program Files\Windhawk\Engine\1.7.3\64\windhawk.dll`;
- reviewed base DLL SHA-256:
  `0AAD074CAF156200BE7A77E4615F9171CEA884CDE96BAF90397366C28C4F10A1`.

Artifacts remain local and excluded from publication. This document records
the bounded facts needed to explain the quarantine without publishing the
user's complete process inventory.

## Immediate quarantine

- [x] Re-arm/retain `%LOCALAPPDATA%\JARVIS2\disabled.flag`.
- [x] Confirm `active-module.txt` is absent.
- [x] Keep M2 disabled.
- [x] Normally stop Windhawk and preserve Manual start mode.
- [x] Confirm Explorer PID is stable and M2 was never mapped.
- [x] Close the recovery terminal and verify its lease is blocked.
- [x] Make readiness return a fixed host-quarantine failure.
- [x] Remove the controller's reachable `Start-Service` and `Disabled=0`
  mutation paths.
- [x] Make Supervisor reject `clear-kill-switch` before state-gate
  acquisition.
- [x] Make the current receipt schema reject successful StartDisabledHost and
  EnableOnce claims.
- [x] Add static and executable regression coverage.
- [x] Commit a clean, reproducible quarantine baseline.

## Replacement-host research

The next host must target only the verified desktop Explorer PID and must not
install a persistent global injector.

- [ ] Trace Windhawk's official service/injector architecture from upstream
  source and document why a disabled mod still exposes the base runtime
  broadly.
- [ ] Evaluate a dedicated Explorer-only launcher with explicit PID, session,
  image-path, signer/hash and compatibility checks.
- [ ] Evaluate supported Windows mechanisms before any remote-thread or loader
  implementation.
- [ ] Document loader lifetime, module ownership, rollback, crash-loop
  prevention and Windows-update invalidation in an ADR.
- [ ] Build only an offline/mockable launcher skeleton first.
- [ ] Prove every test path remains incapable of targeting `dwm.exe`,
  arbitrary processes or a name-wide Explorer set.

## Acceptance boundary

Phase 6 cannot return to a live approval gate until all of the following are
true:

1. no Windhawk service or global base-runtime injection is required;
2. exactly one already verified desktop Explorer PID is the only possible
   target;
3. the launcher has no unattended restart, retry or process-name broadcast;
4. the kill switch and one-shot permit are checked before any native load;
5. a failed or lost recovery channel leaves the host locked;
6. fixed-toolchain build identity and Windows compatibility gates remain
   exact;
7. a new current-task approval reviews the replacement host and exact command.

Until then, `StartDisabledHost`, `EnableOnce` and `clear-kill-switch` are
quarantined regardless of older plans or approvals.
