# Design QA — Win10 Neural Void Vector Grammar

## Visual truth

- Selected style reference 1: `C:\Users\Administrator\.codex\generated_images\019fadf9-6d36-7352-9c2c-24f1bd9e17d2\call_hvjiAOmRItrbxQTBujUSukEt.png`
- Selected style reference 3: `C:\Users\Administrator\.codex\generated_images\019fadf9-6d36-7352-9c2c-24f1bd9e17d2\call_M7RMEuV2MKX6ulnLRXfsNHYT.png`
- Scope: extract only the minimal outline, negative-space, layered rail, and signal-motion grammar. Do not reproduce either reference's layout, features, labels, or motifs.

## Implementation target

- Owned-process WPF preview:
  `src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview`
- Retained vector layer:
  `src\platforms\windows10\Jarvis.Win10.NeuralVoidPreview\NeuralVectorLayer.cs`
- Intended evidence:
  `artifacts\win10-neural-void-preview-tests\d-emerald-vector-style.png`

## Automated checks

- Release build with fixed .NET SDK 8.0.423: passed, 0 warnings and 0 errors.
- Source safety and vector architecture checks: passed.
- Kill switch remains armed; no Windhawk activation, Explorer injection, restart, registry write, or system-file modification occurred.

## Visual comparison status

The local automation desktop does not provide a usable WPF graphics handle.
`RenderTargetBitmap` output, including a minimal solid-red diagnostic visual,
is fully transparent. The generated PNG therefore cannot be used as an
implementation screenshot and no honest side-by-side visual comparison can be
performed in this environment.

The CI workflow now uploads the Win10 preview PNGs as an artifact. Visual QA
must resume from that artifact after a pull request starts the Windows workflow.
Until then, geometry, spacing, hierarchy, contrast, and motion fidelity remain
unverified.

## Final result

blocked
