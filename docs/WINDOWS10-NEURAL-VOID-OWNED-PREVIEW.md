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

![Current approved JARVIS2 Windows 10 owned-desktop baseline](screenshots/jarvis-win10-current-ui-baseline.png)

The checked-in image above is the current approved deterministic full-frame
baseline. It includes the interactive in-memory Explorer workspace, taskbar and
layout rail. It was rendered repeatedly with identical bytes on locked Windows
10 build 19045.6466 at 96 DPI in software-render mode, then visually reviewed in
a connected graphics session. The owned Task Manager is intentionally hidden in
this canonical desktop scene and has a separate deterministic render below.

## Visual contract

The canvas is pure black. Yellow (`#FFF000`) is the only chromatic accent; it
marks current state and focus junctions. Selection uses pure white (`#FFFFFF`),
including full/compact Explorer signals and text-editor selection. Orange, cyan,
purple, green, RGB cycling and other accent variants are not part of this
component. Text and structural neutrals remain achromatic.

| Structure tier | Color | Stroke |
| --- | --- | --- |
| Primary | `#C2C2BE` | 2 px |
| Secondary | `#626562` | 1 px |
| Quiet | `#303230` | 1 px |

All major geometry is orthogonal and zero-radius. Device-pixel-aligned retained
vectors form the icons, layout topologies, taskbar marks and Explorer controls.
There are no chromatic or fill-color gradients, glow fields, particles or bitmap
placeholders; the achromatic opacity feather is the sole gradient exception.

## Deterministic 1600-by-900 composition

- A 126-pixel layout rail occupies the left layout axis and meets the horizontal
  taskbar at the exact `y=800` crossing.
- The lower-left taskbar slot is the current-layout glyph, not a Start button.
  Its rail contains sixteen unique layout topologies: one maximized window, six
  two-window splits, eight three-window structures and one four-quadrant grid.
  A clipped 556-pixel viewport shows about eight to nine items at once rather than
  expanding all sixteen. The glyphs use a 64-pixel pitch, are ordered by pane count, grouped
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

The rail and orthogonal axis remain visible at all times. The desktop starts in
`FREEFORM`; no tiled preset is selected. All sixteen presets are selectable.
An explicit commit restores already registered owned surfaces only, fills as
many panes as those surfaces permit, and honestly reports partial fill. Keyboard
preview never creates, restores or moves windows. A layout commit does not
allocate new Explorer sessions. Manually registered Explorer sessions plus Task
Manager participate in real three/four-pane placement, and extra visible windows
retain their existing bounds. Committing a layout updates the lower-left glyph.
The catalog validates exact pane coverage, stable order,
unique topology signatures and horizontal/vertical/rotational closure before the
surface can run.

Pointer height across the rail now acts as continuous scroll pressure rather than
two fixed edge switches. Exact vertical center is stationary; a short activation
curve blends into a 65% linear / 35% smoothstep pressure response, removing the
old low-speed shelf before rising to 180 pixels per second at the viewport edges.
There is no dwell. Normal motion follows WPF rendering
timestamps and integrates physical pixel offsets without forcing a full list
layout on each frame. Returning to center, leaving the rail, using the wheel or
keyboard, selecting a layout, reaching an endpoint or unloading the surface stops
immediately without snapping or changing the physical offset. Reduced-motion
environments use a fixed 200-millisecond tick and bounded 1-to-32-pixel
pressure-dependent steps, avoiding delayed full-item jumps.

Escape from the permanently visible layout list stops any active rail motion;
when the rail itself holds keyboard focus, Escape also returns that focus to the
active owned window and is consumed, otherwise it bubbles without consuming the
key, preserving the owning window's global Escape action. Clicking the lower-left
current-layout button requests selected-item keyboard focus through the same
guarded/cancellable shell scheduler: newer Search or foreign Explorer focus
wins before dispatch, and unload or a superseding click aborts the old request.
If Windows high contrast changes while the surface is open, rail motion stops and
the opacity feather immediately becomes fully opaque; the normal boundary-aware
mask returns when high contrast is disabled.

The 86-by-556 list viewport applies a frozen continuous opacity mask over 256
pixels at each scrollable end: 0% at the clipping edge, then 20%, 50% and 100%
inward, leaving a 44-pixel fully opaque center. A reached boundary is fully opaque. Because the fade is spatial and every
glyph itself remains at 100%, partial items dissolve smoothly into the black field
without a hard black plate, scrollbar, arrow or extra rule segment.

The simulated Explorer remains operational within the preview: minimize,
maximize, close and restore all work, and its taskbar button can activate or
restore it. The taskbar's normally closed mouse/keyboard session menu adds
**New Explorer window** and ID-bearing Activate/Restore/Reopen actions. There
are at most four retained Explorer sessions, including the original `explorer`;
closed sessions retain their content and exact reopen identity, so closing does
not free a fifth allocation slot. The aggregate taskbar button and desktop
navigation shortcuts target the active/MRU Explorer. Clicking the active taskbar
button minimizes that session; explicit ID entries restore only their session. The maximize
control maximizes only the active owned window; restoring returns that window
to its saved floating or tiled bounds. These interactions affect only controls
owned by the WPF preview; they do not invoke or mutate the real Windows Explorer
process.

