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

Phase 4 can create a short-lived, source-bound plan and exercise the observer
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

## Required human gate

Before activation, all of the following must be true in the same task:

1. The user reviews a fresh receipt and Supervisor compatibility report.
2. The kill switch is armed and the permit is absent.
3. The canonical M2 source/build identity exactly matches the receipt.
4. M1 is off and remains build-only.
5. A second recovery terminal is open with this command prepared:

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

1. Re-run readiness and compare its exact hashes.
2. Start/configure Windhawk only as explicitly approved for M2.
3. Clear the kill switch once with the exact command above.
4. Load only `jarvis-taskbar-icon-size` once.
5. Execute [the interaction checklist](M2-INTERACTION-CHECKLIST.md).
6. Re-arm before any unload or recovery step.
7. Verify the permit is absent and no automatic reload occurs after a new
   Explorer lifecycle.

No step may be put into an unattended loop.

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
