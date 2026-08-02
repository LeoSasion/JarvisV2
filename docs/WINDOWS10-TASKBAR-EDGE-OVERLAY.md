# Windows 10 owned taskbar edge overlay

`Jarvis.Win10.TaskbarEdgeOverlay` is the first visible Neural Void canary on
the classic Windows 10 taskbar. It does not replace the taskbar or load code
into Explorer. A separate, transparent WPF window draws only an eight-DIP
signal rail along the top edge of one exactly admitted primary taskbar.

## Safety and interaction boundary

The canary:

- consumes the exact `win10-22h2-19045.6466-x64` host profile and the fresh,
  read-only Shell topology inventory;
- requires one explicit `Shell_TrayWnd` HWND whose PID is the desktop Shell
  PID and whose TID, class, visibility and bottom-horizontal geometry match;
- applies `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW` and `WS_EX_NOACTIVATE` only
  to its own HWND and returns `HTTRANSPARENT`;
- has no buttons, text, input targets or opaque background, so Start, running
  applications, notification area, clock and Show Desktop remain native;
- hides when the taskbar is hidden or a foreground fullscreen window covers
  the taskbar edge;
- starts fully transparent to prevent a pre-position flash and remains hidden
  while Windows High Contrast is active;
- closes immediately if the exact HWND/PID/TID/class retires, the taskbar
  moves to the top or side, or its geometry becomes unsupported;
- closes after an explicitly confirmed 10-60 second TTL.

The project does not write a taskbar property, send Explorer a message, call a
DWM setter, inject a DLL, start Windhawk, restart Explorer, change the registry
or clear `%LOCALAPPDATA%\JARVIS2\disabled.flag`.

## Visual contract

The visible contract is `neural-void-taskbar-edge-canary-v1`. A one-pixel
shared-RGB datum and small authored triangle consume
`jarvis-visual-signal-v1`. The sharp vector is retained while a duplicate
triangle receives a three-DIP Gaussian blur in a bounded region. High Contrast
retreats the whole rail. Disabled client animation or an unavailable WPF
render tier removes motion/glow while preserving a static vector core. There
are no bitmap, gradient or Unicode icon assets.

This is an interaction and performance canary for the future native taskbar
theme, not the final taskbar implementation. Passing offline tests does not
prove that the rail has been visually observed on the live taskbar.

## Offline verification

```powershell
pwsh -NoLogo -NoProfile -File `
  .\scripts\Test-Windows10TaskbarEdgeOverlay.ps1
```

The audit enforces the read-only dependency boundary, native import allowlist,
single-target gate, transparent/no-activate window styles, fullscreen retreat,
shared RGB frame, vector/glow budget, negative mutation receipt fields,
warning-free Release build and 21 deterministic policy scenarios.
It also samples the same analytic vector geometry consumed by the runtime
XAML, applies a deterministic Gaussian post-process and writes a 1600x48 PNG
under `artifacts/win10-taskbar-edge-overlay-tests`. That artifact is an
offline density/glow-capability check, not a screenshot of the Windows
taskbar. Its receipt also constrains the changed-pixel count, color count and
vertical bounds so an empty or overdrawn image cannot pass by file existence.

## Bounded preview procedure

First run the read-only Shell probe and copy the single primary taskbar's
`rootWindow` value:

```powershell
dotnet run --project `
  .\src\platforms\windows10\Jarvis.Win10.ShellSurfaceProbe `
  --configuration Release -- inspect
```

After reviewing that fresh inventory, run only the exact handle:

```powershell
dotnet run --project `
  .\src\platforms\windows10\Jarvis.Win10.TaskbarEdgeOverlay `
  --configuration Release -- show `
  --expected-window-handle 0x123456 `
  --ttl-seconds 30 `
  --confirm-owned-taskbar-edge-overlay-preview
```

Visual verification requires captures before launch, while the rail is
visible, during a fullscreen retreat, and after TTL expiry. The before and
post-expiry taskbar must be pixel-identical outside unrelated clock/activity
changes. Until that evidence exists, the current state is offline-verified and
live-unobserved.
