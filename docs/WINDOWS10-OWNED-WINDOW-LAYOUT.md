# Windows 10 owned-window layouts

The Horizon Membrane preview now applies layout presets to its registered
Explorer sessions and the read-only Task Manager surface. The initial desktop is Freeform:
each surface retains its own floating bounds, and the current-layout slot shows
overlapping windows instead of claiming that a tiled preset is applied.

## Availability and placement

The catalog contains sixteen exact-cover topologies, and every preset is
always selectable. Explicitly applying a preset restores enough registered
own-process windows to fill it where possible. Minimized windows take priority
over closed surfaces, with activation order then registration order within
each group. The existing active window stays active and occupies the primary
pane; restoring another surface does not steal focus. Lifecycle changes are
committed only after the complete placement geometry is valid.

The initial desktop registers the primary Explorer (`explorer`) and the closed
read-only Task Manager. Selecting a two-pane layout from that initial state
therefore opens the already registered Task Manager. Extra Explorer sessions
are allocated only by the taskbar's **New Explorer window** action, never by a
layout commit. Three- and four-pane presets place real populated WPF surfaces
when enough sessions have been manually registered; otherwise spare panes stay
empty. An empty pane is not a new window. Rail tooltips describe planned restore
and fill counts; the current-layout slot reports actual occupied panes.
No operating-system or third-party window is restored or created.

## Manual Explorer sessions

Right click the Explorer taskbar button, or focus it and use Shift+F10 / the
Apps key, to open its normally closed black/white mono context menu. **New
Explorer window** has an access key and supports keyboard Enter. Retained
sessions have explicit ID-bearing **Activate**, **Restore** or **Reopen** entries.
The primary ID remains `explorer`; extras are lazily assigned `explorer-2`,
`explorer-3` and `explorer-4`. The four-session limit includes the primary.
New is disabled at the limit with a tooltip/accessibility explanation. Closing
retains the session and its allocation slot; its exact ID remains reopenable.

Each frame contains a real `ExplorerSurface`, with an independent
`ExplorerWorkspace` and mutable seeded tree. Navigation/history, selection,
rename validation/editor, folder creation, empty state and owned preview belong
to that surface. Shared seed item IDs cannot route an editor into another
surface. There is no shared virtual filesystem claim. The original primary
outer frame and default full-size composition remain unchanged; compatibility
accessors are reserved for snapshots and legacy tests, not feature routing.

The taskbar button and desktop navigation shortcuts target the active Explorer,
or the most recent running Explorer when another surface is active. Only when
all Explorer sessions are closed does explicit taskbar activation reopen the
most recent retained session. The running marker aggregates all sessions:
visible = full opacity, all running sessions minimized = dimmed, all closed =
hidden. Per-ID title controls and title gestures never operate on another frame.
Non-button title activation hands keyboard focus to that session's ledger,
visible preview or retained rename editor. Automatic layout restoration does
not request focus. Deferred Explorer and shell focus requests retain their
generation, owning-session/lifecycle/active-ID guards and also require the same
keyboard-focus origin at dispatch. Hosted traces prove that newer Search focus
wins over pending secondary minimize/close and Task Manager rail-return focus.
The current-layout button uses that same guarded/cancellable shell queue:
normal intent focuses the selected rail item, while newer Search or another
Explorer focus wins, and unload or a superseding click aborts the old operation.
Passive rename blur validates or commits without reactivating the departing
session or requesting replacement focus: valid names commit, while invalid
text/error state remains correctable without taking Search or another
Explorer's focus. Explicit Enter still returns to the ledger or validation
editor. Unload aborts pending work and releases capture; no polling or global
focus-event hooks were added.

The active window occupies the largest pane. Equal-area panes use top-to-bottom,
left-to-right order; remaining participants follow activation order and then
registration order. Extra visible windows retain their bounds. The usable area
is `(148, 24, 1428, 752)` DIP, internal gaps are 12 DIP, and placement edges are
rounded to integer device-independent coordinates. Maximizing then restoring
returns the last applied tiled preset even when it is now only partially
fillable. Double-clicking an owned window title bar toggles maximize and
restore, matching Windows muscle memory; single click and drag behavior are
unchanged.

