# Design QA - Horizon Membrane compact layout rail

- Canonical baseline: `docs/screenshots/jarvis-win10-current-ui-baseline.png`
- Deterministic evidence: `artifacts/win10-neural-void-preview-tests/layout-rail-scroll.png`
- Viewport: 1600 x 900 at 96 DPI
- State: black/yellow desktop, compact layout rail open, `LeftMainRightStack`
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

Independent finish review verdict: **PASS**. Blocking findings: none.

- The clipped rail viewport is 86 x 556 and presents about ten complete items
  instead of expanding all sixteen across the work area.
- Every list item is 86 x 54. Its 70 x 42 glyph has a six-pixel top and bottom
  inset; group transitions add eight pixels of whitespace and no divider line.
- The list begins at `x=20`, and its ListBox and GroupItem templates contribute
  zero horizontal chrome or indentation. Every rail glyph element therefore
  occupies `x=28..98`; the fixed lower-left glyph uses the same 70 x 42 token and
  occupies the same range. Both element centers are exactly `x=63`.
- Pixel inspection confirms the neutral linework occupies `x=34..91`
  (`center=62.5`) in the rail and current slot alike; the yellow selected bounds
  occupy `x=29..96` (`center=62.5`) in both places. The former 6-pixel rail drift
  is zero.
- Separate 126 x 44 upper and lower hover zones sit outside the clipped list and
  cannot cover or intercept a layout item. They add no permanent arrow or rule.
  While scrolling, only the corresponding segment of the existing vertical axis
  adopts the yellow accent.
- The vertical axis remains at `x=124..126` and meets the two-pixel taskbar rule
  at `y=800`, preserving the approved orthogonal cross.
- Pure black, near-white vector content, one `#FFF000` accent, square geometry
  and the existing primary/secondary/quiet rule hierarchy remain unchanged.

## Auto-scroll behavior

- A 140 ms edge dwell starts one bounded background timer. Normal mode scrolls
  physical pixels at a stable elapsed-time-based velocity; reduced-motion and
  high-contrast modes advance one complete 54-pixel item at a slower interval.
- Moving away from an edge snaps to the nearest complete item. Reaching either
  endpoint stops immediately and prevents idle CPU work.
- Direction changes are exclusive. Wheel input, keyboard input, selection,
  closing the rail, leaving the rail or unloading the surface stops auto-scroll.
- Scrolling never mutates `CurrentLayout`, the selected data item or the fixed
  lower-left current-layout glyph.
- Opening or reopening the rail reveals the selected item. The first and last
  catalog entries are both fully reachable and fully visible.

## Verification

- Release build: passed, 0 warnings / 0 errors
- WPF retained-vector adapter: 13 / 13 passed
- Layout and edge-bar scenarios: 19 / 19 passed, including translated bounds of
  all sixteen generated rail glyphs versus the current-slot glyph
- Owned-preview audit: 15 / 15 passed
- Deterministic PNG: 1600 x 900, `#FFF000`
- Final SHA-256: `2158EEA1184EFD22CBE3B630D662F3562A02DFC27955922F4321E2D1957AD9E0`
- Empty/transparent rendering protection: fail-closed

Final result: **passed**.
