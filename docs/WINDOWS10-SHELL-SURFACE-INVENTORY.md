# Windows 10 Shell surface inventory

`Jarvis.Win10.ShellSurfaceProbe` is the second Windows 10 backend slice. It
collects a bounded structural inventory of desktop, File Explorer and classic
taskbar windows on an exactly admitted Win10 host.

The probe does not collect window titles, folder paths, UI Automation names or
control content. It does not send window messages or change a window,
process, service, registry key or system file.

## Run

```powershell
dotnet build `
  .\src\platforms\windows10\Jarvis.Win10.ShellSurfaceProbe\Jarvis.Win10.ShellSurfaceProbe.csproj `
  --configuration Release

dotnet run `
  --project .\src\platforms\windows10\Jarvis.Win10.ShellSurfaceProbe `
  --configuration Release `
  --no-build `
  -- inspect
```

For the complete local audit, keep one ordinary File Explorer folder window
open and run:

```powershell
pwsh -NoLogo -NoProfile -File `
  .\scripts\Test-Windows10ShellSurfaceProbe.ps1
```

CI uses `-StaticOnly`. It compiles and audits the import/capability boundary
without assuming that a CI runner matches the exact local host or has a folder
window open.

## Exact admission

Both Win10 probes now consume `Jarvis.Win10.HostAdmission`. That platform
library embeds `config/windows10-host-profiles.json` and requires an exact
build, UBR, architecture, installation type and Explorer image identity.

The `win10-22h2-19045.6466-x64` profile grants
`read-shell-window-topology`. It still grants no Explorer write, injection,
module activation or restart capability.

## Bounded reader

The reader:

- enumerates top-level windows and direct child relationships;
- records class name, visibility, rectangle and PID/TID;
- caps each tree at 1,024 nodes and depth 8;
- produces a SHA-256 topology fingerprint;
- ties accepted roots to the current exact Explorer process set;
- requires `GetShellWindow` and the primary `Shell_TrayWnd` to resolve to the
  same desktop Shell PID.

Non-Explorer windows that happen to use `Progman` or `WorkerW` are ignored.
An Explorer or taskbar root with a mismatched PID is a failure.

## Observed Win10 19045.6466 topology

The first read-only run observed one complete surface set without restarting
or changing Explorer:

- desktop: `Progman` → `SHELLDLL_DefView` → `SysListView32`;
- Explorer: `CabinetWClass` with `ShellTabWindowClass`,
  `UIRibbonCommandBar`, `DirectUIHWND`, `SHELLDLL_DefView`,
  `NamespaceTreeControl` and `msctls_statusbar32`;
- taskbar: `Shell_TrayWnd` with `ReBarWindow32`, `MSTaskSwWClass`,
  `MSTaskListWClass`, `TrayNotifyWnd`, `TrayClockWClass`,
  `TrayShowDesktopButtonWClass` and the Win10 `Start` class.

These observations are evidence for the next selector-planning slice, not
permission to style or inject into any of those surfaces.
