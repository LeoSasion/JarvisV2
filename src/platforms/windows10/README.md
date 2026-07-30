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
accent presets over the continuous HSV engine in the shared
`Jarvis.VisualEffects` library. The desktop composition
contains no peripheral controls or illustrations; a future external device
bridge may consume the same RGB frame without becoming a Shell dependency.

The fifth slice, `Jarvis.Win10.NeuralVoidPreview`, renders that shared RGB
frame as a runnable own-process WPF desktop, Explorer and classic Win10
taskbar. The selected fourth visual variant is encoded as a reusable,
bitmap-free `ApertureFrame` with subtractive open contours, tangent arcs and
two bounded focus junctions; no reference layout or feature set is copied.
All accent geometry consumes one shared RGB frame. Component-level glow is
intentionally absent; particles, glow and post processing are reserved for a
future desktop-global, film/game-engine-style VFX parameter stack. Its A/C/D
presets, continuous hue slider and four color effects are preview controls
only, not the future VFX editor. Deterministic 1600x900 renders and the latest
source/implementation comparison pass for the approved style-only scope.
Neither path contacts or modifies the live Shell.

The platform-neutral `Jarvis.VisualEffects` library and
`neural-void-global-vfx-v1` contract now define 30 typed
parameters across five particle modules and five ordered post effects, plus
three bounded quality profiles. The Win10 RGB model is the first consumer and
compiles the versioned inert preset plus the shared semantic visual frame in
fail-closed tests. Every node remains disabled, the GPU backend is unselected,
and no VFX renderer or parameter editor is implemented. The same common
library now compiles a backend-neutral retained
point/line/polyline/arc/compound-path/plane command buffer. The first Win10 WPF
adapter now consumes that buffer for the owned preview's static planes, datums,
junction paths and reusable aperture contours, renders every supported
primitive kind in tests and fails closed before committing partial geometry.
Four exact PNG hashes on Windows 10 build 19045 prove the migration does not
change the current WPF pixels. See
`docs/NEURAL-VOID-GLOBAL-VFX-CONTRACT.md`.

The owned `Jarvis.Win10.NativeStyleProbe` now consumes the same RGB frame for
its client surface while retaining the single reviewed Win10 dark-caption
attribute. Arbitrary caption color is not claimed. The shared
`Jarvis.PiAgentHost` now creates a root-confined, in-memory SDK session and
uses a desktop-owned, provider-neutral model broker across multiple turns. The
offline provider proves two ordinary responses, one real `read` tool round trip
and active generation cancellation. Each turn also exposes a bounded, ordered
stream of text, tool and terminal events for future WPF binding. The default
sidecar remains non-prompting unless that reviewed current-user named pipe is
present; no authenticated production provider or credential is connected yet.
The shared host now also folds the stream into bounded, immutable conversation
snapshots, and `Jarvis.ControlCenter` compiles a non-visual WPF binding
adapter. No conversation panel or XAML layout has been added.

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
conversation panel and authenticated desktop model provider while real
Explorer remains untouched.
Future projects continue to use `Jarvis.Win10.<Feature>` and must not reuse
Win11 private symbols, selectors, DWM backdrops or module IDs.
