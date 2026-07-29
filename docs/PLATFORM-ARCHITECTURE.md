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
- `Jarvis.DesktopStyleSession`.

These projects keep their existing namespaces and assembly names. “Common”
means they are candidates for both Windows 10 and Windows 11, not that every
operation has already been validated on both systems. Each live operation
still requires a platform compatibility profile and exact target evidence.

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
