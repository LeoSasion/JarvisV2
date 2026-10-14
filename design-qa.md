# Design QA - Horizon Membrane layout rail and Explorer workspace

- Current approved owned-desktop baseline:
  `docs/screenshots/jarvis-win10-current-ui-baseline.png`
- Deterministic evidence: 1600 x 900 locked-profile render,
  SHA-256 `F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2`
- Viewport: 1600 x 900 at 96 DPI
- State: black/yellow desktop, layout rail permanently visible, `FREEFORM`,
  Explorer restored at `(596, 63)` with size `930 x 667`; all sixteen presets
  are selectable and enter fully occupied, zero-gap Partition on explicit
  commit; the untouched initial Freeform scene remains the canonical
- Validation scope: own-process WPF preview only;
  `shellMutationSupported=false`, `liveExplorer=not-run`

## Layout catalog

The catalog still contains 16 stable topologies: one maximized window, six
two-window splits, eight three-window structures and one four-quadrant grid.
All sixteen presets are selectable. The separate historical legacy placement
API restores only already registered owned windows on its explicit commits.
The actual rail now enters enforced Partition: it reuses real owned sessions and
transactionally prepares only missing Explorers, under the retained cap of four.
Every pane is filled and no unassigned floating frame obscures the template.
Titles cannot detach frames; shared divider dragging changes proportions without
exiting. Close/minimize refills the exact pane excluding its target, and an
identity-specific show/New command swaps into a pane. Older Closed sessions may
be reused for refill. Zoom/restore preserves exact assignments and cuts. A
right-click or keyboard context command on the current-layout button returns to
Freeform, restoring floating bounds and only mode-induced suppression. Initial
FourQuadrants reuses Task Manager and prepares two fresh Explorers.
Every definition is an exact cover of a 12 x 12 integer grid. Startup validation
rejects duplicate IDs, orders, presets or signatures, gaps, overlaps, and any
catalog that is not closed under horizontal mirror, vertical mirror and
90-degree rotation.

## Compact rail geometry

Interaction and geometry verdict: **PASS**. The pixel measurements in this
section describe the current approved rail and interactive Explorer full frame,
rendered and reviewed in a connected graphics session.

- The clipped rail viewport is 86 x 556 and presents about eight to nine items
  instead of expanding all sixteen across the work area.
- Every list item is 86 x 64. Its 70 x 42 glyph has an eleven-pixel top and bottom
  inset; group transitions add twelve pixels of whitespace and no divider line.
- The list begins at `x=20`, and its ListBox and GroupItem templates contribute
  zero horizontal chrome or indentation. Every rail glyph element therefore
  occupies `x=28..98`; the fixed lower-left glyph uses the same 70 x 42 token and
  occupies the same range. Both element centers are exactly `x=63`.
- Pixel inspection confirms the neutral linework occupies `x=34..91`
  (`center=62.5`) in the rail and current slot alike; the yellow selected bounds
  occupy `x=29..96` (`center=62.5`) in both places. The former 6-pixel rail drift
  is zero.
- The list sits inside its own 86 x 556 viewport. A frozen linear opacity mask
  feathers each scrollable edge across 256 pixels (0% / 20% / 50% / 100%) with
  strictly increasing stops, so hard clipping is already transparent when it
  meets the black field. The two ramps leave a 44-pixel fully opaque center. At
  a reached boundary that end is 100% opaque.
- The feather is spatial rather than item-based: glyph opacity remains 100%,
  selection geometry is unchanged, and content moves continuously through the
  mask without a black overlay plate, arrow or auxiliary rule.
- The vertical axis remains at `x=124..126` and meets the two-pixel taskbar rule
  at `y=800`, preserving the approved orthogonal cross.
- Pure black, near-white vector content, one `#FFF000` accent, square geometry
  and the existing primary/secondary/quiet rule hierarchy remain unchanged.

## Auto-scroll behavior

- Pointer height is evaluated across the rail instead of through two fixed edge
  switches. Exact center is stationary; a short activation curve blends into a
  65% linear / 35% smoothstep pressure response, eliminating the old low-speed
  shelf while rising continuously to 180 pixels per second at the viewport
  edges. There is no dwell.
- Normal mode integrates requested physical offsets from WPF rendering timestamps
  and caps a delayed frame at 1/30 second. It does not force a full list layout on
  every frame. Reduced-motion and high-contrast modes tick every 200 milliseconds
  with a bounded 1-to-32-pixel pressure-dependent step, so feedback begins
  promptly instead of waiting several seconds before a full-item jump.
- Returning to center or leaving the rail stops at the exact physical offset with
  no snap or rebound. Reaching either endpoint immediately releases the rendering
  callback or reduced-motion timer and prevents idle CPU work.
- Direction changes are exclusive. Wheel input, keyboard input, selection,
  leaving the rail or unloading the surface stops auto-scroll.
- Escape stops auto-scroll. When the rail owns keyboard focus it is consumed
  and returns focus to the active owned window; otherwise it remains unhandled
  so the owning window's global Escape action still runs.
