# Windows 10 backend

The first Win10 vertical slice is implemented in
`Jarvis.Win10.NativeStyleProbe`. It reads exact host and DWM state, verifies
the reviewed dark-caption attribute on an owned HWND, and provides an
interactive own-process visual surface.

The second slice, `Jarvis.Win10.ShellSurfaceProbe`, records bounded,
text-free class topology for the desktop, File Explorer and classic taskbar.
Both tools consume `Jarvis.Win10.HostAdmission`, which owns the exact
`win10-22h2-19045.6466-x64` identity gate.

The third slice, `Jarvis.Win10.SurfaceSelectorModel`, compiles eight exact
class-path candidates against a sanitized excerpt of that topology. It is a
pure offline model: it defines no visual values and has no native, process,
registry or mutation transport.

The fourth slice, `Jarvis.Win10.RgbThemeModel`, records the approved D /
Neural Void desktop direction. A cyan, C amber and D emerald are recommended
accent presets over one continuous HSV color system. The desktop composition
contains no peripheral controls or illustrations; a future external device
bridge may consume the same RGB frame without becoming a Shell dependency.

The fifth slice, `Jarvis.Win10.NeuralVoidPreview`, renders that shared RGB
frame as a runnable own-process WPF desktop, square Explorer and classic
Win10 taskbar. Its A/C/D presets, continuous hue slider and four effects are
preview controls only. Deterministic PNG rendering provides visual evidence
without contacting or modifying the live Shell.

The owned `Jarvis.Win10.NativeStyleProbe` now consumes the same RGB frame for
its client surface while retaining the single reviewed Win10 dark-caption
attribute. Arbitrary caption color is not claimed. The shared
`Jarvis.PiAgentHost` now creates a root-confined, in-memory SDK session and
proves a real streaming prompt through a desktop-owned diagnostic model broker.
The default sidecar remains non-prompting unless that reviewed current-user
named pipe is present. The managed response pump also proves active generation
can be cancelled; no production provider or credential is connected yet.

See `WINDOWS10-HANDOFF.md`,
`docs/WINDOWS10-NATIVE-STYLE-PROBE.md` and
`docs/WINDOWS10-SHELL-SURFACE-INVENTORY.md` for host commands and receipts.
See `docs/WINDOWS10-SURFACE-SELECTOR-CANDIDATES.md` for the offline selector
contract and `docs/WINDOWS10-NEURAL-VOID-RGB-INTENT.md` for the approved
visual intent. See `docs/WINDOWS10-NEURAL-VOID-OWNED-PREVIEW.md` for the
runnable preview and reproducible screenshot.

The Shell probe reads window structure but does not collect titles or modify
Explorer. Neither probe uses Windhawk.
The next visible slice may connect the admitted Pi bridge to an owned
conversation panel and production desktop model adapter while real Explorer
remains untouched.
Future projects continue to use `Jarvis.Win10.<Feature>` and must not reuse
Win11 private symbols, selectors, DWM backdrops or module IDs.