The Explorer content surface is also an operational, deterministic in-memory
workspace per reusable `ExplorerSurface`, not a shared virtual filesystem.
Each surface supports independent selection, Back / Forward / Up navigation, folder open,
an owned-process file preview, inline rename with Windows-style validation,
unique new-folder naming, long names and explicit empty locations. File previews
never launch an operating-system application, and the model has no real
filesystem access. Preview focus cycles inside the visible layer and disables
the covered item list until the preview closes.

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
accepted yellow case. The current approved owned-desktop PNG on locked Windows
10 build 19045.6466 at 96 DPI in software-render mode has SHA-256:

`F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2`

Current bounded verification for manual multi-instance Explorer:

- Release build: passed with zero warnings and zero errors;
- WPF vector-adapter scenarios: 13 / 13;
- layout and edge-bar scenarios: 25 / 25;
- Explorer workspace scenarios: 30 / 30, including mouse/keyboard creation,
  independent mutable trees, same-seed-ID rename isolation, owned preview,
  cap/reopen, already-focused valid/invalid blur, lifecycle/focus-intent races
  and unload cleanup; three consecutive unfiltered runner passes;
- owned Task Manager surface scenarios: 10 / 10;
- owned-window layout scenarios: 55 / 55 (18 coordinator, 36 hosted,
  1 WPF visual-tree contract), including
  real populated three/four-pane placement, usable compact controls and exact
  secondary title/lifecycle/focus routing, intervening Search/foreign-Explorer
  focus races and current-layout-button cancellation/normal rail handoff;
  the promoted-active stacking regression covers real taskbar New/minimize,
  EqualColumns restoration then rail Maximized, for Explorer and Task Manager
  without body reactivation, changed ordinals, focus theft, moved/hidden extras
  or new session allocation; non-active MRU/registration ordering is retained;
  three consecutive unfiltered runner passes;
- platform layout: 16 / 16;
- publication boundary: passed for 536 candidate files with no secret values
  printed; publication requirements include the control, workspace and shared
  visual-tree helper owners;
- current owned-preview audit: 26 / 26, `locked-profile-approved`. The default
  desktop matched the unchanged approved SHA-256 exactly. The Task Manager
  structural render retains SHA-256
  `3971C3ECB2D725C9F5EB12F6F95A95E43600E16EF41D4F91DED57055A2BDEACF`.

The source gate reads `ExplorerSurface.xaml/.cs` as the actual control owner and
checks the shell, control, workspace and shared `VisualTreeSearch` code against
the filesystem/OS-launch ban. Applicable desktop-owner negative XAML bans examine
both shell and Explorer owning texts with unchanged matching rules, including
image, square geometry,
gradient/glow, device content, Start, HueSlider and obsolete-handler bans.
Positive-role checks and window-root constraints remain unchanged. The original
60/60 mutation record covered only ten canaries and remains real but partial
historical evidence. The copied/extended actual-AST probe reproduced 15 further
Explorer misses, then rejected 90/90 full/hidden/compact-template mutations from
15 canaries across both owners; totals and uniqueness derive from its traces.
Neither probe writes published source or approves a canonical. Focus/policy evidence
remains in `jarvis-multi-explorer/fix-round2/fix-round2-verification.json` under the
Hermes scratch root. The additional stacking repair's permanent hosted RED/GREEN
and byte-exact parent-reproducer differential are in
`jarvis-multi-explorer/fix-zorder/fix-zorder-verification.json`: the parent's
previous active Z=6/secondary Z=7 becomes active Z=7/secondary Z=6, exit 0,
`not-reproduced`, with unchanged identity, focus and extra bounds. The original
bug receipt/PNG remain untouched. The historical Escape-focus flake remains **UNDIAGNOSED**;
these passing paths do not establish its cause. No capability ban, approved hash
or golden PNG is relaxed or advanced.

The row/column render gate also checks the actual Task Manager title rule and
window-control pixels. Snapshot scenes are admitted before the first native
frame so a shorter tile cannot retain a floating-window clip. See
[owned-window layout evidence](WINDOWS10-OWNED-WINDOW-LAYOUT.md).

When the Windows graphics session is disconnected, the pixel-dependent checks
fail closed instead of treating a zero-pixel WPF frame as evidence. A canonical
PNG or approved hash can advance only from a connected locked-profile render and
visual review.

The receipt and tests remain fail-closed about authority:
`shellMutationSupported=false`, `liveExplorer=not-run`. They validate the
own-process rendering and interaction model only, not real Explorer injection,
Explorer styling or live Shell mutation.
