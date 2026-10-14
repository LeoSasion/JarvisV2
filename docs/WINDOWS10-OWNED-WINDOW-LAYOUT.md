# Windows 10 owned-window layouts

The owned desktop distinguishes independent **Freeform** windows from fully
occupied, resizable **Partition** templates. Initial Freeform retains the
original floating bounds, fonts, palette and canonical pixels. This feature
does not discover, create or manage operating-system/third-party windows.

## Commands and transactional real fill

Committing any of the sixteen rail templates enters Partition. Rail arrows,
Home/End only preview; Enter/Space commits. Ordinary current-layout-button
activation retains its guarded rail-focus handoff. Right click that button,
or use Shift+F10 / Apps while focused, for the black/white mono **Freeform**
command. Its access key, automation name and tooltip/help make exit discoverable
without permanent decoration.

Candidate order is active identity, other visible MRU, mode-suppressed spares,
retained Minimized, retained Closed, then real Explorer allocation for the exact
deficit. Activation/registration order break ties. Active occupies the catalog
primary pane. Initial FourQuadrants reuses Task Manager and prepares **two**,
not three, new Explorers. Geometry is validated before allocation; all missing
controls/resources/event wiring are prepared detached before batch admission
and Canvas publication. Late faults and invalid geometry/admissions preserve
counters, identities, assignments and actual children. No blank Border or
model-only ID counts as an occupied pane.

Explorer IDs remain `explorer`, `explorer-2`, `explorer-3`, `explorer-4`. The cap
includes all retained Explorers, even Closed or suppressed ones. Published
IDs/slots are never recycled. Each frame retains its real `ExplorerSurface`
and independent memory-only `ExplorerWorkspace`: navigation/history, selection,
rename, folders and owned preview are per-session. Shared seed IDs are not
window identities or a shared real filesystem. Task Manager stays read-only
telemetry with no process control.

The Explorer taskbar menu supplies **New Explorer window** and exact-ID
Activate/Show, Restore or Reopen. New is disabled and inert at cap. In Partition,
showing an unassigned identity or creating one swaps it into its previous
compatible pane, otherwise the active/primary pane. The displaced session is
mode-suppressed, not destroyed or falsely Closed. Assigned activation does not
reshuffle panes or cuts.

## Lifecycle, zoom and exit

Only assigned frames are shown. Suppression is distinct from explicit
Minimize/Close. Hiding an occupant atomically refills its exact pane, excluding
that target from the same refill. Prefer another suppressed/retained hidden
session, then prepare an admissible fresh Explorer if necessary. **Older Closed
sessions may be reused** to keep coverage. Preparation/admission failure rejects
the whole hide with an honest no-change status, not a hole, partial hide or
immediate resurrection of the target.

Partition maximize is temporary one-window zoom. Restore returns the exact
underlying template, assignments and resized cuts, not an MRU/default-ratio
replan. Neither allocates. If the zoomed occupant is hidden, its exact-pane
replacement takes the zoom; per-ID restore can swap the original back. Even
the single-pane template never silently exits through title maximize/restore.

Freeform exit restores independent floating bounds and reverses only
mode-induced suppression. Currently Minimized/Closed sessions stay hidden;
a hidden session already reused as refill is now a visible occupant. Fresh
sessions remain retained. The command also restores floating bounds after
ordinary Freeform maximize. Freeform keeps ordinary hide/reopen, thresholded
independent title dragging and release/capture-loss completion.

Partition titles may activate/focus exact content, but cannot move a frame or
exit. Legacy model mutations and the WPF manual-bounds seam reject independent
mutation in Partition. Body focus/stacking keep active above other owned frames.
Automatic fill, resizing and zoom do not request content focus. Existing
deferred-focus origin/generation/owner guards remain in force.

## Shared geometry, input and density

The admitted work area is `(148,24,1428,752)` DIP. Shared edges are projected and
rounded once for exact zero-gap coverage; adjacent frame rules are not gutters.
Transparent 8-DIP hit strips overlay seams without reserving space. T branches
occupy only their connected branch; stripe cuts remain ordered; FourQuadrants
shares one vertical and one horizontal cut. At crossings, the later horizontal
overlay owns the pointer, while both controls remain focusable.

Cuts use preset, axis and original grid coordinate. A bounded in-memory
catalog-keyed cache retains successful ratios through template switches and
zoom, without settings writes. Resizing preserves IDs, assignments, active ID,
activation history and floating bounds. Only affected panes receive new geometry
and density. Validation covers components and Right/Bottom, intermediate
projection, normalized ordering, integer edge reconstruction, legal clamp
intervals and post-rounding minima. Every atomic cell has exactly one owner;
strict positive-area overlap is rejected. Coincident edges are legal, unlike
WPF `Rect.IntersectsWith` checks.

