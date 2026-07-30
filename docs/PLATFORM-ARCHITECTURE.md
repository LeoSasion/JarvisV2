# Platform architecture

JarvisV2 uses one shared layer and separate Windows-family backends. The
physical layout is an enforcement boundary, not a statement that both
platforms already have feature parity.

```text
src/
  common/                     reviewed Windows 10/11 candidates
  platforms/
    windows10/                new Win10 implementation only
    windows11/                preserved Win11 implementation
mods/
  common/                     protocol source shared by reviewed builds
  windows10/                  future Win10 modules with new IDs
  windows11/                  current build-locked modules
tests/native/
  common/
  windows10/
  windows11/
```

The `scripts/` directory remains flat. These scripts are stable repository
entry points and may dispatch to platform-specific paths; moving them would
create churn without improving backend separation.

## Common layer

`src/common` currently contains:

- `Jarvis.ControlCenter`;
- `Jarvis.DesktopStyleProbe`;
- `Jarvis.DesktopStyleSession`;
- `Jarvis.PiAgentHost`;
- `Jarvis.VisualEffects`.

These projects keep their existing namespaces and assembly names. “Common”
means they are candidates for both Windows 10 and Windows 11, not that every
operation has already been validated on both systems. Each live operation
still requires a platform compatibility profile and exact target evidence.

`Jarvis.PiAgentHost` pins the official Pi Agent SDK behind an isolated Node.js
sidecar and bounded LF-delimited JSONL. The managed desktop bridge now owns the
no-shell child lifecycle, imports the real package, proves a clean child
environment and creates one in-memory SDK session for one canonical workspace.
Its custom `read`, `grep`, `find` and `ls` tools reject path escape and reparse
points. Prompting is admitted only when the desktop owns a current-user local
model-broker pipe; resource discovery, sidecar provider network access,
credential transport and mutation tools remain disabled. The desktop now owns
a provider-neutral, multi-request model broker with bounded current-user pipes,
validated read-only tool events and a managed response pump. The pump now
publishes a bounded, ordered, single-consumer turn stream for future WPF
binding. A revisioned conversation-state adapter now consumes that stream,
tracks text and tools, enforces one active turn and dispatches immutable
snapshots through a captured synchronization context. `Jarvis.ControlCenter`
compiles an `INotifyPropertyChanged` wrapper without exposing a panel yet. A
desktop-owned runtime now composes the broker, sidecar, admitted session and
conversation state, then quiesces submissions and cancels an active turn
before orderly shutdown. Its offline provider proves multi-turn Pi streaming,
a real `read` tool round trip and active-turn cancellation. A future
authenticated provider and reviewed desktop conversation surface will consume
these admitted boundaries; the agent runtime will never be loaded into
Explorer. See `docs/PI-AGENT-DESKTOP-HOST.md` and
`docs/PI-AGENT-DESKTOP-RUNTIME.md`.

Neural Void visual effects use one platform-neutral
`neural-void-global-vfx-v1` parameter contract for Win10 and Win11. It defines
the shared particle-module vocabulary, render order, post stack, quality
budgets and RGB binding while leaving every effect disabled.
`Jarvis.VisualEffects` now implements this contract, the RGB sampler, a
linear-sRGB semantic signal frame and schema-versioned inert preset validation
without WPF or native Windows dependencies. It also defines a retained
point/line/polyline/arc/compound-path/plane command buffer with deterministic
ordering, semantic color binding and explicit quality budgets. Compound paths
retain multiple continuous line/arc figures as one draw operation. Platform
backends may later implement separate renderers against that common contract;
the contract does not authorize Shell integration. See
`docs/NEURAL-VOID-GLOBAL-VFX-CONTRACT.md`.

The current Supervisor remains under `src/platforms/windows11` because its
compatibility inspector, module allowlist, recovery lease and command text are
bound to the reviewed Win11 modules. A future Win10 supervisor may extract
small shared state primitives only after the first Win10 vertical slice proves
what is actually common.

## Windows 11 backend

The Windows 11 tree preserves all reviewed source identities:

- exact build and image fingerprinting;
- `Taskbar.View.dll` private-symbol experiments;
- DWM color and system-backdrop previews;
- Explorer WinUI/XAML selector, transport, TAP and transaction foundations.

Moving the files does not authorize them. The current platform matrix retains
`activationPermitted=false` and `liveExplorer=not-run`; Windhawk remains
quarantined.

## Windows 10 backend