## Lifecycle and compact presentation

Minimizing retains the window's bounds; restoring returns to them. If they
exactly match an unoccupied pane in the currently committed geometry, the
coordinator restores that pane association even after an intervening
maximize/restore cycle. It does not reuse a stale association from another
topology or work area. Closing breaks the applied layout, and reopening
returns to the last independent floating
bounds. Dragging any owned title bar returns the desktop to Freeform only
after the system drag threshold is crossed and the window actually moves.
Clicks and sub-threshold pointer jitter preserve the applied layout and saved
floating bounds. Release or capture loss commits an actual move exactly once.
Maximize applies the single-window preset; restore reapplies the last remembered
tiled preset using visible windows only, or restores floating bounds when no
tiled preset is remembered. Unlike an explicit rail commit, maximize/restore
does not reopen unrelated closed or minimized windows. A committed manual drag
clears the remembered tiled preset.
Body clicks and keyboard focus update the active window and stacking order.
The active owned frame is always above all other owned frames; historical
activation ordinal and registration order sort only the non-active frames.
Minimize can promote a visible identity without changing its ordinal. An explicit
rail restore preserves that identity/history, so a newer restored secondary must
not cover it on a subsequent rail maximize. Extra visible frames retain their
bounds and lifecycle; stacking does not activate, hide or move them.
Rail keyboard navigation steps through all sixteen presets. Home and End jump
to the first and last preset. Previewing neither moves nor restores windows;
Enter commits and may restore missing registered surfaces. When the rail owns
keyboard focus, Escape is consumed and returns focus to the active owned
window; otherwise Escape remains unhandled.

Below 820 DIP width, Explorer uses a compact file ledger and Task Manager uses
process name, PID and CPU columns. Below 400 DIP height, auxiliary summaries
collapse. Explorer also collapses its folder illustration and storage rows in
compact presentation. Its compact action strip stays operable instead of
hiding rename/open/new-folder actions. Short panes use 32-DIP title, navigation
and action bands, a 24-DIP ledger header and at least 28-DIP control bounds;
even the 182-DIP side panes retain a usable populated ledger. Compact previews
retain their close control and item name; rename validation remains visible in
the navigation band. Hidden auxiliary panels are explicitly collapsed and
compact content is clipped to its owned bounds. Task Manager
retains its sampling status in the compact ledger header. Full row values and
file names remain available to accessibility and tooltips.

![Owned two-column layout](screenshots/jarvis-win10-owned-layout-columns.png)

![Owned two-row layout](screenshots/jarvis-win10-owned-layout-rows.png)

These existing two-pane fixture images use deterministic preview processes and virtual files.
Current compact control bounds are checked by the hosted scenarios below. They do not
show a live Windows Task Manager or a styled Windows Explorer.

## Verification

