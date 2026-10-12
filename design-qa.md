# Design QA - Horizon Membrane layout rail and Explorer workspace

- Current approved owned-desktop baseline:
  `docs/screenshots/jarvis-win10-current-ui-baseline.png`
- Deterministic evidence: 1600 x 900 locked-profile render,
  SHA-256 `F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2`
- Viewport: 1600 x 900 at 96 DPI
- State: black/yellow desktop, layout rail permanently visible, `FREEFORM`,
  Explorer restored at `(596, 63)` with size `930 x 667`; only the one-window
  preset is initially available
- Validation scope: own-process WPF preview only;
  `shellMutationSupported=false`, `liveExplorer=not-run`

## Layout catalog

The catalog still contains 16 stable topologies: one maximized window, six
two-window splits, eight three-window structures and one four-quadrant grid.
Only registered visible owned windows may be tiled. Opening Task Manager
enables seven presets; three- and four-window entries remain disabled.
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
- Escape from the permanently visible layout list stops auto-scroll but remains
  unhandled so the owning window's global Escape action still runs.
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

## Verification

- Release build: passed, 0 warnings / 0 errors
- Layout and edge-bar scenarios: 25 / 25 passed, including translated bounds of
  all sixteen generated rail glyphs, the frozen continuous feather mask,
  top/middle/bottom boundary states, actual adjacent-item hit testing,
  handled white-glyph pointer routing,
  nonlinear symmetric pointer pressure,
  exact endpoints, Escape routing, dynamic high-contrast handling and Explorer
  taskbar minimize/restore behavior
- Explorer workspace scenarios: 21 / 21 passed
- Owned Task Manager model and command scenarios: 10 / 10 passed
- Owned Task Manager surface scenarios: 9 / 9 passed, including host-window
  minimize/restore lifecycle, process-instance identity and in-flight restart
- Owned-window layout scenarios: 27 / 27 passed, including all six splits,
  both active-window assignments, disabled no-op, compact viewport gates,
  title-click/jitter restore preservation and actual-drag capture loss
- Control Center audit: 27 / 27 passed
- Platform layout: 16 / 16 passed
- Publication boundary: passed for 533 candidate files; no secret values printed
- Current owned-preview audit: 26 / 26 passed on the connected locked graphics
  profile; WPF vector adapter 13 / 13, canonical desktop hash matched exactly,
  and the separate owned Task Manager render passed structural validation
- Current canonical SHA-256:
  `F472A0C14D08348B49A44D878202A3F69330D78A6226244D2B2A499EE2C70AC2`
- Separate owned Task Manager render SHA-256:
  `3971C3ECB2D725C9F5EB12F6F95A95E43600E16EF41D4F91DED57055A2BDEACF`
- Current evidence state: `locked-profile-approved`. A disconnected graphics
  session still fails pixel-dependent checks closed and cannot advance either
  the canonical PNG or approved hash.

Current result: **functional interaction and current full-frame pixel approval
passed**.

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
