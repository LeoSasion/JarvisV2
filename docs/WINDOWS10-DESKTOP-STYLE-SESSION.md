# Windows 10 bounded desktop style session

`Jarvis.Win10.DesktopStyleSession` is the first profile-bound Windows 10
adapter that can prepare a real Shell visual preview without loading a DLL into
Explorer. It changes only the desktop `SysListView32` text `COLORREF` for
10–60 seconds through a bounded scalar `SendMessageTimeoutW` call.

This adapter does not make the Windows 11 modules compatible with Windows 10.
It does not hook, inject, start Windhawk, change the registry, restart
Explorer, alter icon positions, change the wallpaper, or style File Explorer
and taskbar internals.

## Admission

Every command except `model-test` performs the following checks again:

1. exact host profile `win10-22h2-19045.6466-x64`;
2. exact desktop and primary-taskbar topology tied to the desktop Shell PID;
3. the `run-bounded-desktop-text-color-preview` capability;
4. `%LOCALAPPDATA%\JARVIS2\disabled.flag` is an ordinary armed file;
5. `%LOCALAPPDATA%\JARVIS2\active-module.txt` is absent.

The profile continues to report `activationPermitted=false` and
`liveExplorer=not-run`. The adapter grants only the bounded non-module preview.

## Build and read-only inspection

```powershell
dotnet build `
  .\src\platforms\windows10\Jarvis.Win10.DesktopStyleSession\Jarvis.Win10.DesktopStyleSession.csproj `
  --configuration Release

dotnet run `
  --project .\src\platforms\windows10\Jarvis.Win10.DesktopStyleSession `
  --configuration Release `
  --no-build `
  -- inspect
```

`inspect` reads the current desktop text color. It performs no mutation.

## Prepare a preview

The read-only planning form is:

```powershell
dotnet run `
  --project .\src\platforms\windows10\Jarvis.Win10.DesktopStyleSession `
  --configuration Release `
  --no-build `
  -- plan-preview `
  --preset neural-emerald `
  --ttl-seconds 30
```

The allowed Neural Void presets are:

- `orbital-cyan` (`#00E5FF`);
- `reactor-amber` (`#FF6A00`);
- `neural-emerald` (`#00FF9A`).

The plan prints the exact apply command and an exact emergency rollback command.
Planning does not write the desktop or create a session journal.

## Live command boundary

The apply command requires the exact
`--confirm-live-desktop-text-color` flag. Before the first SET, it persists the
original color and exact HWND/PID/TID identity under
`%LOCALAPPDATA%\JARVIS2\DesktopStyle`. After SET it invalidates and
synchronously redraws only that exact admitted `FolderView` HWND, then reads
the value back. Rollback restores the scalar value, redraws the same exact
target and verifies the original value in `finally` on timeout, Ctrl+C, or an
exception. No broadcast redraw is permitted.

The common exact-PID rollback command remains usable independently of the
Win10 adapter so recovery is not blocked if the OS profile later drifts.

The bounded preview and rollback have been visually verified on the exact
profile above while the kill switch remained armed. This does not validate or
authorize any DLL module, Windhawk activation, Explorer injection, or broader
Shell styling.