- A runtime Windows high-contrast change immediately stops auto-scroll and
  replaces the feather with a fully opaque mask; unloading the surface removes
  the static system-parameter subscription.
- Four frozen feather brushes cover the boundary states without per-frame brush
  allocation or per-glyph opacity writes; rapid selection changes still coalesce
  to one reveal operation.
- Scrolling never mutates `CurrentLayout`, the selected data item or the fixed
  lower-left current-layout glyph.
- The permanent rail reveals the selected item without changing it. The first and
  last catalog entries are both fully reachable and fully visible.

## Interactive Explorer workspace

- Six deterministic document fixtures are supplied by a memory-only model;
  navigation, selection, Back / Forward / Up and empty locations are stateful.
- Open displays an owned-process preview and never starts an operating-system
  application or reads the real filesystem. The covered list is disabled while
  the preview is visible, and Tab / Shift+Tab remain contained in the preview.
- Rename validates Windows-compatible names and synchronizes the visible type,
  icon and preview kind when the extension changes. New Folder creates stable,
  unique names and immediately enters inline rename.
- Full names remain available to accessibility and tooltip surfaces when visual
  columns must ellipsize them.

## Current owned-slice verification

- Release build: passed, 0 warnings / 0 errors
- Layout and edge-bar scenarios: 25 / 25
- Explorer workspace scenarios: 40 / 40; three consecutive unfiltered passes
- Owned Task Manager surface scenarios: 10 / 10
- Owned-window layout scenarios: 126 / 126 (25 legacy coordinator, 36 original
  hosted, 1 WPF visual-tree contract and 64 Partition);
  three consecutive unfiltered passes
- Partition scenarios: 64 / 64; three preplanned consecutive unfiltered passes.
  All original 39 bodies remain intact; 25 added entries cover a compile-first
  backward moving-caret RED/GREEN and the forward/backward, first/last,
  valid/invalid, owning/Search/foreign focus matrix through captured width and
  height round-trips, same-seeded-ID physical-session isolation and ordinary
  completion. Direction is checked by subsequent public WPF Shift navigation
- WPF vector adapter: 13 / 13
- Platform layout: 16 / 16
- Publication boundary: passed for 541 candidate files; no secret values printed
- Full owned-preview audit: 26 / 26, `locked-profile-approved`
- Extended actual-predicate owning-XAML probe: 135 / 135 mutations detected
  (15 canaries across three owners and full/hidden/compact-template placements)
- Partition source-owner probe: 20 / 20 canaries detected
- Arranged-control/rule/ledger pixel probe: 33 / 33, covering all sixteen default
  templates, fifteen resized templates and real Explorer/Task Manager at the
  simultaneous 320 x 188 DIP minimum; hosted renders are not pointer evidence
- Genuine SendInput owned-MainWindow harness: 17 / 17 unfiltered; exact
  HWND/PID/foreground/hit-target admission, null product input overrides, observed
  native down/up and WPF capture, interference abort and finally cleanup.
  The parent also replays the formerly failing composed Search case with
  byte-exact native case source. The full parent replay changes only two
  screenshot filename expressions to preserve case-specific image history.
  The scoped direction-fix agent reruns the unchanged original 17 cases and
  byte-exact parent caret/composed reproducers on the corrected artifact.
  A no-case-filter appended-harness replay passes 18/18 checks, but its unchanged
  literal-17 `completeUnfilteredPass` field remains false. The caret-selection
  fixture uses routed editor commands, not native keyboard selection
- Default canonical SHA-256 unchanged:
  `F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2`
- Task Manager structural render SHA-256 unchanged:
  `3971C3ECB2D725C9F5EB12F6F95A95E43600E16EF41D4F91DED57055A2BDEACF`

This pre-publication record separates hosted/deterministic render evidence from
genuine native input delivered to the owned WPF preview. Neither is live Shell
injection or native-release acceptance. Hash-bound parent receipts remain in
`jarvis-partitions/parent/` under the local Hermes scratch root; independent
frozen-tree approval and exact-SHA remote/CI readback are separate release proof.
Current scoped correction receipts, source/artifact hashes, images and failures
are under `jarvis-partitions/fix-caret-direction/`; this is fix-agent execution,
not new parent verification or independent approval. The retained physical
TextBox keeps draft and real anchor/moving intent; its shared style uses current
row-data visibility, while the existing blur/realization/focus guards remain.
Ordinary valid blur, Escape cancellation and subsequent Name binding are checked.
Logical scroll offset is retained subject to WPF viewport clamping, not a new
automatic-scroll policy for retained off-viewport rows.
Historical records below retain their original scope, dates, counts and
unresolved findings. The historical Escape-focus, post-migration blank-rename
(093) and rail-focus (115) failures remain undiagnosed. The confirmed composed
width/height density round-trip draft/caret loss is fixed with a permanent
behavior RED/GREEN and same-target virtualized-row realization inside the existing
blur guard. That scoped correction does not claim to explain the historical
failures. Earlier full native-harness runs overwrote four same-named Search PNG
versions; their failed history audit is retained. The parent's filename-only
variant preserves every case's referenced images rather than rewriting history.
The implementation's early slices did not each achieve full-regression GREEN;
the actual failed chronology remains preserved, not retroactively relabeled.
Current stacking-repair evidence is in
`jarvis-multi-explorer/fix-zorder/fix-zorder-verification.json` under the local
Hermes scratch root; earlier focus/policy evidence remains unchanged.

