# Design QA - Horizon Membrane compact layout rail

- Current approved feather baseline:
  `docs/screenshots/jarvis-win10-current-ui-baseline.png`
- Deterministic evidence: 1600 x 900 locked-profile render,
  SHA-256 `42AD07963D7BA732F5FBC3EABC3B15E28F1E38AACFA13C0243FA69870D6168E2`
- Viewport: 1600 x 900 at 96 DPI
- State: black/yellow desktop, layout rail permanently visible, `LeftMainRightStack`
  selected, Explorer restored at `(596, 63)` with size `930 x 667`
- Validation scope: own-process WPF preview only;
  `shellMutationSupported=false`, `liveExplorer=not-run`

## Layout catalog

The catalog still contains 16 stable topologies: one maximized window, six
two-window splits, eight three-window structures and one four-quadrant grid.
Every definition is an exact cover of a 12 x 12 integer grid. Startup validation
rejects duplicate IDs, orders, presets or signatures, gaps, overlaps, and any
catalog that is not closed under horizontal mirror, vertical mirror and
90-degree rotation.

## Compact rail geometry

Interaction, geometry and locked-profile pixel verdict: **PASS**.

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

## Verification

- Release build: passed, 0 warnings / 0 errors
- Layout and edge-bar scenarios: 25 / 25 passed, including translated bounds of
  all sixteen generated rail glyphs, the frozen continuous feather mask,
  top/middle/bottom boundary states, actual adjacent-item hit testing,
  handled white-glyph pointer routing,
  nonlinear symmetric pointer pressure,
  exact endpoints, Escape routing, dynamic high-contrast handling and Explorer
  taskbar minimize/restore behavior
- Owned-preview audit: 18 / 18 passed on locked Windows 10 build 19045.6466 at
  96 DPI with software rendering
- Current approved canonical SHA-256:
  `42AD07963D7BA732F5FBC3EABC3B15E28F1E38AACFA13C0243FA69870D6168E2`
- Evidence state: `locked-profile-approved`; checked-in baseline, approved hash
  and fresh observed render are byte-for-byte equal. Non-locked hosts remain
  explicitly `non-comparable-structural-pass` and cannot set pixel approval.

Current result: **passed**.
