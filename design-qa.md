# Design QA - Horizon Membrane layout rail and Explorer workspace

- Current approved owned-desktop baseline:
  `docs/screenshots/jarvis-win10-current-ui-baseline.png`
- Deterministic evidence: 1600 x 900 locked-profile render,
  SHA-256 `DE71FCCB280636402914ED8108DE22D6864870E37F8E9D2B9EF7FC4E7AA69759`
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
- Owned-window layout scenarios: 24 / 24 passed, including all six splits,
  both active-window assignments, disabled no-op and compact viewport gates
- Control Center audit: 27 / 27 passed
- Platform layout: 16 / 16 passed
- Publication boundary: passed for 533 candidate files; no secret values printed
- Current owned-preview audit: 26 / 26 passed on the connected locked graphics
  profile; WPF vector adapter 13 / 13, canonical desktop hash matched exactly,
  and the separate owned Task Manager render passed structural validation
- Current canonical SHA-256:
  `DE71FCCB280636402914ED8108DE22D6864870E37F8E9D2B9EF7FC4E7AA69759`
- Separate owned Task Manager render SHA-256:
  `FD2DC1342ADA445B0B9515D98CD31901B7F61A0DDE79AE9C5F9805B0F2CE2412`
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