Pane minima are **320 x 188 DIP**. Hosted and separate pixel probes prove the
simultaneous pair for Explorer and Task Manager, controls at least 28 DIP inside
the pane and content viewport floors of 44/43 DIP. Compact density changes below
820 width / 400 height. Template migration retains uncommitted valid/invalid
rename text, error, forward/backward anchor and moving-caret intent and the
focused owning editor without
committing blur. Scrolled-item, preview and newer Search/foreign-session focus
traces cover both directions. A composed focused width/height round-trip can
leave the selected row outside the virtualized viewport. With newer Search
focus, a subsequent template refresh drops that row, and the first layout pass
need not create its replacement. Migration explicitly realizes the same selected
rename target before reattaching its original physical TextBox and selection;
it does not
focus an unfocused editor or alter ordinary blur/identity policy. Initial
full-size composition is unchanged.

The draft and shared editor style are retained before template teardown. Editor
visibility follows the current row's `IsRenameTarget` in that shared style rather
than a departing template's named-child setter. This avoids reconstructing a
backward range with `Select(start,length)`, which reverses its active end even
when public `CaretIndex` and the normalized range appear unchanged. Ordinary
Shift-navigation commands verify both directions; valid blur still commits,
Escape still cancels, and a subsequent rename binds the current Name again.
Height changes preserve logical scroll offset subject to WPF viewport clamping;
they do not introduce an automatic-scroll policy for a retained off-viewport row.

Pointer drag uses absolute DesktopCanvas anchoring and actual WPF capture,
not additive Thumb-local deltas. It does not focus the divider or steal
editor/Search focus. Tab/focus and axis arrows move 8 DIP; Shift moves 32;
Home/End use legal endpoints. The real AutomationPeer provides a quantized
ratio RangeValue, limits, small/large changes and finite/stale rejection.
Observable value changes feed automation notifications for peer, arrow,
pointer and Escape restoration through the same validated operation.

Release/capture loss keeps valid cuts. Pointer Escape rolls back the starting
cuts only while generation/template/assignment intent remains current; the
shell preview route consumes it before content/host Escape. Intent is invalidated
before release. Mode/template/membership/zoom, disable, unload, host deactivate/
minimize and stale requests cannot change or roll back newer state. Rejected
resize requests do not cancel live capture. No resize timer, animation, render
loop or global focus hook was added.

## Legacy compatibility and deliberate assertion migrations

The separate legacy APIs and **25 original coordinator algorithm scenarios**
retain registered-only partial fill, 12-DIP gutters, unchanged extras, arbitrary
IDs, tiny-area rules and legacy lifecycle behavior. Actual rail UI uses the new
enforced API; old algorithms cannot break active Partition. Workspace, Task
Manager and vector scenario bodies remain unchanged.

Only the audited hosted conflicts changed. Unrelated identity/content/focus,
history, stacking, cap and usability assertions remain:

| Hosted scenario/group | Intentional replacement |
|---|---|
| `hosted-real-populated-three-and-four-pane-explorer-layouts` | Initial 2/4/no-factory becomes real 4/4/two deficits; manual allocation accounts for one remaining slot; exact-ID restore swaps the retained object. |
| `hosted-current-layout-reports-occupied-panes` | Truthful full occupancy after refill; restore the original ID. |
| `hosted-partial-fill-moves-windows-and-keeps-rail-live` | Full zero-gap bounds/help replace empty spares; historical scenario ID remains. |
| `hosted-passive-actions-leave-spare-window-minimized` | Explicit Freeform exit retains passive non-restoration/sampling checks; independent Partition cases test refill. |
| `hosted-minimize-restore-and-maximize-retain-pane` | Hide retains full count; per-ID restore replaces aggregate-MRU restoration. |
| `hosted-close-and-reopen-return-to-floating-bounds` | Explicit exit before unchanged floating close/reopen checks. |
| `hosted-body-focus-and-manual-move-update-active-freeform-state` | Enforced WPF no-op before exit and original floating movement checks. |
| `hosted-title-drag-and-capture-loss-commit-only-real-moves`; `hosted-secondary-title-focus-rail-and-mru-routing` | Original drag/focus/routing checks in explicit Freeform; independent Partition title/divider cases. |
| `hosted-promoted-active-frame-above-mru-after-rail-restore-and-maximize` | Only suppressed extra visibility/lifecycle replaces unchanged-extra policy; preserve bounds, IDs/content, active-highest Z, external focus, ordinals and MRU order. |
| Missing/minimized restore, six-splits/both-active-choice bounds and populated-pane helper | Shared zero-gap rectangles; coincident edges are not positive overlap. |

Edge-bar changes are limited to explicit exit before unchanged floating bounds,
exact-ID restores in the original inner-control lifecycle case, and renaming
obsolete partial-fill labeling while adding real full-frame occupancy checks.
Baseline/current bodies and reasons remain in assertion/edge migration receipts.
No test-only exit substitutes for the actual Freeform command.

## Verification and limits