## Push-preparation boundary (2026-10-09)

This approval covers only the Win10 owned-process slice. It is not a complete
native-release or real Explorer-injection acceptance. The local whole-project
audit was run with `-SkipManagedBuild` and did not pass: the locked portable
Windhawk toolchain is not provisioned, the required lifecycle receipt and
historical native run summary are missing, and receipt script/lock identities
do not match. Those evidence gates remain fail-closed. Dedicated preview,
Control Center and Supervisor Release builds passed with zero warnings/errors.
Pi Host static checks passed 14/14; no authenticated provider or real Shell
session was exercised.

The Impeccable final-quality pass preserved the existing Horizon Membrane world
and drove the compact-pane/content and title-chrome verification above. Native
pointer automation was unavailable (`GetCursorPos` access denied), so hosted
WPF scenarios and deterministic PNG review are not reported as a live mouse or
keyboard acceptance session. The final safety state was verified read-only:
kill switch armed, one-shot module permit absent, `liveExplorer=not-run`.

The interaction-hardening follow-up also preserves that visual baseline. Both
owned title bars defer layout changes until the system drag threshold is crossed
and the clamped position actually changes. Plain clicks, repeated clicks,
sub-threshold jitter and non-drag capture loss preserve maximized restore bounds
and tiled assignments. An actual drag is finalized on release or capture loss.
The three new hosted regressions failed against the previous handlers and now
pass; the complete owned-preview gate remains 26/26 with the same canonical hash.

## Locked-profile re-approval on the new Windows 10 host (2026-10-11)

The clone moved to a second physical Windows 10 machine: Pro 22H2, build
19045.6466, UBR 6466, x64, 96 DPI, software-only rendering — the same locked
pixel profile as the original VM. The first render on the new host produced a
different SHA-256 (`9B5F27C2…`) while every structural check passed. Diagnosis:
the owned desktop renders mono text with `Cascadia Mono, Consolas`; the new
host had neither Cascadia Mono nor any substitute installed, so WPF fell back
and 1.72% of pixels differed. Installing Microsoft's official CascadiaMono.ttf
from the cascadia-code v2407.24 release (SHA-256 `08026F61…`) reduced the
difference to 794 pixels (0.06%) against the old canonical; a sweep of twelve
official CascadiaMono releases never reproduced the old VM's exact rasterizer
output, so the residual is host-level rasterization/GDI state, not app code.

Under explicit user authorization for this machine, the checked-in canonical
PNG and approved hash were re-advanced to the new host's deterministic output:
`017999E0A925D23660F1234C76E63024728CDE3ADECFBAE05B9806125D7B84C1`. Two back-to-back
renders on this host hash identically. The old canonical remains recorded here
as the original VM's approved value; CI runs on GitHub-hosted runners and does
not gate on this hash, so no CI contract changes. This re-approval is scoped to
the owned-process preview pixel baseline only — it is not live Shell evidence
and does not relax any other gate.

Host facts for this record: Windows 10 Pro 22H2 19045.6466 x64; .NET SDK
8.0.425; PowerShell 7.6.6; explorer.exe 10.0.19041.6159 SHA-256
`988A56D8…E97E`; kill switch state untouched, `liveExplorer=not-run`, no
mutation performed during this re-approval.

## Provisioned-toolchain native rebuild and first bounded live preview on this host (2026-10-11)

The locked portable Windhawk 1.7.3 toolchain was pre-provisioned on this host
without executing the installer: the hash-and-signature-verified offline
installer payload was carved with 7-Zip, and `windhawk.ini` was taken from a
silent-portable probe extraction. The complete compile-input aggregate
reproduces the lock byte-for-byte (8397 files / 570199839 bytes /
`990DB4CE…6E8B`), and the pinned CPython 3.14.3 launcher, interpreter and four
runtime DLLs match their locked hashes exactly. `Build-NativeMod.ps1` compiled
both reviewed modules into immutable run `20261010T221518556Z-dc5c0762`; the
committed canonical receipt was refreshed accordingly.

Exact-host admission initially blocked this machine because the profile's
Explorer version strings (`10.0.19041.4522`) no longer matched what the host
serves (`10.0.19041.6159`), while size and SHA-256 were byte-identical — the
served strings come from servicing-updated MUI resources, not from explorer.exe
bytes. Under the AGENTS.md compatibility-refresh rule the profile was updated
to the observed identity with size and SHA-256 unchanged.

