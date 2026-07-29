# Windows 10 Neural Void RGB intent

The approved Windows 10 desktop direction is concept D, **Neural Void**.
Concepts A, C and D contribute three recommended accent colors:

- A / Orbital cyan: `#00E5FF`;
- C / Reactor amber: `#FF6A00`;
- D / Neural emerald: `#00FF9A`.

These are shortcuts, not separate fixed themes. The accent model uses
continuous HSV hue from 0 through 360 degrees, plus saturation, value and an
effect phase. The neutral black-ceramic surface system remains stable while
the accent drives desktop traces, icon focus, Explorer borders and selection,
taskbar running indicators and status highlights.

## Desktop boundary

The desktop composition contains no keyboard, mouse, peripheral illustration
or RGB device-control panel. Neural Void describes the Windows visual
language only.

Physical-world lighting is a separate future consumer of the same RGB frame:

```text
color/effect source
        |
        +-- Windows Shell visual adapter
        |
        +-- external device-lighting bridge (future)
```

The Shell must never depend on the device bridge. If a device disconnects or
a future provider fails, the display continues with its last valid local
frame. No device SDK, HID transport or provider is implemented in this slice.

## Offline model

`Jarvis.Win10.RgbThemeModel` embeds and validates
`config/windows10-neural-void-rgb-theme.json`. It also produces deterministic
RGB frames for `static`, `breathe`, `spectrum` and `signal-pulse` effects.

```powershell
pwsh -NoLogo -NoProfile -File `
  .\scripts\Test-Windows10RgbThemeModel.ps1 `
  -DotnetPath C:\path\to\dotnet.exe
```

This slice defines approved visual values but cannot render or apply them.
It has no native imports, process access, registry access, device I/O or
Shell transport. Execution, mutation and activation remain false, and live
Explorer remains `not-run`.
