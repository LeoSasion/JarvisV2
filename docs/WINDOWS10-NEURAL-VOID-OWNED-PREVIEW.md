# Windows 10 Horizon Membrane desktop shell owned preview

> Evidence boundary: `Jarvis.Win10.NeuralVoidPreview` is an own-process WPF
> preview. It reports `shellMutationSupported=false` and
> `liveExplorer=not-run`. Nothing in this document is evidence of a real
> Explorer injection or Shell mutation.

`Jarvis.Win10.NeuralVoidPreview` renders the signed **Windows 10 Horizon
Membrane Desktop Shell** expression inside its own 1600-by-900 WPF window. It
simulates a Windows 10 desktop, a restored Explorer window, a taskbar and a
layout instrument without inspecting or styling the live Shell. The executable
and this document retain the historical Neural Void name, but the current
black-and-yellow Horizon Membrane contract supersedes the former multicolor
aperture and preset descriptions.

![Current JARVIS2 Windows 10 UI baseline](screenshots/jarvis-win10-current-ui-baseline.png)

The checked-in image above is the canonical deterministic baseline. The
finish-review record in [`design-qa.md`](../design-qa.md) documents its visual
and interaction verification.

## Visual contract

The canvas is pure black. Yellow (`#FFF000`) is the only chromatic accent; it
marks current state, focus junctions and selected structure. Orange, cyan,
purple, green, RGB cycling and other accent variants are not part of this
component. Text and structural neutrals remain achromatic.

| Structure tier | Color | Stroke |
| --- | --- | --- |
| Primary | `#C2C2BE` | 2 px |
| Secondary | `#626562` | 1 px |
| Quiet | `#303230` | 1 px |

All major geometry is orthogonal and zero-radius. Device-pixel-aligned retained
vectors form the icons, layout topologies, taskbar marks and Explorer controls;
there are no gradients, glow fields, particles or bitmap placeholders in this
signature shell.

## Deterministic 1600-by-900 composition

- A 126-pixel layout rail occupies the left layout axis and meets the horizontal
  taskbar at the exact `y=800` crossing.
- The lower-left taskbar slot is the current-layout glyph, not a Start button.
  Its rail contains sixteen unique layout topologies: one maximized window, six
  two-window splits, eight three-window structures and one four-quadrant grid.
  A clipped 556-pixel viewport shows ten items at once rather than expanding all
  sixteen. The glyphs use a 54-pixel pitch, are ordered by pane count, grouped
  with whitespace, and share the lower-left slot's 70-by-42 geometry and `x=63`
  centerline. Both glyph elements begin at `x=28`; their rendered neutral frames
  occupy the identical `x=34..91` pixels and selected brackets occupy the
  identical `x=29..96` pixels. Rail templates are explicitly zero-indent so
  Windows theme defaults cannot move the list column.
- Six desktop icons occupy the left side of the working field.
- A non-maximized 930-by-667 Explorer window occupies the right side. The files,
  storage summaries, clock and date belong to the deterministic fixture; the
  visible storage rows do not establish a fixed three-drive product contract.

## Interaction contract

Hovering the current-layout slot fades the rail in. Leaving the instrument
starts its delayed retraction rather than hiding it immediately. Selecting any
of the sixteen layouts moves the selected state and updates the lower-left
current-layout glyph. The catalog validates exact pane coverage, stable order,
unique topology signatures and horizontal/vertical/rotational closure before the
surface can run.

The rail's separate 44-pixel upper and lower edge zones start auto-scroll after a
short dwell without covering any layout item. Motion uses a single bounded
background timer and physical pixel offsets. Leaving the edge zone, using the
wheel or keyboard, selecting a layout, closing the rail, reaching an endpoint or
unloading the surface stops it immediately. Reduced-motion environments keep the
navigation but advance one complete 54-pixel item per step. Reopening the rail
reveals the current selection without changing it.

The simulated Explorer remains operational within the preview: minimize,
maximize, close and restore all work, and its taskbar button can activate or
restore it. The maximize control selects the single-window topology; restoring
returns to the last tiled topology. These interactions affect only controls
owned by the WPF preview; they do not invoke or mutate the real Windows Explorer
process.

## Run the interactive preview

```powershell
dotnet .\src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\bin\Release\net8.0-windows\jarvis-win10-neural-void-preview.dll show
```

The interactive host opens the fixed Horizon Membrane shell. It does not expose
the former A/C/D palette presets, a continuous hue slider or multicolor effect
modes.

## Deterministic evidence

```powershell
dotnet .\src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\bin\Release\net8.0-windows\jarvis-win10-neural-void-preview.dll `
  render .\preview.png 56.470588 static 0
```

The render command creates a 1600-by-900 PNG and a JSON receipt for the one
accepted yellow case. On the target Windows 10 build 19045, the canonical PNG
SHA-256 is:

`2158EEA1184EFD22CBE3B630D662F3562A02DFC27955922F4321E2D1957AD9E0`

Final bounded verification passed:

- owned-preview audit: 15 / 15;
- WPF vector adapter: 13 / 13;
- layout and edge-bar scenarios: 19 / 19, including actual descendant-bounds
  equality for all sixteen rail glyphs and the current-layout glyph.

The receipt and tests remain fail-closed about authority:
`shellMutationSupported=false`, `liveExplorer=not-run`. They validate the
own-process rendering and interaction model only, not real Explorer injection,
Explorer styling or live Shell mutation.