With admission passing, the read-only Shell surface probe and desktop style
probe both ran live on this host as `read-only-inspection` (no text collected,
kill switch armed, permit absent, Explorer PID/start-time identities unchanged).
The first bounded live preview on this host then ran under the profile's
`run-bounded-desktop-text-color-preview` capability: session
`20261011T144652132Z-f00a3827`, preset `neural-emerald`, TTL 30 seconds, on
the exact admitted FolderView HWND of desktop Shell PID 6188. The journal was
written before the first SET; apply readback observed `#FFFFFF → #00FF9A` with
an accepted nonclient-only redraw; TTL rollback restored and re-verified
`#FFFFFF` with a second accepted redraw, `state=recovered`. Explorer was not
restarted or terminated, no registry write occurred, the kill switch stayed
armed, the one-shot permit stayed absent, and `activationPermitted=false`
throughout. This is bounded non-module desktop evidence only: it does not
validate Win11 module compatibility, does not enable Windhawk host activation,
and does not relax any other gate.

## First bounded owned taskbar-edge overlay session on this host (2026-10-11)

Under the profile's `run-bounded-owned-taskbar-edge-overlay-preview` capability,
the owned edge overlay ran against the exact admitted primary taskbar
(`Shell_TrayWnd`, HWND `0x2008C`, desktop Shell PID 6188 / thread 6316) for a
30-second TTL. Receipt `passed-owned-overlay-session-completed`: 637 visible
rendered frames, zero hidden samples, one reposition, target never retired;
`explorerMutationPerformed=false`, `injectionRequested=false`, mouse-transparent,
no-activate, owned-window-only, and `moduleActivationPermitted=false` throughout.

Visual evidence: full-screen captures before launch, while the rail was visible,
and after TTL expiry. A sampled comparison of the taskbar strip (y 1158–1200,
x 0–1400, excluding the tray/clock region) found 519 changed sample pairs
during the session and **zero** between before and after — the taskbar returned
pixel-identical outside unrelated clock changes. Visual review of the during
capture confirms the emerald edge rail with its central chevron along the top
border, confined to the gap between the app-icon cluster and the tray, matching
the `neural-void-taskbar-edge-canary-v1` contract; no taskbar content is
obscured.

Scope limits: this host had no fullscreen window during the session, so
`fullscreenRetreatSamples=0` and `accessibilityRetreatSamples=0`; the retreat
paths remain offline-verified only. This is bounded owned-window evidence — it
does not validate module injection, does not enable Windhawk host activation,
and does not change the caption-write revocation or any other gate.

## State-lock tokens and taste-audit corrections (2026-10-12)

Mechanical UI audit against the installed taste-skill rules found the preview
already color-locked (zero hardcoded accent hexes, 40 `AccentBrush` bindings),
shape-locked (all-sharp, zero corner radii), and free of banned string tells
(zero em-dashes or middle-dot separators in UI strings). Two corrections to the
audit method itself: pressed feedback lives in the shared `BareButton` control
template (`IsPressed` dims to 0.74; keyboard focus draws an accent border) and
is inherited by all six button styles, so a style-level trigger grep
undercounts coverage; scrollbar page buttons are intentionally invisible
(`Opacity=0`, non-focusable) and need no pressed state.

Shipped changes: one `DisabledOpacity` resource (0.35) replaces the drifting
per-style values 0.34 / 0.28 / 0.48, and the layout-rail option focus ring now
uses the accent border like every other interactive family instead of a quiet
grey rule. The rename editor's focus affordance is structural: it exists only
while renaming and carries an accent caret, selection brush, and underline.
The 45 uppercase mono labels are functional chrome (column headers, rail
navigation, status strips), not decorative eyebrows; none were removed.

Pure `#000000` remains an intentional override of the taste default against
pure black: true black is load-bearing for the feather transparency contract
and avoids adding decorative blur or glow effects - geek minimalism and
performance economy, per the owner's decision. This is a design rationale,
not evidence of zero rendering or compositing cost.

Because the ACCESS action button renders disabled in the canonical frame, the
disabled-opacity lock shifts its pixels; under the established re-approval
process the checked-in canonical PNG and approved hash advance to this host's
deterministic output `9A4DFB3BAF6B79A42F271718D09821B1CA3174FD6901B22FDF23D73D70F07CE6`
(gate-observed and independently re-rendered identically). The separate Task
Manager render hash is unchanged. This is an owned-process preview pixel
baseline change only; it is not live Shell evidence and relaxes no other gate.

## Pure-white selection and brighter disabled state (2026-10-12, owner-directed)

Live exploration feedback on this host: the layout rail read as unselectable
and the surface overall too dark. Root cause of "unlightable": rail presets are
gated by visible owned-window count (Explorer starts alone; Task Manager
registers closed), so multi-pane presets correctly start disabled — but the
previous 0.35 disabled-opacity lock made those glyphs dimmer than the earlier
0.48 and read as dead rather than gated. The owner directed a rollback of that
darkening plus pure-white selection highlights.

