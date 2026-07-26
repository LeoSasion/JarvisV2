# M2 controlled live-validation runbook

Status: **PREPARED — NOT AUTHORIZED TO ACTIVATE**

Target module: `jarvis-taskbar-icon-size`
Host: verified desktop `explorer.exe` only
Maximum scope: one module, one permit, one validation session

## What this runbook does not authorize

Preparing or passing this runbook does not authorize:

- clearing `%LOCALAPPDATA%\JARVIS2\disabled.flag`;
- creating or consuming `active-module.txt`;
- starting, configuring or enabling Windhawk;
- loading M2;
- restarting Explorer.

Broad authorization to develop a phase cannot replace the final, current-task
approval required by `AGENTS.md`.

## Offline readiness

Build the managed Supervisor and generate a non-overwriting read-only receipt:

```powershell
dotnet build .\src\Jarvis.Supervisor\Jarvis.Supervisor.csproj --configuration Release
pwsh -NoLogo -NoProfile -File .\scripts\Test-M2LiveReadiness.ps1 `
  -OutputPath .\artifacts\m2-live-readiness\runs\<unique-name>.json
```

A passing receipt means only `readyForExactApproval=true`. It must still say:

- `activationPermitted=false`;
- `liveExplorer=not-run`;
- `mutationPerformed=false`;
- `exactCommandApproved=false`;
- `recoveryTerminalAvailable=false`;
- `canExecuteNow=false`.

## Locked session rehearsal

Phase 4/5 can create a short-lived, source-bound plan and exercise the observer
without opening a terminal or changing the host:

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\New-M2ValidationSessionPlan.ps1 `
  -OutputPath .\artifacts\m2-validation-session-plans\runs\<unique-name>.json

pwsh -NoLogo -NoProfile -File .\scripts\Open-M2RecoveryTerminal.ps1 `
  -SessionPlanPath .\artifacts\m2-validation-session-plans\runs\<unique-name>.json

pwsh -NoLogo -NoProfile -File .\scripts\Test-M2ObservationRehearsal.ps1 `
  -SessionPlanPath .\artifacts\m2-validation-session-plans\runs\<unique-name>.json
