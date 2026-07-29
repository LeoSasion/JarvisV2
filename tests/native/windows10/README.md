# Windows 10 native tests

The first Win10-specific implementation is audited by
`scripts/Test-Windows10NativeStyleProbe.ps1`. The gate checks the exact host
profile, DWM import allowlist, owned-HWND proof, Win11 capability exclusions,
Release build and the local owned-window apply/reset roundtrip.

`scripts/Test-Windows10ShellSurfaceProbe.ps1` audits the shared host
admission, read-only user32 allowlist, bounded text-free topology reader and
the exact desktop/Explorer/classic-taskbar surface set on this host.

`scripts/Test-Windows10SurfaceSelectorModel.ps1` audits the eight-role
candidate, sanitized structural fixture, pure-offline source boundary,
Release build and fail-closed selector scenarios. It never contacts Explorer.

Future native harnesses belong in this directory. No test may load a module
into Explorer without a separately approved live gate.