Shipped: `DisabledOpacity` returns to 0.48 (still one token, now matching the
brightest prior value), and a `SelectionBrush` (#FFFFFF) replaces accent-colored
selection marks across every interactive family — Explorer row rail and signal
squares, selected item name text, layout-rail glyph corner brackets and marker,
the current-layout taskbar glyph, and Task Manager process rows. Accent stays
on focus rings, caret/selection-in-text, hover thumb, and the feathered axis;
selection is now always pure white everywhere.

Canonical consequence: the current-layout glyph carries selection in the
canonical frame, so both pixel baselines move — desktop canonical advances from
`9A4DFB3BAF6B79A42F271718D09821B1CA3174FD6901B22FDF23D73D70F07CE6` to
`76D3FF62E50303B82A63E08E1625408C3E42E5D38CEA9AB02877D175B606EEEE` and the
separate Task Manager render from `FD2DC1342ADA445B0B9515D98CD31901B7F61A0DDE79AE9C5F9805B0F2CE2412`
to `2FACE1769938D581BD35279624ED0300CE22516B2CBC103F4B564714A2623307`
(gate-observed, independently re-rendered identically). Owned-process preview
pixel baseline only; not live Shell evidence; no other gate relaxes.

## Always-selectable rail with partial fill; title double-click (2026-10-12, owner-directed)

Live exploration feedback: the gated rail read as "cannot light up" and the
Explorer lacked Windows-style minimize-by-title-bar habits. The owner chose the
product-level alternative to gating: every preset is always selectable, and a
preset with more panes than visible windows fills its leading panes (active
window into the primary pane, then activation order) and leaves spare panes
empty; rail items announce "fills N of M panes" instead of disabling. Owned
window title bars now toggle maximize/restore on double-click, matching Windows
muscle memory; single click and drag semantics are unchanged, and a manual
title drag clears the remembered tiled preset so restore returns floating
bounds. Maximizing then restoring re-applies the last tiled preset even when it
is only partially fillable.

Scenario suites moved 29->30 (partial-fill placement proof replaces the
insufficient-windows no-op; hosted double-click scenario added) and the edge-bar
suite renamed its slot scenario to every-layout-applies-and-selects-with-partial-
fill. All green: layout 30/30, edge bars 25/25, full preview gate passed except
the pixel canonical, which moved because rail glyphs no longer render dimmed in
the canonical frame: desktop canonical advances from
`76D3FF62E50303B82A63E08E1625408C3E42E5D38CEA9AB02877D175B606EEEE` to
`F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2` and the Task
Manager render from `2FACE1769938D581BD35279624ED0300CE22516B2CBC103F4B564714A2623307`
to `3971C3ECB2D725C9F5EB12F6F95A95E43600E16EF41D4F91DED57055A2BDEACF`
(gate-observed, independently re-rendered identically). Owned-process preview
baseline only; not live Shell evidence; no other gate relaxes.

## Explicit layout commit restores registered surfaces (2026-10-13)

The previous always-selectable revision implemented partial placement only:
spare panes remained empty, and even registered closed/minimized surfaces were
not restored. This revision adds the missing automatic-restore path for an
explicit rail commit. The planner prefers minimized surfaces, then closed
surfaces, ordered by MRU and registration order, and restores only the number
needed by the preset. It preserves the existing active window and commits
lifecycle changes only after the complete pane geometry has been validated.
The WPF surface synchronizes visibility, running markers and Task Manager
sampling without queuing focus to a newly restored window.

Keyboard preview still neither moves nor restores windows. Maximize/restore
and taskbar activation retain their visible-window-only placement behavior.
Rail metadata describes the planned restore; current-layout metadata reports
actual visible occupied panes rather than the topology's nominal pane count.

Scope limit: there are still only two registered UI surfaces, Explorer and
Task Manager. Two-pane presets can be filled automatically; three/four-pane
presets may still have empty panes. No additional Explorer instance or native
Windows window is created. Multi-instance Explorer remains unimplemented.

Verification on this host: the new closed-window restore, planned-restore
metadata, and actual-fill metadata regressions each failed before their fixes
and passed afterwards. Layout scenarios pass 41/41 (18 coordinator, 23 hosted),
including three additional consecutive runs. Edge bars pass 25/25, Explorer
workspace 21/21 and Task Manager surface 10/10. The full owned-preview gate
passes 26/26 with `locked-profile-approved`; the Task Manager model gate passes
10/10. No golden PNG or approved hash was changed: the pure-black field and
pure-white selection remain intact. This is own-process interaction evidence,
not live Shell styling, injection or third-party window control.

Pre-commit review found two additional regressions and blocked publication:
Task Manager taskbar restoration left occupied-pane metadata stale, and finite
rectangle components could still produce nonfinite edges/panes. Both were
reproduced by the tests before fixing restoration refresh and finite geometry
validation. Coverage now also checks actual keyboard-focus preservation,
hidden-window arrow/Home/End previews, taskbar activation, and remembered
maximize/restore without restoring unrelated windows.

A transient canonical mismatch was traced by pixel comparison to the Terminal
shortcut hover region only (23790 changed pixels, bounds x=199..347,
y=301..469); a sample background pixel changed from #000000 to the button's
#090909 hover color. The snapshot host was receiving live pointer input.
Pointer hit-testing is now disabled only on the deterministic render stage,
with a new regression that failed before the fix. Interactive show-mode uses
a separate host and is unchanged. Three independent fixed renders match the
existing approved hash; the golden was not advanced.

One intermediate run reported an intermittent Escape-focus failure. Five
alternating baseline/candidate runs all passed, so its cause was not established
and it is not claimed fixed. Phase-specific failure diagnostics were added
without weakening assertions or adding automatic retries. The final candidate
then passed three consecutive 41/41 runs and the full 26/26 preview gate.

The final review exposed a longer minimize -> maximize -> remembered-layout
restore -> taskbar-restore sequence that cleared a hidden window's pane
association while retaining its matching bounds. Coordinator and hosted
regressions reproduced it for both surface choices. The coordinator now keeps
the last successfully validated pane geometry and recovers a minimized
window's association only when its retained bounds exactly match an unoccupied
current pane; breaking layout clears that geometry. No window is moved or
unrelated surface revived by this reconciliation. After the fix, the parent
again verified three consecutive 41/41 runs, the full 26/26 preview gate,
Task Manager model and platform gates, with the same approved canonical hash.


## Manual multi-instance owned Explorer (2026-10-13)

Eight vertical hosted RED-to-GREEN traces completed one at a time. The first
compiled against the old shell and failed behaviorally because **New Explorer
window** was absent from `TaskbarExplorerButton.ContextMenu`. Subsequent REDs
proved same-seed-ID editor cross-routing, missing ID-bearing session lifecycle
entries, inactive secondary title double-click, clipped three-pane controls,
the missing retained-session cap, stale cross-session rename focus and stale
external taskbar focus. No retry-to-pass or assertion suppression was added.

The original shell-owned primary frame and its geometry remain intact. Content
is now a reusable `ExplorerSurface` with one independent mutable seeded workspace
per instance. The small identity registry retains up to four Explorer sessions;
menus, title controls, placement, stacking, density and focus resolve identities
instead of primary names. Closed sessions retain their surface/workspace and
slot for exact-ID reopen. Explicit layout commit restores already registered
surfaces only; there is no automatic new-window factory on commit, no shared
virtual filesystem, no filesystem access and no OS application launch.

Real three/four-pane hosted tests verify actual populated WPF frame and control
bounds, independent content, compact rename/preview operability and honest
occupied-pane metadata. The compact ledger keeps actions visible with minimum
28-DIP controls, and short-pane bands preserve content in 182-DIP side panes.
Selection signals and editor selection are pure white; the canvas stays pure
black. The original deferred-before-editor-focus traces did not prove the
already-focused blur or shell-callback cases; the independent review repairs
below add that missing evidence. Unload aborts every instance's work and
releases capture without adding polling timers.

Extraction initially changed only 119 pixels in the New Folder label, bounds
`x=994..1053, y=212..219`. A pixel diff localized the change to the added full-size
`ClipToBounds` compositing boundary. Keeping the original full-size composition
unclipped and clipping only compact panes restored **zero differing pixels** and
the exact approved PNG hash. No golden or approved hash was changed.

The gate now reads the actual ExplorerSurface owning files while retaining and
expanding the memory-only capability ban to shell/control/workspace code. The
publication manifest includes both new control files and the existing workspace
and scenario files. The current counts above come from actual receipts. The
previously documented Escape-focus flake remains unexplained; this milestone
neither suppresses it nor claims to have diagnosed it. No live theme/Shell
session, dependency/Pi change, service restart, injection or remote publication
was performed.

## Independent review blocker repairs (2026-10-13)

Four vertical RED-to-GREEN cycles reproduced the reviewed defects before each
minimal fix. All original tests and assertions were retained; no retry filters
or native keyboard injection were used.

- Secondary title activation left real keyboard focus in the primary ledger;
  routing F2 from `Keyboard.FocusedElement` renamed the primary selection.
  The fixed title path queues the clicked session's ledger, visible preview or
  retained rename editor. Existing click/jitter/drag/capture-loss/double-click
  assertions still pass; automatic layout restoration remains non-focusing.
- Already-focused rename blur reactivated the departing owner and stole Search
  focus for both valid and invalid names. Passive blur now commits/validates
  without activation or replacement focus; explicit Enter retains its existing
  focus/validation behavior. Four hosted cases prove valid/invalid state plus
  Search and foreign-Explorer focus/active-identity isolation.
- Shell callbacks stole intervening Search focus after secondary minimize,
  close and Task Manager rail return even with unchanged owner/lifecycle.
  They now check captured keyboard-focus origin alongside the existing
  generation/load/owner guards. All three new real hosted traces pass.
- The actual negative gate expressions missed 24 forbidden Explorer insertions
  across full, hidden and compact-template content. The image, geometry,
  gradient/glow and device-content bans tested in that round examine both
  owning texts without changing their strings/regexes or capability checks.
  That scratch AST-backed probe rejected its 60 shell/Explorer mutations from
  ten canaries; this was real but partial coverage, not an inventory of all
  applicable negative predicates. Published XAML and canonical stayed identical during
  the probe. One initial probe setup failed on the shell Canvas insertion point;
  its output is preserved separately from the subsequent behavioral RED.

Evidence is in the local Hermes scratch bundle
`jarvis-multi-explorer/fix-round1`: `issue1-red.log`, `issue1-green.log`,
`issue2-red.log`, `issue2-green.log`, `issue3-red.log`, `issue3-green.log`,
`issue4-red-behavior.log`, `issue4-green.log`, and
`fix-round1-verification.json`. That round's final receipts recorded Release
`--warnaserror` (zero warnings/errors), three consecutive 30/30 workspace and
48/48 layout runs, and the full 26-check preview gate. Its logs and receipts
remain unchanged; the current table above reflects the later verification below.
Canonical `F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2`
and Task Manager `3971C3ECB2D725C9F5EB12F6F95A95E43600E16EF41D4F91DED57055A2BDEACF`
remain unchanged. The historical Escape-focus flake remains unexplained;
these passing runs do not diagnose it. This repair adds no OS/filesystem
capability, dependencies, models, live Shell changes or publication authority.

## Residual independent-review blockers (2026-10-13)

Two separate vertical behavioral RED-to-GREEN cycles repaired only the remaining
focus-scheduling and negative-XAML-policy paths.

- Actual `CurrentLayoutButton.Click` overwrote newer real Search and foreign
  Explorer keyboard focus after Input dispatch. Its untracked operation also
  remained Pending after the routed Unloaded teardown event or a second click.
  The explicit rail request now uses the existing guarded/cancellable
  `QueueOwnedFocus` path. Five hosted scenarios retain the actual operation,
  assert Aborted before pumping on teardown/supersession, preserve newer focus
  and active identity, and prove normal button-to-selected-item focus without
  pre-focusing the rail, including unchanged preview geometry and Escape rules.
  An initial GREEN fixture incorrectly demanded Pending after foreign owner
  activation, which already cancels synchronously. That failed output remains
  recorded; the corrected fixture additionally requires actual Aborted status,
  and its confirmed behavioral RED was rerun against the unchanged handler.
- The remaining owning-XAML bans for `TaskbarStartButton`, `StartFlyout`, the
  obsolete rail MouseMove/layout Click handlers, and `x:Name="HueSlider"` still
  examined only the shell. The copied/extended actual-AST probe demonstrated
  15 missed Explorer insertions before the fix and 90/90 detected mutations
  afterward: 15 canaries x two owners x three placements, counted from real
  traces. All applicable desktop-owner negative predicates now use the combined
  owning texts. Exact matching rules, positive roles, window-root constraints
  and capability bans are unchanged. No published XAML or canonical is written
  by the probe; the previous 60/60 record remains partial historical evidence.

The remaining shell Dispatcher callbacks were audited: only the existing
tracked Input focus helper schedules deferred focus; high-contrast notification
and selected-item reveal are non-focus work and were left unchanged.
Release `--warnaserror` passed with zero warnings/errors; workspace 30/30 and
extended layout 53/53 each passed three consecutive unfiltered runs. Edge 25/25,
Task Manager surface 10/10, full preview 26/26, platform 16/16 and publication
boundary for 535 files also passed. The desktop and Task Manager hashes above
are exact and unchanged. Local evidence is in
`jarvis-multi-explorer/fix-round2`: `issueA-red-confirmed.log`,
`issueA-green-confirmed.log`, `issueB-red.log`, `issueB-green.log`, and
`fix-round2-verification.json`. HEAD and the existing index bytes/entries were
preserved; no owned preview process was left running.

The historical Escape-focus flake remains **UNDIAGNOSED**. These defects and
passing runs are not proof of its cause or repair. This remains hosted
own-process evidence only; no auto-creation during rail commit, real filesystem,
OS launch, native Shell/injection, package/Pi, palette or golden changes were
introduced.

## Promoted-active stacking repair (2026-10-13)

One additional bounded RED-to-GREEN repaired only owned-frame stacking.
`UpdateOwnedWindowZOrder` previously sorted solely by historical activation
ordinal. Real taskbar New/minimize promoted the still-visible primary Explorer
without advancing its ordinal; explicit EqualColumns restoration retained the
newer secondary's history. A following rail Maximized commit left that extra
right-pane frame above the active primary. The permanent real-hosted scenario
failed with active Z=6/secondary Z=7 after proving unchanged identity, actual
external keyboard focus, ordinals and extra bounds. The sorter now prioritizes
the current active identity above all owned frames and retains the existing
ordinal/registration ordering among non-active frames; no lifecycle, geometry,
activation-history or focus path changed.

The same permanent scenario also exercises promoted Task Manager with the
primary Explorer closed, using the real System button and Explorer session
menu/button. Both cases preserve retained surface/workspace identities, active
ID, focus origin, registration/activation history, extra visible bounds and
lifecycle; neither rail commit allocates a session or reactivates the promoted
body. The actual GREEN receipt reports 54/54 (18 coordinator, 36 hosted), and
the gate count was derived from that receipt. All prior assertions remain.
A first scratch targeted adapter build failed nullable warnings-as-errors;
that setup log is preserved and is not counted as behavioral RED. The corrected
adapter compiled cleanly before the confirmed stacking RED and unchanged test
GREEN.

Release `--warnaserror` passed with zero warnings/errors. Workspace 30/30 and
layout 54/54 passed three consecutive unfiltered runs; edge 25/25, Task Manager
10/10, full preview 26/26, platform 16/16, publication for 535 files, and the
unchanged fix-round2 actual-AST 90/90 canary probe also passed. Canonical
`F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2`
and Task Manager
`3971C3ECB2D725C9F5EB12F6F95A95E43600E16EF41D4F91DED57055A2BDEACF`
remain exact. A byte-exact scratch copy of the parent's `ZOrderRepro` now exits
0/`not-reproduced`, active Z=7 above secondary Z=6, preserving active ID, external
focus and both rectangles. The old `z-order-repro/reproduction.json` and PNG are
unchanged; the new output lives separately under `fix-zorder`.

Local proof is in `jarvis-multi-explorer/fix-zorder`: the confirmed RED/GREEN
logs, differential output and `fix-zorder-verification.json` contain actual
commands, exits, receipts and source hashes. HEAD/index remain unchanged and
these scoped worktree edits are left unstaged for parent freeze/review.
The historical Escape-focus flake remains **UNDIAGNOSED**, not repaired or
suppressed. Fresh Explorer auto-fill during rail commit remains deferred. No
new feature, real filesystem/OS launch, native Shell/service activation,
dependency, palette or golden change is included; this is own-process evidence.

## Scoped semantic cleanup (2026-10-13)

Six reviewed cleanup findings were applied: sole configured title-button IDs,
shared stateless visual traversal, shared Explorer hide sequencing, direct
dynamic title-button Click wiring, the existing active-Explorer fast return,
and layout-result presentation completion. Only the five immediately adjacent
caller copies were removed; failed results still complete presentation. Named
lifecycle wrappers, announcements, focus guards, primary aliases and Task
Manager-specific lifecycle paths remain. Hidden ContextMenu rows are unchanged.

The new reflection-discovered WPF traversal regression compiled before failing
because the helper was absent, then passed with the unchanged extracted bodies.
Every small refactor retained the existing unfiltered behavioral tests. Final
Release `--warnaserror`, three consecutive workspace 30/30 and layout 55/55
runs, edge 25/25, Task Manager 10/10, vector 13/13, preview 26/26, platform 16/16,
publication for 536 files and the unchanged actual-AST 90/90 probe passed. The
canonical, Task Manager render, palette and historical evidence remain exact.

Physical production-source metrics, including the new 47-line helper:
DesktopShellSurface 2815 → 2769 lines, ExplorerSurface 673 → 634;
combined 3488 → 3450 lines and 128214 → 126935 bytes. The 60-line regression
means all edited C# totals instead grow by 22 lines / 2280 bytes. Local commands,
receipts, hashes and per-file metrics are recorded in
`jarvis-cleanup/implementation/cleanup-verification.json` under the Hermes
scratch root. The implementation receipt was captured before staging; Release
remains ready. The historical Escape flake stays **UNDIAGNOSED**, and fresh
auto-fill remains deferred.

## Second bounded model cleanup

Eight approved equivalence-only changes reuse selection clearing, history
transitions, seeded-file presentation and folder construction; avoid splitting
reserved-name stems; read committed layout state directly; remove a redundant
remembered-preset availability consultation; and use two named value-tuple
projections. No feature, UI, authority, dependency, lifecycle or focus change.
Compile-first reflection tests for the two new private helpers failed only
because each helper was absent, then passed with the unchanged operation order.
Each small refactor passed its real unfiltered runner before the next step.
Literal contracts and before/after runtime receipts preserve all 13 seed nodes,
six complete location snapshots, seven owned preview requests, synchronous
reentrant Changed traces and layout metadata/placement ordering.

Release `--warnaserror`, three consecutive workspace 40/40 and layout 62/62
runs, edge 25/25, Task Manager 10/10, vector 13/13, preview 26/26, platform 16/16,
publication for 536 files and the actual-AST 90/90 probe passed. All original
test bodies/assertions and gate predicates are byte-exact apart from the two
receipt-derived scenario totals. Both approved render hashes are unchanged.

Physical CRLF production metrics include all helper code: ExplorerWorkspace
830 → 801 lines / 23757 → 22920 bytes; coordinator 667 → 645 / 20485 → 19837.
Combined production is 1497 → 1446 lines / 44242 → 42757 bytes. Regression tests
separately grow by 720 lines / 51742 bytes (10 workspace and 7 coordinator cases).
Seed presentation reuse adds seven bounded constructor extension parses for
maintenance consistency, not an allocation/latency gain. Tuple projections
remove temporary anonymous reference constructions; CPU/latency was not measured.
Scratch commands, raw stdout, receipts, source-transform proof, hashes and
metrics are in `jarvis-refine-state/implementation/verification.json` under the
Hermes scratch root. The implementation receipt was captured before staging;
the Escape flake stays **UNDIAGNOSED**.
