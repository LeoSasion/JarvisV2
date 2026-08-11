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
bitmap-free `ApertureFrame` with subtractive open 90-degree contours, miter
joins and two bounded point/square focus junctions; no reference layout or
feature set is copied.
All accent geometry consumes one shared RGB frame. Component-level glow is
intentionally absent; particles, glow and post processing are reserved for a
future desktop-global, film/game-engine-style VFX parameter stack. Its A/C/D
presets, continuous hue slider and four color effects are preview controls
only, not the future VFX editor. Deterministic 1600x900 renders and the latest
source/implementation comparison pass for the approved style-only scope.
Neither path contacts or modifies the live Shell.

The sixth slice, `Jarvis.Win10.DesktopStyleSession`, binds the exact Win10 host
profile and the read-only desktop/taskbar topology to the common bounded
desktop text-color session. It exposes read-only inspect and plan commands plus
an explicitly confirmed 10–60 second preview using the A/C/D Neural Void
colors. The original scalar value and exact HWND/PID/TID identity are persisted
before SET. Apply and rollback each synchronously redraw only that admitted
FolderView, and timeout, Ctrl+C and exceptions converge on read-back-verified
rollback. This path does not inject a module, start Windhawk, change the
registry, restart Explorer, move icons, or style Explorer/taskbar internals.

The seventh slice, `Jarvis.Win10.ExplorerCaptionPlan`, starts the separate
Explorer backend with a read-only, single-window DWM caption inspection. It
requires exactly one visible `CabinetWClass` tied to the desktop Shell PID and
records HWND/PID/TID, rectangle, topology hash and the current
`DWMWA_USE_IMMERSIVE_DARK_MODE` value. Its 10–60 second plan is deliberately
non-executable: the project imports `DwmGetWindowAttribute` but contains no
window write API.

The eighth slice, `Jarvis.Win10.ExplorerCaptionSession`, implements the
journaled 10–60 second dark-caption transaction for one explicitly selected
Explorer HWND. The original boolean and HWND/PID/TID/class identity are durable
before SET, and TTL, Ctrl+C and exceptions converge on an independent,
readback-verified rollback. This non-module session is built and audited but
its first approved write produced a `0 → 1` API readback with zero changed
pixels across the 36,448-pixel title-bar sample. The target HWND then retired
while Explorer PID 1244 remained stable. A separately approved redraw build
proved the same exact HWND, `0 → 1 → 0`, apply/rollback repaint requests and
verified rollback, but the full before/during/after PNG hashes were identical.
With `AppsUseLightTheme=1`, the exact profile now revokes the caption-write
capability and retains only read-only planning until a different documented
Win10 design is reviewed.

The ninth slice, `Jarvis.Win10.ExplorerCaptionOverlay`, is that safe alternate
design. It owns a separate transparent WPF window and positions a 32-pixel
Neural Void band over one exactly admitted Explorer caption only while that
Explorer window is foreground. The overlay is mouse-transparent,
non-activating, absent from the taskbar, bounded to 10-60 seconds and closes if
the target HWND/PID/TID/class retires. It never writes an Explorer window
attribute, sends Explorer a message, loads code into Explorer, restarts the
Shell or changes the registry. The disabled DWM caption writer remains
disabled. A live own-process run on the separate Explorer PID mode changed
35,744 pixels only inside the 32-pixel caption band; the before and post-TTL
images were byte-for-byte identical.

The tenth slice, `Jarvis.Win10.TaskbarEdgeOverlay`, extends the same canary
strategy to the classic Win10 taskbar without covering its controls. A
separate transparent, mouse-through, non-activating WPF window binds one exact
bottom-horizontal primary `Shell_TrayWnd` and draws only an eight-DIP
shared-RGB vector signal along its top edge. It retreats behind fullscreen,
closes on target or geometry drift and expires after a confirmed 10-60 second
TTL. Start, task buttons, notification area, clock and Show Desktop remain
native. No Explorer property is written and no code is loaded into the Shell.
The implementation and deterministic safety model are offline-verified; the
v1 rail has not yet been described as live visually verified. See
`docs/WINDOWS10-TASKBAR-EDGE-OVERLAY.md`.