`test-owned-window-layout` passes 55/55 scenarios: 18 coordinator, 36 hosted
WPF scenarios and one real WPF visual-tree contract. The shared traversal keeps
depth-first child order, excludes the descendant starting node, includes the
parent starting source and preserves null behavior. The existing coordinator
is reused without a model rewrite.
New hosted traces cover exact secondary lifecycle/content retention, title
click/jitter/drag/capture loss/double-click, body activation, MRU stacking, rail
focus return, actual populated three/four-pane bounds and usable controls.
`test-explorer-workspace` passes 30/30, including taskbar creation, independent
navigation/selection/rename/new-folder/preview, the inert retained-session cap,
keyboard New/Restore and lifecycle/focus-intent/unload races. Hosted checks cover both active-window choices for all six splits,
actual element bounds, partial fill, compact controls, lifecycle, focus,
manual movement, title clicks/jitter/double-click, maximize/restore, capture
loss, and rail keyboard navigation. Automatic-restore checks cover initially
closed and minimized surfaces, both windows closed, taskbar running markers,
sampler restart, active-window preservation, planned/actual pane metadata,
minimal restore count, MRU preference, idempotence, and transactional rejection
of invalid layouts or work areas.
Both extended runners passed three consecutive unfiltered runs. The added
regressions route F2 from the actual `Keyboard.FocusedElement` after a
secondary title click, prove already-focused valid/invalid rename blur to
Search and a foreign Explorer, and preserve intervening Search focus after
secondary minimize/close and Task Manager rail Escape. Five additional current-
layout-button cases prove newer Search/foreign-Explorer focus preservation,
retained-operation cancellation before dispatch, and normal selected-item
handoff with preview/Escape intact, without pre-focusing the intended item.
All previous scenarios and assertions remain intact. The historical
Escape-focus flake remains **UNDIAGNOSED**, not fixed or suppressed.
The full preview gate passed 26/26, edge-bar 25/25, Task Manager surface 10/10,
platform layout 16/16 and publication boundary for 536 files. Both recorded
render hashes are unchanged. The original actual-expression owning-XAML probe
missed 24 Explorer insertions and then rejected its 60 tested mutations; that
real but partial record remains unchanged in
`jarvis-multi-explorer/fix-round1/fix-round1-verification.json`.
The extended actual-AST probe inventories all applicable desktop-owner negative
Contains predicates and retains the five device-regex canaries. It reproduced
15 further Explorer misses for Start/HueSlider and obsolete-handler bans, then
rejected 90/90 shell/Explorer full, hidden and compact-template mutations after
the minimal variable-scope fix. Counts derive from 15 canaries, two owners and
three placements, with unique traces checked. No matching rule, positive role
or capability ban changed, and neither published XAML nor canonical was mutated
by either probe. Focus/policy RED/GREEN logs and receipts remain in
`jarvis-multi-explorer/fix-round2/fix-round2-verification.json` under the Hermes
scratch evidence root. The additional permanent hosted stacking scenario first
failed with active Explorer Z=6 below restored explorer-2 Z=7, then passed for
both promoted Explorer and Task Manager. It invokes the real taskbar menu/button,
commits EqualColumns then Maximized without reactivating the promoted body, and
asserts active-highest ZIndex, unchanged active ID/external keyboard focus and
activation ordinals, retained extra bounds/lifecycle/content, no fresh allocation
and unchanged non-active MRU/registration ordering. The actual receipt supplies
the 54-scenario gate count. A byte-exact copy of the parent's scratch reproducer
now returns `not-reproduced`/exit 0 with active Z=7 above secondary Z=6 and the
same active ID, keyboard focus and rectangles. The original bug receipt/PNG are
untouched; new logs and differential output are in
`jarvis-multi-explorer/fix-zorder/fix-zorder-verification.json`.
The deterministic render host excludes pointer hit-testing, preventing live
mouse hover from changing canonical evidence. Interactive `show` mode remains
operable and uses a different host.
Explorer retains at least 44 DIP of content viewport
and Task Manager at least 43 DIP in every available split.

The row/column render checks also require actual Task Manager title-rule and
window-control pixels. Deterministic scenes are admitted before the first native
frame, preventing a shorter tile from retaining its earlier floating clip.

The public gate includes these scenarios:

```powershell
pwsh -File .\scripts\Test-Windows10NeuralVoidPreview.ps1
```

Reproduce a deterministic layout frame:

```powershell
dotnet .\src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\bin\Release\net8.0-windows\jarvis-win10-neural-void-preview.dll `
  render-owned-layout .\owned-layout.png 56.470588 static 0 EqualColumns
```

The receipt remains `ownProcessOnly=true`,
`thirdPartyWindowControlSupported=false`, `shellMutationSupported=false`,
`liveExplorer=not-run`, and `mutationPerformed=false`. This slice registers no
third-party HWND and introduces no Shell transport or injection authority.
