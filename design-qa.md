# Design QA - Win10 Neural Void aperture grammar

## Visual truth

- Source:
  `%USERPROFILE%\.codex\generated_images\019fadf9-6d36-7352-9c2c-24f1bd9e17d2\call_cXINEJfYYCzX7K1sHSPjMfGk.png`
- Selection: the fourth displayed variant from the latest ideation set.
- Scope: reproduce only its subtractive contour, tangent-arc, registration
  mark, negative-space and local-focus grammar. Do not copy its blank layout,
  functions or decorative arrangement.
- Latest refinement: local components must not implement glow. They draw
  shared-RGB point/line/arc/plane geometry only; particles and post effects
  belong to a future global VFX system.

## Implementation target

- Owned-process WPF preview:
  `src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview`
- Reusable contour:
  `src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\ApertureFrame.cs`
- Desktop vector layer:
  `src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\NeuralVectorLayer.cs`
- Implementation capture:
  `artifacts\win10-neural-void-preview-tests\d-emerald.png`

## Viewport and state

- Source pixels: 1600 x 900.
- Implementation pixels and WPF surface: 1600 x 900 at 96 DPI
  (1x density).
- Theme state: D neural emerald, `#00FF9A`, `signal-pulse`, phase `0.25`.
- Crop and density normalization: none required.

## Comparison evidence

- Combined full-view input:
  `artifacts\win10-neural-void-preview-tests\variant4-qa-comparison.png`
- Left half: 1600 x 900 source visual.
- Right half: 1600 x 900 implementation output.
- Both halves were inspected together at their native state and density.

## Findings

- No P0, P1 or P2 fidelity issue remains for the approved style-only scope.
- Intentional divergence: the source uses luminous focus glow, while the
  latest user direction explicitly defers all glow to a future global system.
  The implementation therefore uses crisp RGB points, crosses and rings.
- The source's empty composition, panel placement and decorative arrangement
  are not fidelity targets. The implementation applies the approved contour
  grammar to the existing Win10 desktop, Explorer and taskbar role map.

## Required fidelity surfaces

- Contours: open one-pixel graphite frames, missing edge segments, tangent
  corner arcs and registration marks are visibly present.
- Focus: two bounded junctions use the shared emerald RGB frame without local
  glow, blur or drop shadow.
- Color: neutral black surfaces remain stable while the accent family is
  consistently applied to active geometry and state.
- Asset fidelity: the implementation is vector-only and has no bitmap
  decoration dependency.
- Copy and layout: intentionally outside the selected style-only scope; the
  retained content remains legible at 1600 x 900.

## Comparison history

1. Offscreen `RenderTargetBitmap` capture after the variant-four build:
   transparent.
2. Owned preview process launch: process exited before exposing a usable
   window handle in this automation desktop.
3. Screen capture attempt: blocked with `The handle is invalid`.
4. Computer Use initialization: unavailable because its Node kernel assets
   could not be created in this desktop session.
5. A later deterministic Release render produced a valid 1600 x 900 image.
   The source and implementation were then recomposed into one 3200 x 900
   comparison and inspected together.

## Verified evidence

- Fixed .NET SDK 8.0.423 Release build: passed, 0 warnings and 0 errors.
- Owned-preview audit: 11/11 passed, including four visible and distinct RGB
  render states.
- RGB theme contract: 11/11 passed, including `aperture-contour-v1`, shared
  RGB binding and the reserved global VFX boundary.
- Combined source/implementation visual comparison: passed for the approved
  style-only scope.
- Kill switch remains armed; no Windhawk activation, Explorer injection,
  restart, registry write or system-file modification occurred.

## Follow-up polish

- [P3] Revisit graphite contour opacity after an interactive ClearType capture.
- [P3] Revisit the two point/ring junction weights after real Win10 compositor
  validation.
- Design the global particle/post-effect parameter model separately; do not
  add local glow to individual components.

final result: passed
