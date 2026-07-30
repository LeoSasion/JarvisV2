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

The selected frame grammar is `aperture-contour-v1`, based on the fourth
reviewed visual variant. It preserves style only, not the mockup layout,
functions or motifs. Its reusable primitives are point, line, tangent arc and
plane; frames are subtractive and open, exactly two local focus junctions may
carry the current shared `RgbFrame` accent, and no bitmap resource is
required. Local controls do not render glow.

## Future global effects

Glow is reserved for one future desktop-global VFX compositor together with
particles and post processing. The intended authoring model follows
parameterized 3D film and game-engine systems, with domains for spawn, motion,
lifetime, appearance, color and size over life, material, render order and
post processing. A Galaxy View-like control surface can later expose these
parameters without making individual windows own independent effect engines.

The theme intent records the direction, while the separate
`neural-void-global-vfx-v1` contract now provides a validated, platform-neutral
parameter vocabulary:

- component geometry remains point/line/arc/compound-path/plane and consumes
  the shared RGB
  frame;
- the global compositor may consume the same frame and add synchronized
  particle and post-effect layers;
- 30 scalar, integer, range, enum and curve parameters are organized into five
  particle modules and five ordered post effects;
- all effect nodes are disabled by default;
- local glow, the compositor runtime and its parameter UI are not implemented.

See `NEURAL-VOID-GLOBAL-VFX-CONTRACT.md` for render stages, quality budgets and
the fail-closed parameter rules.

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
RGB frames for `static`, `breathe`, `spectrum` and `signal-pulse` effects. The
same model now embeds and compiles the shared global VFX parameter contract;
that operation validates data only and does not render an effect.

```powershell
pwsh -NoLogo -NoProfile -File `
  .\scripts\Test-Windows10RgbThemeModel.ps1 `
  -DotnetPath C:\path\to\dotnet.exe
```

This model defines approved visual values and cannot apply them. The separate
`Jarvis.Win10.NeuralVoidPreview` project now renders the frames inside its own
WPF process, including deterministic PNG evidence; it still cannot apply them
to Windows. See `WINDOWS10-NEURAL-VOID-OWNED-PREVIEW.md`.

Neither project has native imports, process access, registry access, device
I/O or Shell transport. Shell mutation and activation remain false, and live
Explorer remains `not-run`.
