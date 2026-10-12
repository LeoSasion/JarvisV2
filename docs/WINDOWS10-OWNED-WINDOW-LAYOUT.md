# Windows 10 owned-window layouts

The Horizon Membrane preview now applies layout presets to its registered
Explorer and read-only Task Manager surfaces. The initial desktop is Freeform:
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

The preview currently has two registered surfaces: Explorer and the read-only
Task Manager. Selecting a two-pane layout from the initial desktop therefore
opens Task Manager automatically. Selecting a three- or four-pane layout can
restore both surfaces, but spare panes still remain empty. Extra Explorer
instances are not implemented, and an empty pane is not a new window. Rail
tooltips and accessibility metadata describe the planned restore and fill
count; the current-layout slot reports the actual occupied pane count.
No operating-system or third-party window is restored or created.

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
bounds. Dragging either owned title bar returns the desktop to Freeform only
after the system drag threshold is crossed and the window actually moves.
Clicks and sub-threshold pointer jitter preserve the applied layout and saved
floating bounds. Release or capture loss commits an actual move exactly once.
Maximize applies the single-window preset; restore reapplies the last remembered
tiled preset using visible windows only, or restores floating bounds when no
tiled preset is remembered. Unlike an explicit rail commit, maximize/restore
does not reopen unrelated closed or minimized windows. A committed manual drag
clears the remembered tiled preset.
Body clicks and keyboard focus update the active window and stacking order.
Rail keyboard navigation steps through all sixteen presets. Home and End jump
to the first and last preset. Previewing neither moves nor restores windows;
Enter commits and may restore missing registered surfaces. Escape returns
focus to the active owned window.

Below 820 DIP width, Explorer uses a compact file ledger and Task Manager uses
process name, PID and CPU columns. Below 400 DIP height, auxiliary summaries
collapse. Explorer also collapses its folder illustration and storage rows in
compact presentation. Hidden panels are explicitly collapsed, so fixed-size
children cannot spill into title bars or remain keyboard targets. Task Manager
retains its sampling status in the compact ledger header. Full row values and
file names remain available to accessibility and tooltips.

![Owned two-column layout](screenshots/jarvis-win10-owned-layout-columns.png)

![Owned two-row layout](screenshots/jarvis-win10-owned-layout-rows.png)

These images use deterministic preview processes and virtual files. They do not
show a live Windows Task Manager or a styled Windows Explorer.

## Verification

`test-owned-window-layout` runs 18 coordinator scenarios and 23 hosted WPF
scenarios. Hosted checks cover both active-window choices for all six splits,
actual element bounds, partial fill, compact controls, lifecycle, focus,
manual movement, title clicks/jitter/double-click, maximize/restore, capture
loss, and rail keyboard navigation. Automatic-restore checks cover initially
closed and minimized surfaces, both windows closed, taskbar running markers,
sampler restart, active-window preservation, planned/actual pane metadata,
minimal restore count, MRU preference, idempotence, and transactional rejection
of invalid layouts or work areas.
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