The Windows 10 tree now contains its first exact-host vertical slice:
`Jarvis.Win10.NativeStyleProbe`. It reads the actual target identity, matches
the embedded `win10-22h2-19045.6466-x64` profile and may style only a window
owned by the probe process. It must not:

- widen the Win11 compatibility range;
- reuse a Win11 module ID;
- assume `Taskbar.View.dll`, Mica, rounded-corner DWM attributes or
  `FileExplorerExtensions.*` XAML nodes exist;
- fall back to the Win11 backend after an admission failure.

Common visual intent should be represented as small tokens such as color,
density and icon size. Platform adapters translate those tokens only into
operations explicitly supported by their verified host.

The first adapter therefore uses only the Win10 dark-caption attribute. It
does not pretend that Win11 corner, caption-color or system-backdrop
attributes are available. See `docs/WINDOWS10-NATIVE-STYLE-PROBE.md`.

The second adapter slice is a read-only topology probe. Shared Win10 host
identity moved into `Jarvis.Win10.HostAdmission` after the first two consumers
proved the boundary. `Jarvis.Win10.ShellSurfaceProbe` uses that gate before
enumerating bounded, text-free desktop, Explorer and classic-taskbar class
trees. See `docs/WINDOWS10-SHELL-SURFACE-INVENTORY.md`.

The third slice is the pure-offline
`Jarvis.Win10.SurfaceSelectorModel`. It resolves eight exact class paths
against a sanitized excerpt of the observed topology and fails closed on
profile, role, shape, uniqueness, parent or capability drift. It defines no
color, material, spacing, icon size or other visual intent. Four image
concepts must be reviewed before that visual boundary can advance. See
`docs/WINDOWS10-SURFACE-SELECTOR-CANDIDATES.md`.

That visual review selected concept D / Neural Void for the Windows desktop.
`Jarvis.Win10.RgbThemeModel` owns its neutral palette and A/C/D recommended
colors while consuming the common continuous HSV frame engine. The Windows
visual adapter and a
future external device-lighting bridge are separate consumers of the same
frame; Shell behavior never depends on physical-device availability. Keyboard,
mouse and device controls are intentionally absent from the desktop
composition. See `docs/WINDOWS10-NEURAL-VOID-RGB-INTENT.md`.

`Jarvis.Win10.NeuralVoidPreview` is the first renderer of that intent. It is a
standalone WPF process whose desktop, Explorer and classic taskbar are all
simulated controls. It consumes the shared RGB frame engine, exposes A/C/D
shortcuts plus continuous hue and effects, and implements the selected fourth
visual variant as a reusable `ApertureFrame`: subtractive open contours,
tangent arcs and two local RGB focus junctions. Local components draw only
point/line/arc/compound-path/plane geometry; every colored primitive consumes
the same RGB
frame and no local glow is implemented. The desktop layer's static planes,
datums and junction paths plus each `ApertureFrame` contour are now authored as
common retained-vector commands and rendered by a Win10 WPF adapter that
validates the semantic palette, stages a frozen drawing and fails closed
before commit. `ApertureFrame` uses one compound path so its tangent joins keep
single-draw alpha semantics; its registration squares and dynamic focus
geometry remain direct retained WPF visuals. Exact four-case PNG hashes on
Windows 10 build 19045 prove this adapter boundary is pixel-identical to the
preceding implementation.

Particles, glow and post processing are reserved for a future desktop-global
VFX compositor with a film/game-engine-style parameter stack. This boundary
prevents windows from creating competing local emitters or effect pipelines
and leaves room for a Galaxy View-like authoring surface. The compositor,
particle runtime and parameter UI are intent only in this slice.

Deterministic 1600x900 WPF evidence and the source/implementation comparison
now pass for the approved style-only scope. The renderer still has no Shell
mutation transport or physical-device integration.
See `docs/WINDOWS10-NEURAL-VOID-OWNED-PREVIEW.md`.

## Compatibility profiles

Compatibility profiles are append-only and platform-specific. A profile
contains an exact Windows family, build, UBR, architecture and relevant image
identities. “Windows 10 or newer” is never a valid native admission rule.

If no exact profile matches, the backend reports an incompatible host and does
nothing. No profile may silently select a different backend.

## Return discipline

Win10 work lands only in `common` when both platform gates prove the behavior.
Otherwise it remains in `platforms/windows10`. Returning to Win11 therefore
requires no reverse migration: switch to the Win11 backend, refresh its exact
host evidence and run the existing gates.