Owned layout enumerates **126 = 25 legacy + 36 original hosted + 1 traversal +
64 Partition**, checked against actual unique entries. The 13 earlier density
entries are the composed last-file round-trip/newer-Search regression plus a
12-case first/last, valid/invalid, owning/Search/foreign focus matrix with repeated
width/height round-trips and same-seeded-ID physical-surface isolation. Valid
unfocused drafts are edited after an invalid draft gives up focus; intentional
valid-rename blur still commits. The scoped direction fix adds 25 entries without
changing any original 39 bodies: one compile-first backward-selection regression
and a 24-case forward/backward, first/last, valid/invalid, owning/Search/foreign
matrix. Repeated captured width and height round-trips check subsequent Shift
navigation, physical-editor and same-seeded-ID session isolation, routed text
input with its original OneWay binding, current style/automation, error/preview/
scroll context and ordinary completion. Three preplanned unfiltered cycles pass
64/126/40 on the corrected artifact. Original runners remain
workspace 40, edge-bar 25, Task Manager surface 10 and vector 13. Full preview
has 26 checks, platform 16, publication 541 candidates. Helpers stay in the same
preview project and explicit platform/publication requirements. Capability bans
are unchanged or stronger. Exact negative XAML predicates cover all three
rendered owners; the actual-AST probe rejects 135/135 full/hidden/compact-template
mutations (15 canaries x 3 owners x 3 placements). Source-owner canaries also
exercise filesystem/native/process-control/timer/animation bans.

Raw compile-first behavior RED/GREEN logs, exits, source snapshots, artifact
hashes, no-op fingerprints and migration bodies are retained in the Hermes
scratch `jarvis-partitions/implementation/` bundle. Separate hosted renders cover
sixteen defaults, fifteen resized templates and both minimum owners: 33 frames
with actual arranged bounds, populated controls and rule/control/ledger pixels.
Their hit-test-disabled host is **not physical pointer proof**. Routed tests
prove real WPF capture/routing. The separate byte-exact scratch native-input
harness now passes all 17 cases unfiltered on the corrected artifact, including
the formerly failing composed Search sequence. It uses an interactive product
MainWindow, genuine SendInput down/up/moves, exact owned-HWND/PID admission,
null product input overrides and bounded finally cleanup. This is owned-process
WPF input evidence, not native Shell injection. The correction's permanent
RED/GREEN, instrumented container/layout traces, native receipts and raw hashes
remain in `jarvis-partitions/fix-density/`. The prior parent record repeats the
39/101/40 runners three times, replays the composed case with byte-exact native
source and runs all 17 native cases unfiltered. Its full replay changes only two
screenshot filename expressions to prevent cross-case image overwrites; it does
not change assertions or input. Hash-bound raw receipts, images and probe source
are in `jarvis-partitions/parent/`. This is a pre-publication evidence record;
independent frozen-payload approval and exact-SHA remote/CI readback are separate.

The selection-direction correction is separately recorded under
`jarvis-partitions/fix-caret-direction/`: permanent behavior RED/GREEN, all
original 17 native cases passing unfiltered, byte-exact parent caret and composed
replays, and a no-case-filter 18-case replay retaining all original 17 source bytes.
The appended harness's unchanged `completeUnfilteredPass` field still requires
literal 17, so that 18-case receipt reports false for that field despite 18/18
enumerated passing checks; the original 17-case receipt reports true. The caret
fixture uses routed WPF editor commands, not native keyboard selection; resize
uses genuine admitted SendInput with null overrides. This is fix-agent execution
proof, not new parent verification or independent approval.
Historical post-migration blank-rename/Window-focus (093) and old rail-focus
(115) failures remain undiagnosed. This scoped composed-loss fix is not evidence
that it explains those failures.

Unchanged approved Freeform canonical:
`F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2`.
Unchanged standalone Task Manager:
`3971C3ECB2D725C9F5EB12F6F95A95E43600E16EF41D4F91DED57055A2BDEACF`.
No palette/canonical reapproval occurred. Existing column/row fixture PNGs in
`screenshots/jarvis-win10-owned-layout-columns.png` and
`screenshots/jarvis-win10-owned-layout-rows.png` are historical pre-Partition
placements, not current zero-gutter proof.

```powershell
pwsh -File .\scripts\Test-Windows10NeuralVoidPreview.ps1
# After the Release build:
dotnet .\src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\bin\Release\net8.0-windows\jarvis-win10-neural-void-preview.dll test-partitions
dotnet .\src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\bin\Release\net8.0-windows\jarvis-win10-neural-void-preview.dll render-owned-layout .\owned-layout.png 56.470588 static 0 FourQuadrants
```

Receipts retain `ownProcessOnly=true`, `thirdPartyWindowControlSupported=false`,
`shellMutationSupported=false`, `liveExplorer=not-run`, `mutationPerformed=false`.
This is an owned WPF preview, not styling/management of live Windows Explorer
or Task Manager and not a new native Shell authority.