The platform-neutral `Jarvis.VisualEffects` library and
`neural-void-global-vfx-v1` contract now define 30 typed
parameters across five particle modules and five ordered post effects, plus
three bounded quality profiles. The Win10 RGB model is the first consumer and
compiles the versioned inert preset plus the shared semantic visual frame in
fail-closed tests. Every node remains disabled, the GPU backend is unselected,
and no VFX renderer or parameter editor is implemented. The same common
library now compiles a backend-neutral retained
point/line/polyline/arc/compound-path/rectangle/ellipse/plane command buffer.
The first Win10 WPF adapter now consumes that buffer for the owned preview's
static planes, datums, junction paths, reusable aperture contours,
orthogonal registration marks and per-frame focus geometry, renders every supported
primitive kind in tests and fails closed on invalid input.
Four exact PNG hashes on Windows 10 build 19045 prove the migration does not
lock the reviewed WPF output on the exact target renderer. See
`docs/NEURAL-VOID-GLOBAL-VFX-CONTRACT.md`.
See `docs/WINDOWS10-DESKTOP-STYLE-SESSION.md` for the exact-host-bound,
non-module desktop preview.
See `docs/WINDOWS10-EXPLORER-CAPTION-PLAN.md` for the read-only first Explorer
backend plan.
See `docs/WINDOWS10-EXPLORER-CAPTION-SESSION.md` for the bounded
single-window transaction and its two failed visual observations.
See `docs/WINDOWS10-EXPLORER-CAPTION-OVERLAY.md` for the owned, click-through
fallback and its visual verification procedure.

The owned `Jarvis.Win10.NativeStyleProbe` now consumes the same RGB frame for
its client surface while retaining the single reviewed Win10 dark-caption
attribute. Arbitrary caption color is not claimed. The shared
`Jarvis.PiAgentHost` now creates a root-confined, in-memory SDK session and
uses a desktop-owned, provider-neutral model broker across multiple turns. The
offline provider proves two ordinary responses, one real `read` tool round trip
and active generation cancellation. Each turn exposes a bounded, ordered
stream of text, tool and terminal events to the native WPF Control Center. The
sidecar remains non-prompting unless the reviewed current-user named pipe is
present. The desktop now also has an opt-in OpenAI Responses provider and an
explicit CurrentUser-DPAPI setup flow; no credential crosses the pipe and no
live model call is part of the offline receipts. `Jarvis.ControlCenter` owns a
resizable conversation panel, orderly runtime shutdown and a portable
self-contained Node/Pi package layout.

See `WINDOWS10-HANDOFF.md`,
`docs/WINDOWS10-NATIVE-STYLE-PROBE.md` and
`docs/WINDOWS10-SHELL-SURFACE-INVENTORY.md` for host commands and receipts.
See `docs/WINDOWS10-SURFACE-SELECTOR-CANDIDATES.md` for the offline selector
contract and `docs/WINDOWS10-NEURAL-VOID-RGB-INTENT.md` for the approved
visual intent. See `docs/WINDOWS10-NEURAL-VOID-OWNED-PREVIEW.md` for the
runnable preview and reproducible screenshot.

The Shell probe reads window structure but does not collect titles or modify
Explorer. Neither probe uses Windhawk.
The next visible work continues from the desktop, Explorer and taskbar
canaries toward one narrowly hosted, reversible native Shell theme path. Pi
conversation and supervised self-iteration remain background runtime concerns,
not the center of the desktop composition.
Future projects continue to use `Jarvis.Win10.<Feature>` and must not reuse
Win11 private symbols, selectors, DWM backdrops or module IDs.

The reviewed Win10 BridgeCore, CallWndProc and exact-thread transport research
remain platform-owned forks. The published native package is offline-only and
contains only the empty pass-through callback DLL plus its source-bound
receipt. It contains no collector executable, does not use the transport, and
cannot activate Explorer. The compatible official live entry is fixed blocked
until unload, Hook-removal and callback-drain proof is closed. See
`docs/WINDOWS10-EXACT-THREAD-EXPLORER-COLLECTOR.md`.