```

The recovery-terminal command above is a dry run because it omits
`-ConfirmOpen`. It must report `launchPerformed=false`,
`terminalAvailable=false`, `mutationPerformed=false` and
`canExecuteNow=false`.

The observation rehearsal samples the verified locked Explorer and keeps the
real host snapshot separate from its in-memory fault-evaluation copy. The
supported simulated stop conditions are:

- `kill-switch-missing`;
- `permit-present`;
- `windhawk-running`;
- `explorer-changed`;
- `module-mapped`;
- `elevated-cpu`.

Fault injection never changes the service, permit, flag, process or module
mapping. A `stop-required` result is an offline detector rehearsal, not proof
that recovery ran.

Phase 5 also provides an offline, fixture-only recovery-lease lab:

```powershell
pwsh -NoLogo -NoProfile -File .\scripts\Test-M2RecoveryLeaseLab.ps1
```

It must pass all seven scenarios and report `stateDirectoryTouched=false`.
It never calls `clear-kill-switch`, writes a real lease, starts Windhawk or
touches Explorer.

The production heartbeat lives at
`%LOCALAPPDATA%\JARVIS2\Recovery\m2-recovery-terminal.json`. The child
directory is intentional: M2 keeps a non-recursive emergency watch on the
JARVIS2 state root and separately polls the lease once per second. A heartbeat
older than six seconds latches the hook into pass-through.

Readiness records module-enumeration failures and mappings separately. It
requires zero Jarvis mappings, zero mappings in the verified desktop Explorer
PID and zero unexpected Windhawk mappings. A pre-existing base-runtime mapping
outside Explorer is accepted only when its module name, exact Windhawk 1.7.3
path, size and SHA-256 all match the reviewed file and its host is not a
Windhawk/Jarvis process. The disabled installer requires that accepted set to
remain byte-for-byte identical before and after the update; it never stops the
unrelated host merely to obtain a cosmetically empty global count.

The native metadata also suppresses volatile PE link timestamps. Before a new
DLL hash is admitted to the controller, two independent fixed-toolchain builds
must produce the same M2 SHA-256.

## Required human gate

Before activation, all of the following must be true in the same task:

1. The user reviews a fresh receipt and Supervisor compatibility report.
2. The kill switch is armed and the permit is absent.
3. The canonical M2 source/build identity exactly matches the receipt.
4. M1 is off and remains build-only.
5. A second recovery terminal is opened from the current plan with
   `-ConfirmOpen`. The following read-only command must report `ready=true`,
   a heartbeat no older than four seconds, and the same plan/PID identities:

   ```powershell
   dotnet run --project .\src\Jarvis.Supervisor --configuration Release --no-build -- inspect-recovery-terminal --module jarvis-taskbar-icon-size
   ```

   The terminal visibly displays this prepared recovery command:

   ```powershell
   dotnet run --project .\src\Jarvis.Supervisor --configuration Release --no-build -- arm-kill-switch
   ```

6. The user explicitly approves this exact command and loading only M2:

   ```powershell
   dotnet run --project .\src\Jarvis.Supervisor --configuration Release --no-build -- clear-kill-switch --module jarvis-taskbar-icon-size --confirm
   ```

Until step 6 occurs verbatim after steps 1–5, the command is documentation,
not an action.

## Authorized session sequence

Only a future, separately authorized task may perform this sequence:

1. Re-run readiness, generate a new plan and compare its exact hashes.
2. With Windhawk still stopped, use the plan-bound controller's
   `UpdateDisabledInstallation` action to replace only the already configured,
   disabled M2 source and DLL. It accepts the exact reviewed old hashes, backs
   them up, installs only the canonical new hashes and rolls back on error.
   A leftover lease may be retired only when Supervisor reports it blocked and
   its recorded terminal PID no longer exists.
3. Open the visible recovery terminal from the same unexpired plan and verify
   its fresh lease.
4. Start the Windhawk service with M2 still disabled by running the plan-bound
   controller's `StartDisabledHost` action. It uses Windows SCM directly so the
   service remains `Manual`; it does not launch the Windhawk application.
5. Recheck the recovery lease, then clear the kill switch once with the exact
   command above. The controller intentionally cannot perform this step.
6. Immediately run the controller's `EnableOnce` action. It changes only M2's
   existing `Disabled` value and requires the exact permit to be consumed by
   the canonical DLL in the same desktop Explorer PID.
7. Run a bounded idle `Observe` action, then execute
   [the interaction checklist](M2-INTERACTION-CHECKLIST.md).
8. Re-arm with the exact Supervisor command before any unload or recovery
   step.
9. Run the controller's `Recover` action. It disables M2 and requests a normal
   service stop; it never forces a process exit or restarts Explorer.
10. Verify the permit is absent, M2 is no longer mapped and any remaining
   Windhawk base-runtime mapping is explicitly recorded rather than removed by
   force.

No step may be put into an unattended loop.

Every controller invocation must validate its result against
`config/m2-controlled-live-controller-receipt.schema.json` before printing or
publishing it. The session plan binds both the controller and this schema by
path, size and SHA-256. A schema failure is a failed action, never acceptable
live evidence.

The exact controller forms are:

```powershell
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-M2ControlledLiveValidation.ps1 `
  -Action UpdateDisabledInstallation `
  -SessionPlanPath <fresh-plan> `
  -ExpectedExplorerProcessId <verified-pid> `
  -OutputPath <unique-update-receipt> `
  -RetireStaleRecoveryLease `
  -ConfirmUpdateDisabledInstallation

pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-M2ControlledLiveValidation.ps1 `
  -Action StartDisabledHost `
  -SessionPlanPath <fresh-plan> `
  -ExpectedExplorerProcessId <verified-pid> `
  -OutputPath <unique-start-receipt> `
  -ConfirmStartDisabledHost

pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-M2ControlledLiveValidation.ps1 `
  -Action EnableOnce `
  -SessionPlanPath <same-fresh-plan> `
  -ExpectedExplorerProcessId <same-verified-pid> `
  -OutputPath <unique-enable-receipt> `
  -ConfirmEnableOnce

pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-M2ControlledLiveValidation.ps1 `
  -Action Observe `
  -SessionPlanPath <same-fresh-plan> `
  -ExpectedExplorerProcessId <same-verified-pid> `
  -ObservationSeconds 10 `
  -OutputPath <unique-observe-receipt>

dotnet run --project .\src\Jarvis.Supervisor `
  --configuration Release --no-build -- arm-kill-switch

pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-M2ControlledLiveValidation.ps1 `
  -Action Recover `
  -ExpectedExplorerProcessId <same-verified-pid> `
  -OutputPath <unique-recovery-receipt> `
  -ConfirmRecover
```

## Immediate stop conditions

Re-arm immediately on:

- Explorer crash, hang or restart;
- broken Win key, Start, task buttons, tray, drag ordering or accessibility;
- unexpected window, overlay or input interception;
- wrong icon geometry at any DPI or monitor;
- elevated or growing idle CPU, memory, handle or thread counts;
- compatibility/profile drift;
- any module other than M2 being loaded;
- any receipt, mapping or cleanup state that cannot be explained.

急停是**加载互锁和运行时静默请求**，不是结束进程的按钮。Re-arming
does not prove physical unload or visual restoration.

## Recovery boundary

The recovery terminal runs `arm-kill-switch` first. Explorer recovery is a
separate destructive action and still requires the exact
`restart-explorer --confirm` approval. There is no watchdog, automatic restart,
retry loop or force bypass.
