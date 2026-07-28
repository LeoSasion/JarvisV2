# Phase 8: Controlled desktop text-color session

Status: **IMPLEMENTED — LIVE APPLY NOT YET AUTHORIZED**

## Objective

Introduce the smallest reversible native-desktop experiment after the read-only
host probe: a time-limited change to the Explorer desktop ListView text color.
This is not a shell replacement, canvas overlay, injected module or persistent
desktop configuration.

## Mutation boundary

The controller may send exactly two documented ListView messages to the exact
`SysListView32` desktop `FolderView`:

- `LVM_GETTEXTCOLOR` to capture and verify a scalar `COLORREF`;
- `LVM_SETTEXTCOLOR` to apply or restore a scalar `COLORREF`.

The controller does not change text background, window background, icon
spacing, icon positions, wallpaper, registry, services, processes, DWM
attributes or Explorer lifecycle state. It does not open a process, install a
hook, inject code, start Windhawk, restart Explorer or terminate anything.

## Fail-closed session protocol

- [x] Require exactly one visible `Progman` or `WorkerW` desktop host.
- [x] Bind the exact expected Explorer PID, TID and three HWND identities.
- [x] Read the original color before any mutation.
- [x] Persist the original color and target identity atomically under
  `%LOCALAPPDATA%\JARVIS2\DesktopStyle` before the first SET attempt.
- [x] Reject a second session while an active, prepared or rollback-failed
  journal exists.
- [x] Use `SendMessageTimeoutW` with a 250 ms timeout and
  `SMTO_BLOCK | SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT`.
- [x] Carry scalar values only; no cross-process pointers are marshalled.
- [x] Revalidate the exact target immediately before SET.
- [x] Limit every preview to 10–60 seconds.
- [x] Route TTL expiry, Ctrl+C and exceptions through `finally` rollback.
- [x] Read back and verify the original color after rollback.
- [x] Never send a rollback value to a replacement Explorer HWND.
- [x] Require separate exact confirmation tokens for apply and manual rollback.

## Commands

Read-only inspection:

```powershell
dotnet run --project .\src\Jarvis.DesktopStyleSession `
  --configuration Release --no-build -- inspect `
  --expected-explorer-pid <pid>
```

Read-only plan:

```powershell
dotnet run --project .\src\Jarvis.DesktopStyleSession `
  --configuration Release --no-build -- plan-preview `
  --expected-explorer-pid <pid> `
  --preset graphite `
  --ttl-seconds 60
```

The live `apply-preview` command is intentionally documented but remains
unauthorized until a fresh read-only plan and its exact emergency rollback
command have been reviewed in the current task.

## Acceptance

1. Release build is warning-free.
2. All offline policy scenarios pass.
3. The safety audit proves the narrow import/message allowlist and journal
   ordering.
4. A live read-only inspection identifies the current target and reads its
   existing text color without mutation.
5. Public CI and the project gate run the audit with `-StaticOnly`.
6. Windhawk remains stopped/manual, the kill switch remains armed, and no live
   apply occurs during development or CI.
