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
`FREEFORM`; no tiled preset is selected and independent title dragging remains
available. An explicit rail commit enters **Partition**: all sixteen templates
fill every pane with real owned windows, sharing exact zero-gap edges. Existing
active/visible, mode-suppressed and retained hidden sessions are reused before
preparing only the missing Explorer deficit under the four-retained-session cap.
The existing Task Manager is reused; initial FourQuadrants needs two fresh
Explorers. Preparation and admission are transactional. Unassigned windows are
mode-suppressed rather than falsely closed or left floating over the template.
Keyboard template preview never creates, restores or moves windows. Committing
a layout updates the lower-left glyph. Right click the current-layout button,
or use Shift+F10 / Apps while focused, to choose **Freeform** and restore
independent floating bounds without discarding retained content.

Partition titles cannot move or detach a frame. Drag the shared horizontal or
vertical seam instead: transparent 8-DIP hit strips resize neighboring panes
together while preserving exact coverage and window identities. Keyboard axis
arrows move 8 DIP, Shift moves 32, and Home/End use legal limits; a real
RangeValue AutomationPeer uses the same validation. Panes clamp at a simultaneous
320 x 188 DIP minimum. Pointer dragging does not steal editor/Search focus.
Release or capture loss retains valid cuts; pointer Escape rolls back only the
still-current gesture. No resizing timer, animation or render loop is added.
Ratios persist in bounded memory per template and survive zoom/restore.
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
button minimizes that session; explicit ID entries restore only their session.
In Partition, close/minimize atomically refills the exact pane while excluding
the target; older hidden/Closed sessions may be reused. A failed refill rejects
the entire hide instead of leaving a hole. Showing an unassigned identity or
creating a window swaps it into a pane rather than floating it over the layout.
Partition maximize temporarily zooms one occupant; restore returns the exact
assignments and adjusted cuts without allocation. Freeform retains ordinary
floating maximize/restore. These interactions affect only controls
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

Current pre-publication verification for enforced, resizable Partition:

- Release build: passed with zero warnings and zero errors;
- WPF vector-adapter scenarios: 13 / 13;
- layout and edge-bar scenarios: 25 / 25;
- Explorer workspace scenarios: 40 / 40, including mouse/keyboard creation,
  independent mutable trees, same-seed-ID rename isolation, owned preview,
  cap/reopen, already-focused valid/invalid blur, lifecycle/focus-intent races
  and unload cleanup; three consecutive unfiltered runner passes;
- owned Task Manager surface scenarios: 10 / 10;
- owned-window layout scenarios: 126 / 126 (25 legacy coordinator, 36 original
  hosted, 1 WPF visual-tree contract and 64 Partition); the legacy algorithm
  bodies remain intact while audited hosted-policy conflicts are explicitly
  migrated to enforced placement. Coverage includes all sixteen topologies,
  transactional real fill, suppression/lifecycle refill, exact-ID swaps,
  shared-divider pointer/key/automation paths, gesture cancellation and zoom,
  active-highest stacking, focus isolation and composed density draft retention;
  three consecutive unfiltered runner passes;
- Partition runner: 64 / 64; three preplanned consecutive unfiltered passes.
  All original 39 bodies remain intact. The 25 added direction entries cover a
  compile-first backward-selection RED/GREEN plus forward/backward, first/last,
  valid/invalid, owning/newer-Search/foreign focus width/height round-trips with
  physical-editor and same-seeded-ID session isolation. Public routed text input
  and subsequent Shift navigation exercise real editor state, not normalized
  public `CaretIndex` alone;
- arranged-control/rule/ledger pixel probes: 33 / 33, comprising sixteen defaults,
  fifteen resized templates and both minimum-size owners; these render hosts are
  hit-test-disabled and are not genuine pointer proof;
- separate genuine SendInput harness: 17 / 17 unfiltered in the owned product
  MainWindow, including split/stripe/T/grid resizing, clamp/reversal, Escape,
  capture loss, title enforcement, zoom, minimum panes and rename/focus retention.
  The parent replays the former composed Search failure with byte-exact native
  case source. Its full replay changes only screenshot filenames to keep
  case-specific image history; assertions and input flow are unchanged. The
  scoped direction-fix agent reruns all original 17 cases unfiltered and the
  byte-exact parent caret and composed reproducers on the corrected artifact.
  An additional no-case-filter replay passes all 18 appended-harness entries;
  its unchanged literal-17 `completeUnfilteredPass` Boolean remains false.
  The caret fixture uses routed editor commands, not native keyboard selection;
  divider movement is genuine admitted SendInput with null product overrides;
- platform layout: 16 / 16;
- publication boundary: passed for 541 candidate files with no secret values
  printed; publication/platform requirements include all new partition helpers;
- current owned-preview audit: 26 / 26, `locked-profile-approved`. The default
  desktop matched the unchanged approved SHA-256 exactly. The Task Manager
  structural render retains SHA-256
  `3971C3ECB2D725C9F5EB12F6F95A95E43600E16EF41D4F91DED57055A2BDEACF`.

The source gate reads the actual shell, Explorer and embedded Task Manager XAML
owners, and includes the workspace, shared `VisualTreeSearch` and new partition
helpers in capability bans. Applicable negative XAML predicates use unchanged
matching rules across all three rendered owners, including
image, square geometry,
gradient/glow, device content, Start, HueSlider and obsolete-handler bans.
Positive-role checks and window-root constraints remain unchanged. The current
actual-predicate probe rejects 135 / 135 owner/placement mutations; twenty
source-owner canaries also exercise forbidden authority/timer/animation paths.
Neither probe writes published source or approves a canonical. The original
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
these passing paths do not establish its cause. The post-migration historical
blank-rename/Window-focus (093) and rail-focus (115) failures likewise remain
undiagnosed. The newly confirmed composed density loss has separate permanent
RED/GREEN and container/layout trace evidence; it is not a claimed explanation
of those historical failures. Current hash-bound parent receipts are under
`jarvis-partitions/parent/`; current scoped selection-direction execution receipts,
source/artifact hashes, images and failed chronology are separately under
`jarvis-partitions/fix-caret-direction/`. Density migration now retains the
physical owning TextBox and its anchor/moving intent instead of reconstructing
the selected range. Shared row-data visibility, guarded blur, selected-row
realization and owner-only focus restoration preserve ordinary commit/cancel and
newer focus. Logical scroll offset remains subject to WPF viewport clamping,
without a new automatic-scroll policy. This is fix-agent execution, not fresh
parent verification or independent approval; the pre-publication record does not replace
independent frozen-payload approval or exact-SHA remote/CI readback.
No capability ban, approved hash
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
