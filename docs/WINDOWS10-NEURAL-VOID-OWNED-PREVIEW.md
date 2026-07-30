# Windows 10 Neural Void owned preview

`Jarvis.Win10.NeuralVoidPreview` is the first runnable rendering of the
approved D / Neural Void direction. It draws a simulated desktop, Explorer
window and classic Win10 taskbar entirely inside its own WPF process.

The current visual pass implements the user-selected fourth aperture variant.
The generated frame remains a style reference only: its blank composition,
panel placement and decorative marks are not copied into the simulated Shell.
The retained language is:

- one monochrome accent over neutral black surfaces;
- subtractive one-pixel contours with deliberately missing edge segments;
- tangent corner arcs, tiny registration squares and long quiet datums;
- exactly two local RGB focus junctions drawn as points, crosses and rings;
- mathematical point, line, arc and plane geometry with no bitmap decoration.

`ApertureFrame` is the reusable window-contour primitive. It keeps its static
graphite contour and dynamic RGB focus junction in separate
`DrawingVisual` objects. `NeuralVectorLayer` separately retains the desktop
datums and redraws only its two small signal junctions. `StreamGeometry`
paths are frozen after construction. This keeps RGB animation bounded and
makes the same contour grammar reusable by Explorer, Control Center and the
future Pi conversation surface.

No local glow shader, radial glow brush or particle emitter is implemented in
these components. Every colored contour and junction binds to the same
`RgbFrame`; neutral structure does not change hue. Glow, particles, trails,
distortion and color grading are reserved for a future global VFX compositor
so effects can be coordinated across the complete desktop instead of being
duplicated by individual controls.

That future compositor will use a film/game-engine-style parameter stack for
spawn, motion, lifetime, appearance, color and size over life, material,
render order and post processing. A Galaxy View-like parameter surface may
control those values later. The platform-neutral parameter catalog is now
compiled and tested, but the current preview contains neither the runtime nor
its controls. See `NEURAL-VOID-GLOBAL-VFX-CONTRACT.md`.

![Neural Void owned-process preview](screenshots/jarvis-win10-neural-void-owned-preview.png)

The preview covers the eight reviewed roles:

- desktop icon list;
- Explorer command bar, content host and folder view;
- taskbar Start button, task list, notification area and clock.

It does not draw a keyboard, mouse, linked-device panel or RGB device
controller inside the desktop. A/C/D presets and the continuous hue slider
belong to the outer preview host only.

## Interactive preview

```powershell
dotnet .\src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\bin\Release\net8.0-windows\jarvis-win10-neural-void-preview.dll show
```

The host offers A cyan, C amber and D emerald shortcuts, a 0–360 degree hue
slider and static, breathe, spectrum and signal-pulse effects. Every frame is
computed by the shared `Jarvis.Win10.RgbThemeModel`.

## Deterministic evidence

```powershell
dotnet .\src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\bin\Release\net8.0-windows\jarvis-win10-neural-void-preview.dll `
  render .\preview.png 156.235294 signal-pulse 0.25
```

The render command creates a 1600x900 PNG and a JSON receipt. The checked-in
screenshot uses D neural emerald at the full-brightness point of the
signal-pulse effect. The latest source/implementation comparison is recorded
in `design-qa.md` and passes for the approved style-only scope.

## Safety boundary

This is not a desktop replacement and does not inspect or style a real Shell
window. The project has no native imports, process enumeration, registry
access, device I/O or provider SDK. It cannot mutate Explorer or physical
devices, and it cannot activate a module.
